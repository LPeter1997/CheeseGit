using System.Text;
using CheeseGit.Core.Git;

namespace CheeseGit.Core.Diff;

/// <summary>
/// Which side of the original diff the target (index or working tree) holds.
/// A partial patch must reproduce that side exactly, or git apply rejects it.
/// </summary>
public enum PatchBase
{
    /// <summary>
    /// The target holds the diff's old side; apply forward. Staging lines
    /// (the index is the old side of the unstaged diff).
    /// </summary>
    OldSide,

    /// <summary>
    /// The target holds the diff's new side; apply with <c>--reverse</c>.
    /// Unstaging lines (the index is the new side of the staged diff) and
    /// discarding lines (the working tree is the new side of the unstaged diff).
    /// </summary>
    NewSide,
}

/// <summary>Builds a patch containing only selected change lines of a <see cref="FileDiff"/>.</summary>
public static class PartialPatch
{
    /// <summary>
    /// Builds the patch, or returns null when nothing selected is a change line.
    /// Unselected lines that exist on <paramref name="base"/>'s side become
    /// context; unselected lines that do not are dropped. Content is copied
    /// byte for byte, including '\r' and "No newline at end of file" markers.
    /// </summary>
    public static byte[]? Build(FileDiff diff, IEnumerable<LineSelection> selections, PatchBase @base)
    {
        var selected = selections.ToHashSet();
        var allSelected = true;
        var anySelected = false;
        for (var h = 0; h < diff.Hunks.Count; h++)
        {
            var lines = diff.Hunks[h].Lines;
            for (var l = 0; l < lines.Count; l++)
            {
                if (!lines[l].IsChange) continue;
                if (selected.Contains(new LineSelection(h, l))) anySelected = true;
                else allSelected = false;
            }
        }
        if (!anySelected) return null;

        // A file the target does not have yet (staging lines of an untracked
        // file) or will no longer have must keep its creation/deletion header;
        // otherwise the patch is a plain modification.
        var creates = diff.Change == DiffChange.Added && (@base == PatchBase.OldSide || allSelected);
        var deletes = diff.Change == DiffChange.Deleted && (@base == PatchBase.NewSide || allSelected);

        var patch = new MemoryStream();
        var (a, b) = (Quote("a/", diff.Path), Quote("b/", diff.Path));
        Append(patch, $"diff --git {a} {b}\n");
        if (creates) Append(patch, $"new file mode {diff.NewMode ?? "100644"}\n");
        if (deletes) Append(patch, $"deleted file mode {diff.OldMode ?? "100644"}\n");
        Append(patch, creates ? "--- /dev/null\n" : $"--- {a}\n");
        Append(patch, deletes ? "+++ /dev/null\n" : $"+++ {b}\n");

        // The side the target does not hold shifts by the net line count of
        // the hunks emitted before; the base side stays exact.
        var shift = 0;
        for (var h = 0; h < diff.Hunks.Count; h++)
        {
            var hunk = diff.Hunks[h];
            var body = new MemoryStream();
            int oldCount = 0, newCount = 0;
            var hasSelection = false;
            foreach (var (line, marker) in Order(hunk, h, selected, @base))
            {
                if (marker != (byte)' ') hasSelection = true;
                if (marker != (byte)'+') oldCount++;
                if (marker != (byte)'-') newCount++;
                body.WriteByte(marker);
                body.Write(line.Content);
                body.WriteByte((byte)'\n');
                if (line.NoNewlineAtEnd) body.Write("\\ No newline at end of file\n"u8);
            }
            if (!hasSelection) continue;

            // In unified diffs a side's start is (lines before the hunk) + 1, or
            // just (lines before) when that side is empty. The base side keeps
            // all its lines, so its position comes straight from the original hunk.
            int oldBefore, newBefore;
            if (@base == PatchBase.OldSide)
            {
                oldBefore = hunk.OldCount > 0 ? hunk.OldStart - 1 : hunk.OldStart;
                newBefore = oldBefore + shift;
            }
            else
            {
                newBefore = hunk.NewCount > 0 ? hunk.NewStart - 1 : hunk.NewStart;
                oldBefore = newBefore - shift;
            }
            var oldStart = oldCount > 0 ? oldBefore + 1 : oldBefore;
            var newStart = newCount > 0 ? newBefore + 1 : newBefore;
            shift += newCount - oldCount;

            Append(patch, $"@@ -{oldStart},{oldCount} +{newStart},{newCount} @@\n");
            body.Position = 0;
            body.CopyTo(patch);
        }
        return patch.ToArray();
    }

