using CheeseGit.Core.Graph;

namespace CheeseGit.Cli;

/// <summary>
/// Draws a <see cref="GraphLayout"/> row as text, two columns per lane: the
/// commit line, then (only when an edge changes lanes) a connector line.
/// Approximate by design; it is for eyeballing layouts, not for the app.
/// </summary>
internal static class TextGraph
{
    public static (string Node, string? Connectors) Row(GraphLayout layout, int row)
    {
        var width = layout.LaneCount * 2;
        var node = new char[width];
        var below = new char[width];
        Array.Fill(node, ' ');
        Array.Fill(below, ' ');
        var anyConnector = false;

        foreach (var s in layout.SegmentsOf(row))
        {
            switch (s.Kind)
            {
                case SegmentKind.Through when s.FromLane == s.ToLane:
                    node[s.FromLane * 2] = '|';
                    below[s.FromLane * 2] = '|';
                    break;
                case SegmentKind.Through:
                    node[s.FromLane * 2] = '|';
                    Diagonal(below, s.FromLane, s.ToLane);
                    anyConnector = true;
                    break;
                case SegmentKind.Bottom when s.FromLane == s.ToLane:
                    below[s.FromLane * 2] = '|';
                    break;
                case SegmentKind.Bottom:
                    Diagonal(below, s.FromLane, s.ToLane);
                    anyConnector = true;
                    break;
                case SegmentKind.StubDown:
                    below[s.FromLane * 2] = 'v';
                    anyConnector = true;
                    break;
                case SegmentKind.StubUp:
                    if (node[s.ToLane * 2] == ' ') node[s.ToLane * 2] = '^';
                    break;
            }
        }
        node[layout.NodeLane(row) * 2] = '*';

        // Vertical continuations make the connector line noise unless something bends.
        return (new string(node).TrimEnd(), anyConnector ? new string(below).TrimEnd() : null);
    }

    private static void Diagonal(char[] line, int from, int to)
    {
        var column = (Math.Min(from, to) * 2) + 1;
        line[column] = to > from ? '\\' : '/';
    }
}
