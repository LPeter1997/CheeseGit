using System.Text;
using CheeseGit.Core.Diff;

namespace CheeseGit.App.Controls;

public enum CellKind : byte
{
    Hunk,
    Context,
    Addition,
    Deletion,
    /// <summary>The empty side of a split row (a deletion with no matching addition, or the reverse).</summary>
    Empty,
}

/// <summary>One side of a displayed diff row, precomputed once per diff so drawing only reads it.</summary>
public sealed class DiffCell
{
    public CellKind Kind { get; init; }

    public int Hunk { get; init; }

    /// <summary>Index of the line in its hunk, or -1 (hunk header, empty side).</summary>
    public int Line { get; init; } = -1;

    public int OldNumber { get; init; }

    public int NewNumber { get; init; }

    /// <summary>Display text: tabs expanded, control characters made visible, '\r' dropped.</summary>
    public string Text { get; init; } = "";

    /// <summary>Changed words, as display-text character ranges.</summary>
    public (int Start, int Length)[] Highlights { get; init; } = [];

    public bool NoNewlineAtEnd { get; init; }

    /// <summary>
    /// "CRLF" or "LF" when the diff's changed lines mix line endings (a file converted between them):
    /// such lines otherwise look identical on both sides. Null otherwise.
    /// </summary>
    public string? LineEnding { get; init; }

    /// <summary>First and last row of the run of changes this cell belongs to (on its side), or -1.</summary>
    public int GroupStart { get; set; } = -1;

    public int GroupEnd { get; set; } = -1;

    public bool IsChange => this.Kind is CellKind.Addition or CellKind.Deletion;
}

/// <summary>A displayed row. Unified rows have only <see cref="Left"/>; split rows have both sides.</summary>
public readonly record struct DiffRow(DiffCell Left, DiffCell? Right);

/// <summary>Turns a <see cref="FileDiff"/> into rows for the unified and split layouts (the old app's DiffViewShared.ts).</summary>
public static class DiffRows
{
    public const int TabWidth = 4;

    // A minified megabyte on one line would take seconds to shape; nobody reads past this.
    public const int MaxLineChars = 4000;

    public static List<DiffRow> Unified(FileDiff diff)
    {
        var rows = new List<DiffRow>();
        var eol = HasMixedLineEndings(diff);
        for (var h = 0; h < diff.Hunks.Count; h++)
        {
            var hunk = diff.Hunks[h];
            rows.Add(new DiffRow(new DiffCell { Kind = CellKind.Hunk, Hunk = h, Text = hunk.Header }, null));
            for (var l = 0; l < hunk.Lines.Count; l++)
            {
                rows.Add(new DiffRow(Cell(hunk.Lines[l], h, l, eol), null));
            }
        }
        MarkGroups(rows, r => r.Left);
        return rows;
    }

    public static List<DiffRow> Split(FileDiff diff)
    {
        var rows = new List<DiffRow>();
        var eol = HasMixedLineEndings(diff);
        for (var h = 0; h < diff.Hunks.Count; h++)
        {
            var hunk = diff.Hunks[h];
            var header = new DiffCell { Kind = CellKind.Hunk, Hunk = h, Text = hunk.Header };
            rows.Add(new DiffRow(header, header));
            var lines = hunk.Lines;
            var i = 0;
            while (i < lines.Count)
            {
                if (lines[i].Kind == DiffLineKind.Context)
                {
                    var cell = Cell(lines[i], h, i, eol);
                    rows.Add(new DiffRow(cell, cell));
                    i++;
                    continue;
                }
                // Pair a run of deletions with the run of additions after it.
                var deletions = new List<int>();
                var additions = new List<int>();
                while (i < lines.Count && lines[i].Kind == DiffLineKind.Deletion) deletions.Add(i++);
                while (i < lines.Count && lines[i].Kind == DiffLineKind.Addition) additions.Add(i++);
                for (var j = 0; j < Math.Max(deletions.Count, additions.Count); j++)
                {
                    var left = j < deletions.Count ? Cell(lines[deletions[j]], h, deletions[j], eol) : new DiffCell { Kind = CellKind.Empty, Hunk = h };
                    var right = j < additions.Count ? Cell(lines[additions[j]], h, additions[j], eol) : new DiffCell { Kind = CellKind.Empty, Hunk = h };
                    rows.Add(new DiffRow(left, right));
                }
            }
        }
        MarkGroups(rows, r => r.Left);
        MarkGroups(rows, r => r.Right!);
        return rows;
    }

