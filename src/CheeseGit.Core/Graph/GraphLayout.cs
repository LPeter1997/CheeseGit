namespace CheeseGit.Core.Graph;

/// <summary>Which part of a row a line segment spans.</summary>
public enum SegmentKind : byte
{
    /// <summary>From the row's top edge to the row's commit dot.</summary>
    Top,
    /// <summary>From the row's commit dot to the row's bottom edge.</summary>
    Bottom,
    /// <summary>Straight through the whole row (a lane passing by).</summary>
    Through,
    /// <summary>
    /// A long edge broken off below the commit dot (drawn as a short arrow).
    /// Its other end is a <see cref="StubUp"/> on the parent's row.
    /// </summary>
    StubDown,
    /// <summary>The parent end of a broken long edge, arriving at the dot.</summary>
    StubUp,
}

/// <summary>One line segment drawn inside a single row.</summary>
public readonly record struct Segment(short FromLane, short ToLane, short Color, SegmentKind Kind);

/// <summary>
/// A lane layout of a <see cref="CommitDag"/>. Every commit gets a lane, and
/// every parent edge inside the DAG is drawn; nothing is dropped. Rendering a
/// row needs only that row's node and segments, so any visible window can be
/// drawn without looking at the rest.
/// </summary>
public sealed class GraphLayout
{
    private readonly short[] nodeLane;
    private readonly short[] nodeColor;
    private readonly int[] segmentStart;
    private readonly Segment[] segments;

    private GraphLayout(short[] nodeLane, short[] nodeColor, int[] segmentStart, Segment[] segments, int laneCount)
    {
        this.nodeLane = nodeLane;
        this.nodeColor = nodeColor;
        this.segmentStart = segmentStart;
        this.segments = segments;
        this.LaneCount = laneCount;
    }

    public int RowCount => this.nodeLane.Length;

    /// <summary>Maximum number of lanes used by any row.</summary>
    public int LaneCount { get; }

    public int NodeLane(int row) => this.nodeLane[row];

    public int NodeColor(int row) => this.nodeColor[row];

    public ReadOnlySpan<Segment> SegmentsOf(int row) =>
        this.segments.AsSpan(this.segmentStart[row], this.segmentStart[row + 1] - this.segmentStart[row]);

    /// <summary>Total segment count; a proxy for memory use.</summary>
    public int SegmentCount => this.segments.Length;