    /// <summary>
    /// The patch lines of one hunk with their markers. Unselected lines that
    /// exist on the base side become context, the others are dropped. Inside a
    /// block of changes (deletions, then additions), the selected lines of the
    /// non-base kind are moved next to the selected lines of the base kind, so
    /// that selecting "x1 → y1" out of "x1 x2 → y1 y2" yields "y1 x2" rather
    /// than "x2 y1" (the old app's order).
    /// </summary>
    private static IEnumerable<(DiffLine Line, byte Marker)> Order(DiffHunk hunk, int h, HashSet<LineSelection> selected, PatchBase @base)
    {
        var lines = hunk.Lines;
        var l = 0;
        while (l < lines.Count)
        {
            if (!lines[l].IsChange)
            {
                yield return (lines[l], (byte)' ');
                l++;
                continue;
            }
            var dels = new List<(DiffLine Line, bool Selected)>();
            var adds = new List<(DiffLine Line, bool Selected)>();
            for (; l < lines.Count && lines[l].IsChange; l++)
            {
                var entry = (lines[l], selected.Contains(new LineSelection(h, l)));
                if (lines[l].Kind == DiffLineKind.Deletion) dels.Add(entry);
                else adds.Add(entry);
            }

            if (@base == PatchBase.OldSide)
            {
                // Old side: every deletion stays (selected as '-', others as context).
                var insertAt = dels.FindLastIndex(d => d.Selected) + 1;
                if (insertAt == 0) insertAt = dels.Count;
                for (var i = 0; i < dels.Count; i++)
                {
                    if (i == insertAt) foreach (var a in adds.Where(a => a.Selected)) yield return (a.Line, (byte)'+');
                    yield return (dels[i].Line, dels[i].Selected ? (byte)'-' : (byte)' ');
                }
                if (insertAt == dels.Count) foreach (var a in adds.Where(a => a.Selected)) yield return (a.Line, (byte)'+');
            }
            else
            {
                // New side: every addition stays (selected as '+', others as context).
                var insertAt = adds.FindIndex(a => a.Selected);
                if (insertAt < 0) insertAt = 0;
                for (var i = 0; i < adds.Count; i++)
                {
                    if (i == insertAt) foreach (var d in dels.Where(d => d.Selected)) yield return (d.Line, (byte)'-');
                    yield return (adds[i].Line, adds[i].Selected ? (byte)'+' : (byte)' ');
                }
                if (adds.Count == 0) foreach (var d in dels.Where(d => d.Selected)) yield return (d.Line, (byte)'-');
            }
        }
    }

    /// <summary>
    /// "a/name" or "b/name", with git's C-style quoting around the whole
    /// prefixed name when it contains '"', '\' or control characters.
    /// </summary>
    internal static string Quote(string prefix, GitPath path)
    {
        var bytes = (byte[])[.. Encoding.UTF8.GetBytes(prefix), .. path.Bytes];
        var needs = false;
        foreach (var b in bytes)
        {
            if (b < 0x20 || b == (byte)'"' || b == (byte)'\\' || b == 0x7f) needs = true;
        }
        if (!needs) return Encoding.UTF8.GetString(bytes);

        var sb = new StringBuilder("\"");
        var raw = new List<byte>();
        void FlushRaw()
        {
            sb.Append(Encoding.UTF8.GetString([.. raw]));
            raw.Clear();
        }
        foreach (var b in bytes)
        {
            string? escape = b switch
            {
                (byte)'"' => "\\\"",
                (byte)'\\' => "\\\\",
                (byte)'\t' => "\\t",
                (byte)'\n' => "\\n",
                (byte)'\r' => "\\r",
                < 0x20 or 0x7f => "\\" + Convert.ToString(b, 8).PadLeft(3, '0'),
                _ => null,
            };
            if (escape is null)
            {
                raw.Add(b);
                continue;
            }
            FlushRaw();
            sb.Append(escape);
        }
        FlushRaw();
        return sb.Append('"').ToString();
    }

    private static void Append(MemoryStream stream, string text) => stream.Write(Encoding.UTF8.GetBytes(text));
}
