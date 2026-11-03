namespace CheeseGit.Core.Diff;

/// <summary>
/// Word-level highlights for modified lines: inside a hunk, a block of
/// deletions directly followed by additions is paired line by line, and each
/// pair is diffed word by word. Same rules as the current app: skip pairs
/// that are less than 50% similar, merge spans separated only by whitespace,
/// and drop highlights covering 70% or more of a line (they add no information).
/// </summary>
public static class InlineHighlighter
{
    private const double MinSimilarity = 0.5;
    private const double MaxCoverage = 0.7;

    // Word diffs are quadratic in tokens; minified one-line files must not stall the parser.
    private const long MaxCells = 250_000;

    public static DiffHunk Apply(DiffHunk hunk)
    {
        var lines = hunk.Lines;
        List<DiffLine>? result = null;
        var i = 0;
        while (i < lines.Count)
        {
            if (lines[i].Kind != DiffLineKind.Deletion)
            {
                i++;
                continue;
            }
            var delStart = i;
            while (i < lines.Count && lines[i].Kind == DiffLineKind.Deletion) i++;
            var addStart = i;
            while (i < lines.Count && lines[i].Kind == DiffLineKind.Addition) i++;
            var pairs = Math.Min(addStart - delStart, i - addStart);
            for (var p = 0; p < pairs; p++)
            {
                var (del, add) = Compute(lines[delStart + p].Content, lines[addStart + p].Content);
                if (del.Count == 0 && add.Count == 0) continue;
                result ??= [.. lines];
                if (del.Count > 0) result[delStart + p] = result[delStart + p] with { Highlights = del };
                if (add.Count > 0) result[addStart + p] = result[addStart + p] with { Highlights = add };
            }
        }
        return result is null ? hunk : hunk with { Lines = result };
    }

    /// <summary>Highlights for one deleted/added line pair (byte offsets).</summary>
    public static (List<InlineHighlight> Old, List<InlineHighlight> New) Compute(ReadOnlySpan<byte> oldText, ReadOnlySpan<byte> newText)
    {
        var a = Tokenize(oldText);
        var b = Tokenize(newText);
        if ((long)a.Count * b.Count > MaxCells) return ([], []);

        // Longest common subsequence of tokens.
        var n = a.Count;
        var m = b.Count;
        var lcs = new int[(n + 1) * (m + 1)];
        for (var x = n - 1; x >= 0; x--)
        {
            for (var y = m - 1; y >= 0; y--)
            {
                lcs[x * (m + 1) + y] = Same(oldText, a[x], newText, b[y])
                    ? lcs[(x + 1) * (m + 1) + y + 1] + 1
                    : Math.Max(lcs[(x + 1) * (m + 1) + y], lcs[x * (m + 1) + y + 1]);
            }
        }
        var matches = lcs[0];
        if (n + m == 0 || 2.0 * matches / (n + m) < MinSimilarity) return ([], []);

        var oldSpans = new List<InlineHighlight>();
        var newSpans = new List<InlineHighlight>();
        int i = 0, j = 0;
        while (i < n || j < m)
        {
            if (i < n && j < m && Same(oldText, a[i], newText, b[j]))
            {
                i++;
                j++;
            }
            else if (j < m && (i == n || lcs[i * (m + 1) + j + 1] >= lcs[(i + 1) * (m + 1) + j]))
            {
                newSpans.Add(new InlineHighlight(b[j].Start, b[j].Length));
                j++;
            }
            else
            {
                oldSpans.Add(new InlineHighlight(a[i].Start, a[i].Length));
                i++;
            }
        }

        return (Finish(oldSpans, oldText), Finish(newSpans, newText));
    }

    /// <summary>Merges adjacent spans separated only by whitespace (or nothing).</summary>
    public static List<InlineHighlight> MergeOverWhitespace(List<InlineHighlight> spans, ReadOnlySpan<byte> text)
    {
        if (spans.Count < 2) return spans;
        var merged = new List<InlineHighlight> { spans[0] };
        for (var k = 1; k < spans.Count; k++)
        {
            var prev = merged[^1];
            var prevEnd = prev.Start + prev.Length;
            var gap = text[prevEnd..spans[k].Start];
            if (IsAllWhitespace(gap)) merged[^1] = new InlineHighlight(prev.Start, spans[k].Start + spans[k].Length - prev.Start);
            else merged.Add(spans[k]);
        }
        return merged;
    }

    private static List<InlineHighlight> Finish(List<InlineHighlight> spans, ReadOnlySpan<byte> text)
    {
        spans = MergeOverWhitespace(spans, text);
        var total = spans.Sum(s => s.Length);
        if (spans.Count == 0 || total >= text.Length || (double)total / text.Length >= MaxCoverage) return [];
        return spans;
    }

    private readonly record struct Token(int Start, int Length);

    // Runs of whitespace and runs of non-whitespace, like similar's from_words.
    private static List<Token> Tokenize(ReadOnlySpan<byte> text)
    {
        var tokens = new List<Token>();
        var start = 0;
        for (var k = 1; k <= text.Length; k++)
        {
            if (k == text.Length || IsSpace(text[k]) != IsSpace(text[start]))
            {
                tokens.Add(new Token(start, k - start));
                start = k;
            }
        }
        return tokens;
    }

    private static bool Same(ReadOnlySpan<byte> a, Token x, ReadOnlySpan<byte> b, Token y) =>
        a.Slice(x.Start, x.Length).SequenceEqual(b.Slice(y.Start, y.Length));

    private static bool IsSpace(byte c) => c is (byte)' ' or (byte)'\t' or (byte)'\r' or (byte)'\n' or (byte)'\v' or (byte)'\f';

    private static bool IsAllWhitespace(ReadOnlySpan<byte> s)
    {
        foreach (var c in s)
        {
            if (!IsSpace(c)) return false;
        }
        return true;
    }
}