    /// <summary>
    /// Lays out <paramref name="dag"/>. The first-parent chain starting at
    /// <paramref name="mainTip"/> (normally HEAD) always occupies lane 0, so the
    /// current branch reads as a straight line, as in the current app.
    /// Edges that would need a new lane for more than <paramref name="maxEdgeRows"/>
    /// rows are broken into a pair of stubs instead (as gitk does), which keeps
    /// the graph narrow on histories with many long-lived topic branches.
    /// Rows marked in <paramref name="baseRows"/> (the default branch's first-parent
    /// chain) are colour 0 wherever they are drawn, so master keeps its colour below
    /// a branch built on it, even where both are one straight line. The current
    /// branch's own commits then get a colour of their own.
    /// </summary>
    public static GraphLayout Compute(CommitDag dag, int mainTip, int maxEdgeRows = int.MaxValue, bool[]? baseRows = null)
    {
        var n = dag.Count;
        var isMain = new bool[n];
        var reserveMain = mainTip >= 0;
        for (var c = mainTip; c >= 0; c = FirstParent(dag, c)) isMain[c] = true;
        var isBase = baseRows ?? new bool[n];
        var hasBase = baseRows is not null;

        var nodeLane = new short[n];
        var nodeColor = new short[n];
        var segmentStart = new int[n + 1];
        var segments = new List<Segment>(n * 3);

        // expect[lane] = commit the lane is heading to, or -1 when free.
        var expect = new List<int>();
        var color = new List<short>();
        short nextColor = 1; // 0 is reserved for the main lane

        // Colours are chain ids (the palette takes them modulo its size). A merge-heavy
        // history can start more than 32k chains: wrap to 1 instead of going negative,
        // which indexed the palette out of range. Chains 32k apart never meet on screen.
        short NextColor()
        {
            var c = nextColor;
            nextColor = nextColor == short.MaxValue ? (short)1 : (short)(nextColor + 1);
            return c;
        }
        // Without a separate default branch, the main lane is colour 0 as before.
        var mainColor = hasBase && mainTip >= 0 && !isBase[mainTip] ? NextColor() : (short)0;
        // A lane opened for a merge's other parent takes that parent's colour when it is known
        // up front: merging master into a branch drew the edge to master's tip in a fresh
        // colour, which switched to master's at the tip's dot and read as a stray branch.
        short NewChainColor(int parent) => isBase[parent] ? (short)0 : NextColor();
        var maxLanes = 0;
        var incoming = new List<int>();
        // parent row -> colours of broken edges arriving there
        var stubsInto = new Dictionary<int, List<short>>();

        int AllocateLane(bool main)
        {
            if (main)
            {
                while (expect.Count == 0) { expect.Add(-1); color.Add(0); }
                return 0;
            }
            // Lane 0 is reserved for the main chain when there is one.
            var first = reserveMain ? 1 : 0;
            for (var l = first; l < expect.Count; l++)
            {
                if (expect[l] < 0) return l;
            }
            if (reserveMain && expect.Count == 0) { expect.Add(-1); color.Add(0); }
            expect.Add(-1);
            color.Add(0);
            return expect.Count - 1;
        }

        int LaneExpecting(int commit)
        {
            for (var l = 0; l < expect.Count; l++)
            {
                if (expect[l] == commit) return l;
            }
            return -1;
        }

        for (var row = 0; row < n; row++)
        {
            segmentStart[row] = segments.Count;

            // 1. Lanes converging into this commit.
            incoming.Clear();
            for (var l = 0; l < expect.Count; l++)
            {
                if (expect[l] == row) incoming.Add(l);
            }

            // 2. Pick the commit's lane.
            int lane;
            if (isMain[row]) lane = AllocateLane(main: true);
            else if (incoming.Count > 0) lane = incoming[0];
            else lane = AllocateLane(main: false);

            short laneColor;
            if (isBase[row]) laneColor = 0;
            else if (isMain[row]) laneColor = mainColor;
            else if (incoming.Contains(lane)) laneColor = color[lane];
            else laneColor = NextColor();
            color[lane] = laneColor;
            nodeLane[row] = (short)lane;
            nodeColor[row] = laneColor;

            // 3. Upper half: converging lanes bend into the dot; others pass by.
            foreach (var l in incoming)
            {
                segments.Add(new Segment((short)l, (short)lane, color[l], SegmentKind.Top));
            }
            for (var l = 0; l < expect.Count; l++)
            {
                if (expect[l] >= 0 && expect[l] != row && l != lane)
                {
                    segments.Add(new Segment((short)l, (short)l, color[l], SegmentKind.Through));
                }
            }
            if (stubsInto.Remove(row, out var stubColors))
            {
                foreach (var c in stubColors) segments.Add(new Segment((short)lane, (short)lane, c, SegmentKind.StubUp));
            }
            foreach (var l in incoming) expect[l] = -1;
            expect[lane] = -1;

            // 4. Lower half: edges to parents.
            var parents = dag.ParentsOf(row);
            for (var i = 0; i < parents.Length; i++)
            {
                var p = parents[i];
                if (p < 0) continue; // parent outside this DAG (shallow / filtered)

                // The main chain never leaves lane 0, even when a side lane is
                // already heading to the same parent: both lanes then converge
                // into the parent's dot instead.
                var keepsMainLane = i == 0 && isMain[row];
                var target = keepsMainLane ? -1 : LaneExpecting(p);
                if (target < 0 && !keepsMainLane && p - row > maxEdgeRows)
                {
                    var stubColor = i == 0 ? laneColor : NewChainColor(p);
                    segments.Add(new Segment((short)lane, (short)lane, stubColor, SegmentKind.StubDown));
                    if (!stubsInto.TryGetValue(p, out var list)) stubsInto[p] = list = [];
                    list.Add(stubColor);
                    continue;
                }
                if (target < 0)
                {
                    // The first parent continues in this lane. Other parents
                    // open a new lane with a new colour. Only main-chain commits
                    // ever sit in lane 0; a side branch forking from the main
                    // line converges into lane 0 at the fork commit's row.
                    if (i == 0)
                    {
                        target = lane;
                    }
                    else
                    {
                        target = AllocateLane(main: false);
                        color[target] = NewChainColor(p);
                    }
                    expect[target] = p;
                }
                var edgeColor = i == 0 ? laneColor : color[target];
                segments.Add(new Segment((short)lane, (short)target, edgeColor, SegmentKind.Bottom));
            }

            maxLanes = Math.Max(maxLanes, expect.Count);

            // 5. Compaction: a lane with a free slot to its left slides over by
            // one column, in the lower half of this row (as git log --graph
            // does). Without this, a lane opened far right during a crowded
            // stretch would stay there for its whole length.
            for (var l = Math.Max(1, reserveMain ? 2 : 1); l < expect.Count; l++)
            {
                if (expect[l] < 0 || expect[l - 1] >= 0 || l - 1 == lane) continue;
                expect[l - 1] = expect[l];
                color[l - 1] = color[l];
                expect[l] = -1;
                for (var k = segmentStart[row]; k < segments.Count; k++)
                {
                    var s = segments[k];
                    if (s.ToLane == l && s.Kind is SegmentKind.Through or SegmentKind.Bottom)
                    {
                        segments[k] = s with { ToLane = (short)(l - 1) };
                    }
                }
            }

            // Trim trailing free lanes so the graph narrows again.
            while (expect.Count > 1 && expect[^1] < 0)
            {
                expect.RemoveAt(expect.Count - 1);
                color.RemoveAt(color.Count - 1);
            }
        }
        segmentStart[n] = segments.Count;
        return new GraphLayout(nodeLane, nodeColor, segmentStart, segments.ToArray(), maxLanes);
    }

    private static int FirstParent(CommitDag dag, int commit)
    {
        var parents = dag.ParentsOf(commit);
        return parents.Length > 0 ? parents[0] : -1;
    }
}
