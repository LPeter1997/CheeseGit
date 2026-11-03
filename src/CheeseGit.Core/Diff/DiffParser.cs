using System.Buffers.Text;
using CheeseGit.Core.Git;

namespace CheeseGit.Core.Diff;

/// <summary>
/// Parses git's patch output (<c>diff</c>, <c>diff-tree -p</c>, <c>diff --no-index</c>)
/// into <see cref="FileDiff"/>s, working on bytes. Hunk bodies are consumed by
/// their line counts, so content lines that look like headers are never
/// mistaken for them.
/// </summary>
public static class DiffParser
{
    /// <summary>
    /// Arguments every patch-producing command gets: no external diff driver or
    /// textconv (their output is not a patch), short submodule lines.
    /// <c>--full-index</c>: the "index" line then names both blobs exactly, which is how non-text
    /// files (images) are read back for display.
    /// </summary>
    public static readonly string[] PatchArgs = ["-p", "--no-ext-diff", "--no-textconv", "--no-color", "--submodule=short", "--full-index"];

    public static List<FileDiff> Parse(ReadOnlySpan<byte> output, bool highlight = true)
    {
        var files = new List<FileDiff>();
        var reader = new LineReader(output);
        FileBuilder? file = null;

        while (reader.TryPeek(out var line))
        {
            if (line.StartsWith("diff --git "u8) || line.StartsWith("diff --cc "u8) || line.StartsWith("diff --combined "u8))
            {
                if (file is not null) files.Add(file.Build(highlight));
                file = new FileBuilder();
                file.ParseGitHeader(line);
                reader.Skip();
                continue;
            }
            if (file is null)
            {
                reader.Skip(); // anything before the first file header (e.g. a commit id)
                continue;
            }
            if (line.StartsWith("@@ "u8))
            {
                file.Hunks.Add(ParseHunk(ref reader));
                continue;
            }
            file.ParseExtendedHeader(line);
            reader.Skip();
        }
        if (file is not null) files.Add(file.Build(highlight));
        return files;
    }

    private static DiffHunk ParseHunk(ref LineReader reader)
    {
        reader.TryRead(out var header);
        // "@@ -a[,b] +c[,d] @@[ heading]"
        var rest = header[3..];
        ParseRange(ref rest, (byte)'-', out var oldStart, out var oldCount);
        ParseRange(ref rest, (byte)'+', out var newStart, out var newCount);
        var heading = "";
        var close = rest.IndexOf("@@"u8);
        if (close >= 0 && close + 3 <= rest.Length) heading = System.Text.Encoding.UTF8.GetString(rest[(close + 3)..]);

        var lines = new List<DiffLine>(oldCount + newCount);
        int oldLine = oldStart, newLine = newStart, oldLeft = oldCount, newLeft = newCount;
        while ((oldLeft > 0 || newLeft > 0) && reader.TryPeek(out var line))
        {
            var marker = line.IsEmpty ? (byte)' ' : line[0]; // an empty line is a blank context line (diff.suppressBlankEmpty)
            var content = line.IsEmpty ? [] : line[1..].ToArray();
            switch (marker)
            {
                case (byte)' ':
                    lines.Add(new DiffLine(DiffLineKind.Context, content, oldLine++, newLine++));
                    oldLeft--;
                    newLeft--;
                    break;
                case (byte)'-':
                    lines.Add(new DiffLine(DiffLineKind.Deletion, content, oldLine++, 0));
                    oldLeft--;
                    break;
                case (byte)'+':
                    lines.Add(new DiffLine(DiffLineKind.Addition, content, 0, newLine++));
                    newLeft--;
                    break;
                case (byte)'\\':
                    MarkNoNewline(lines);
                    break;
                default:
                    throw new GitOperationException(GitErrorCode.UnexpectedOutput, "Truncated hunk in diff output.");
            }
            reader.Skip();
        }
        // "\ No newline at end of file" after the hunk's last line.
        while (reader.TryPeek(out var trailer) && trailer.StartsWith("\\"u8))
        {
            MarkNoNewline(lines);
            reader.Skip();
        }
        return new DiffHunk(oldStart, oldCount, newStart, newCount, heading, lines);
    }

