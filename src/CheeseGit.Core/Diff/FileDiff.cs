using System.Text;
using CheeseGit.Core.Git;

namespace CheeseGit.Core.Diff;

public enum DiffLineKind : byte
{
    Context,
    Addition,
    Deletion,
}

public enum DiffChange : byte
{
    Modified,
    Added,
    Deleted,
    Renamed,
    Copied,
}

/// <summary>Byte range of a changed word inside <see cref="DiffLine.Content"/>.</summary>
public readonly record struct InlineHighlight(int Start, int Length);

/// <summary>
/// One line of a hunk. <see cref="Content"/> holds the exact bytes without the
/// leading marker and without the '\n' (a '\r' of a CRLF file stays), so a
/// patch rebuilt from it applies byte for byte.
/// </summary>
public sealed record DiffLine(DiffLineKind Kind, byte[] Content, int OldLine, int NewLine, bool NoNewlineAtEnd = false)
{
    /// <summary>Changed words relative to the paired line on the other side; empty when not a modification.</summary>
    public IReadOnlyList<InlineHighlight> Highlights { get; init; } = [];

    /// <summary>Decoded for display, without a trailing '\r'.</summary>
    public string Text => Encoding.UTF8.GetString(this.Content).TrimEnd('\r');

    public bool IsChange => this.Kind != DiffLineKind.Context;
}

/// <summary>A hunk; <see cref="OldStart"/>/<see cref="NewStart"/> are 1-based (0 for an empty side).</summary>
public sealed record DiffHunk(int OldStart, int OldCount, int NewStart, int NewCount, string Heading, IReadOnlyList<DiffLine> Lines)
{
    public string Header =>
        $"@@ -{this.OldStart},{this.OldCount} +{this.NewStart},{this.NewCount} @@" + (this.Heading.Length > 0 ? " " + this.Heading : "");
}

/// <summary>The diff of one file. For additions <see cref="OldPath"/> equals <see cref="Path"/>.</summary>
public sealed record FileDiff(
    GitPath Path,
    GitPath OldPath,
    DiffChange Change,
    bool IsBinary,
    string? OldMode,
    string? NewMode,
    IReadOnlyList<DiffHunk> Hunks)
{
    public static FileDiff Empty(GitPath path) => new(path, path, DiffChange.Modified, false, null, null, []);

    /// <summary>
    /// A conflicted file, shown as the working tree (with its markers) against HEAD. Its lines
    /// cannot be staged: the index holds the conflict's stages, not one version.
    /// </summary>
    public bool IsConflicted { get; init; }

    /// <summary>The old side's blob ("index" line), or null when there is none (an added file).</summary>
    public Oid? OldBlob { get; init; }

    /// <summary>
    /// The new side's blob, or null when there is none (a deleted file). For the working tree it is
    /// the content's id, which is not in the object database: read the file instead.
    /// </summary>
    public Oid? NewBlob { get; init; }

    public int Additions => this.Hunks.Sum(h => h.Lines.Count(l => l.Kind == DiffLineKind.Addition));

    public int Deletions => this.Hunks.Sum(h => h.Lines.Count(l => l.Kind == DiffLineKind.Deletion));
}

/// <summary>Per-file line counts, as <c>--numstat</c> reports them. Binary files count 0/0.</summary>
public sealed record FileStats(GitPath Path, int Additions, int Deletions, bool IsBinary = false);

/// <summary>A selected change line: index of the hunk in the diff, index of the line in the hunk.</summary>
public readonly record struct LineSelection(int HunkIndex, int LineIndex);