    /// <summary>Runs of consecutive changes on one side, for the "stage this block" gutter action.</summary>
    private static void MarkGroups(List<DiffRow> rows, Func<DiffRow, DiffCell> side)
    {
        var i = 0;
        while (i < rows.Count)
        {
            if (!side(rows[i]).IsChange && side(rows[i]).Kind != CellKind.Empty)
            {
                i++;
                continue;
            }
            // In split rows an empty cell continues the block it sits in.
            var start = i;
            while (i < rows.Count && (side(rows[i]).IsChange || side(rows[i]).Kind == CellKind.Empty)) i++;
            var end = i - 1;
            for (var j = start; j <= end; j++)
            {
                var cell = side(rows[j]);
                if (!cell.IsChange) continue;
                cell.GroupStart = start;
                cell.GroupEnd = end;
            }
        }
    }

    /// <summary>Changed lines end in both CRLF and LF (the last line of a file without a final newline has neither).</summary>
    internal static bool HasMixedLineEndings(FileDiff diff)
    {
        bool crlf = false, lf = false;
        foreach (var hunk in diff.Hunks)
        {
            foreach (var line in hunk.Lines)
            {
                if (!line.IsChange || line.NoNewlineAtEnd) continue;
                if (line.Content.Length > 0 && line.Content[^1] == (byte)'\r') crlf = true;
                else lf = true;
                if (crlf && lf) return true;
            }
        }
        return false;
    }

    private static DiffCell Cell(DiffLine line, int hunk, int index, bool showLineEnding)
    {
        var (text, map) = ToDisplay(line.Content);
        var highlights = new (int, int)[line.Highlights.Count];
        for (var i = 0; i < highlights.Length; i++)
        {
            var hl = line.Highlights[i];
            var start = ToDisplayIndex(line.Content, hl.Start, map, text.Length);
            var end = ToDisplayIndex(line.Content, hl.Start + hl.Length, map, text.Length);
            highlights[i] = (start, Math.Max(0, end - start));
        }
        return new DiffCell
        {
            Kind = line.Kind switch
            {
                DiffLineKind.Addition => CellKind.Addition,
                DiffLineKind.Deletion => CellKind.Deletion,
                _ => CellKind.Context,
            },
            Hunk = hunk,
            Line = index,
            OldNumber = line.OldLine,
            NewNumber = line.NewLine,
            Text = text,
            Highlights = highlights,
            NoNewlineAtEnd = line.NoNewlineAtEnd,
            LineEnding = showLineEnding && line.IsChange && !line.NoNewlineAtEnd
                ? (line.Content.Length > 0 && line.Content[^1] == (byte)'\r' ? "CRLF" : "LF")
                : null,
        };
    }

    /// <summary>
    /// Decodes a line for display. Returns the char-to-display index map when
    /// the text changed shape (tabs, control characters), otherwise null.
    /// </summary>
    internal static (string Text, int[]? Map) ToDisplay(ReadOnlySpan<byte> content)
    {
        if (content.Length > 0 && content[^1] == (byte)'\r') content = content[..^1];
        var text = Encoding.UTF8.GetString(content);
        if (!text.Any(c => char.IsControl(c)) && text.Length <= MaxLineChars) return (text, null);

        var sb = new StringBuilder(text.Length + 16);
        var map = new int[text.Length + 1];
        for (var i = 0; i < text.Length; i++)
        {
            map[i] = sb.Length;
            var c = text[i];
            if (c == '\t')
            {
                sb.Append(' ', TabWidth - sb.Length % TabWidth);
            }
            else if (char.IsControl(c))
            {
                sb.Append(c < ' ' ? (char)(0x2400 + c) : '�');
            }
            else
            {
                sb.Append(c);
            }
        }
        map[text.Length] = sb.Length;
        if (sb.Length > MaxLineChars)
        {
            sb.Length = MaxLineChars;
            sb.Append('…');
        }
        return (sb.ToString(), map);
    }

    private static int ToDisplayIndex(ReadOnlySpan<byte> content, int byteOffset, int[]? map, int textLength)
    {
        byteOffset = Math.Clamp(byteOffset, 0, content.Length);
        var chars = Encoding.UTF8.GetCharCount(content[..byteOffset]);
        if (map is not null) chars = map[Math.Min(chars, map.Length - 1)];
        return Math.Min(chars, textLength);
    }

    /// <summary>The change lines of a hunk.</summary>
    public static List<LineSelection> HunkSelections(FileDiff diff, int hunk)
    {
        var lines = diff.Hunks[hunk].Lines;
        var result = new List<LineSelection>();
        for (var i = 0; i < lines.Count; i++)
        {
            if (lines[i].IsChange) result.Add(new LineSelection(hunk, i));
        }
        return result;
    }
}