    private static void MarkNoNewline(List<DiffLine> lines)
    {
        if (lines.Count > 0) lines[^1] = lines[^1] with { NoNewlineAtEnd = true };
    }

    private static void ParseRange(ref ReadOnlySpan<byte> text, byte sign, out int start, out int count)
    {
        start = 0;
        count = 1;
        var at = text.IndexOf(sign);
        if (at < 0) throw new GitOperationException(GitErrorCode.UnexpectedOutput, "Bad hunk header in diff output.");
        text = text[(at + 1)..];
        Utf8Parser.TryParse(text, out start, out var consumed);
        text = text[consumed..];
        if (!text.IsEmpty && text[0] == (byte)',')
        {
            Utf8Parser.TryParse(text[1..], out count, out consumed);
            text = text[(consumed + 1)..];
        }
    }

    private sealed class FileBuilder
    {
        private byte[]? oldPath, newPath;
        private DiffChange change = DiffChange.Modified;
        private bool binary;
        private string? oldMode, newMode;
        private Oid? oldBlob, newBlob;

        public List<DiffHunk> Hunks { get; } = [];

        public void ParseGitHeader(ReadOnlySpan<byte> line)
        {
            // "diff --git a/x b/y", or "diff --cc x" for a combined (conflict) diff.
            var rest = line["diff "u8.Length..];
            var combined = !rest.StartsWith("--git "u8);
            rest = rest[(rest.IndexOf((byte)' ') + 1)..];
            if (combined)
            {
                this.oldPath = this.newPath = GitQuoting.TryUnquote(rest, out var name, out _) ? name : rest.ToArray();
                return;
            }
            if (GitQuoting.TryUnquote(rest, out var a, out var after))
            {
                this.oldPath = StripPrefix(a);
                after = after.TrimStart((byte)' ');
                this.newPath = GitQuoting.TryUnquote(after, out var b, out _) ? StripPrefix(b) : StripPrefix(after.ToArray());
                return;
            }
            // Unquoted "a/NAME b/NAME": unambiguous when both names are equal,
            // which is every case except renames and copies (those carry
            // their own "rename from/to" lines).
            if ((rest.Length - 5) % 2 == 0 && rest.Length > 5)
            {
                var n = (rest.Length - 5) / 2;
                var a2 = rest.Slice(2, n);
                var b2 = rest[(n + 5)..];
                if (a2.SequenceEqual(b2))
                {
                    this.oldPath = this.newPath = a2.ToArray();
                    return;
                }
            }
            var split = rest.IndexOf(" b/"u8);
            if (split > 0)
            {
                this.oldPath = StripPrefix(rest[..split].ToArray());
                this.newPath = rest[(split + 3)..].ToArray();
            }
        }

        public void ParseExtendedHeader(ReadOnlySpan<byte> line)
        {
            if (line.StartsWith("new file mode "u8))
            {
                this.change = DiffChange.Added;
                this.newMode = Text(line["new file mode "u8.Length..]);
            }
            else if (line.StartsWith("deleted file mode "u8))
            {
                this.change = DiffChange.Deleted;
                this.oldMode = Text(line["deleted file mode "u8.Length..]);
            }
            else if (line.StartsWith("old mode "u8)) this.oldMode = Text(line["old mode "u8.Length..]);
            else if (line.StartsWith("new mode "u8)) this.newMode = Text(line["new mode "u8.Length..]);
            else if (line.StartsWith("index "u8))
            {
                // "index abc..def 100644": the blobs, and the mode of an unchanged-mode file.
                // A combined diff ("index a,b..c") names no single old blob.
                var ids = line["index "u8.Length..];
                if (ids.IndexOf((byte)' ') is var end and >= 0) ids = ids[..end];
                var dots = ids.IndexOf(".."u8);
                if (dots > 0 && ids[..dots].IndexOf((byte)',') < 0)
                {
                    this.oldBlob = Blob(ids[..dots]);
                    this.newBlob = Blob(ids[(dots + 2)..]);
                }
                var space = line.LastIndexOf((byte)' ');
                if (space > 6 && line[(space + 1)..].IndexOf((byte)'.') < 0)
                {
                    var mode = Text(line[(space + 1)..]);
                    this.oldMode ??= mode;
                    this.newMode ??= mode;
                }
            }
            else if (line.StartsWith("rename from "u8)) { this.change = DiffChange.Renamed; this.oldPath = Path(line["rename from "u8.Length..]); }
            else if (line.StartsWith("rename to "u8)) this.newPath = Path(line["rename to "u8.Length..]);
            else if (line.StartsWith("copy from "u8)) { this.change = DiffChange.Copied; this.oldPath = Path(line["copy from "u8.Length..]); }
            else if (line.StartsWith("copy to "u8)) this.newPath = Path(line["copy to "u8.Length..]);
            else if (line.StartsWith("--- "u8)) this.oldPath = SidePath(line[4..]) ?? this.oldPath;
            else if (line.StartsWith("+++ "u8)) this.newPath = SidePath(line[4..]) ?? this.newPath;
            else if (line.StartsWith("Binary files "u8)) this.binary = true;
        }

        public FileDiff Build(bool highlight)
        {
            var path = GitPath.FromBytes(this.newPath ?? this.oldPath ?? []);
            var old = GitPath.FromBytes(this.oldPath ?? this.newPath ?? []);
            var hunks = highlight ? this.Hunks.Select(InlineHighlighter.Apply).ToList() : this.Hunks;
            return new FileDiff(path, old, this.change, this.binary, this.oldMode, this.newMode, hunks) { OldBlob = this.oldBlob, NewBlob = this.newBlob };
        }

        // The all-zero id stands for a missing side (or, for the working tree, a blob not computed).
        private static Oid? Blob(ReadOnlySpan<byte> hex) =>
            hex.ContainsAnyExcept((byte)'0') && Oid.TryParseHex(hex, out var oid) ? oid : null;

        // "a/name", "/dev/null", "\"a/quoted\"", or "a/name with space\t" (git
        // appends a tab when the name contains a space).
        private static byte[]? SidePath(ReadOnlySpan<byte> text)
        {
            if (text.SequenceEqual("/dev/null"u8)) return null;
            if (GitQuoting.TryUnquote(text, out var quoted, out _)) return StripPrefix(quoted);
            if (!text.IsEmpty && text[^1] == (byte)'\t') text = text[..^1];
            return StripPrefix(text.ToArray());
        }

        private static byte[] Path(ReadOnlySpan<byte> text) =>
            GitQuoting.TryUnquote(text, out var quoted, out _) ? quoted : text.ToArray();

        private static byte[] StripPrefix(byte[] path) =>
            path.Length >= 2 && path[1] == (byte)'/' && path[0] is (byte)'a' or (byte)'b' ? path[2..] : path;

        private static string Text(ReadOnlySpan<byte> s) => System.Text.Encoding.ASCII.GetString(s).Trim();
    }

    /// <summary>Splits on '\n' without allocating; a trailing empty segment is not a line.</summary>
    private ref struct LineReader(ReadOnlySpan<byte> text)
    {
        private ReadOnlySpan<byte> rest = text;

        public readonly bool TryPeek(out ReadOnlySpan<byte> line)
        {
            if (this.rest.IsEmpty)
            {
                line = default;
                return false;
            }
            var nl = this.rest.IndexOf((byte)'\n');
            line = nl < 0 ? this.rest : this.rest[..nl];
            return true;
        }

        public bool TryRead(out ReadOnlySpan<byte> line)
        {
            if (!this.TryPeek(out line)) return false;
            this.Skip();
            return true;
        }

        public void Skip()
        {
            var nl = this.rest.IndexOf((byte)'\n');
            this.rest = nl < 0 ? [] : this.rest[(nl + 1)..];
        }
    }
}
