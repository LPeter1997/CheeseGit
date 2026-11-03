using System.Collections;
using CheeseGit.Core.Git;
using CheeseGit.Core.Graph;

namespace CheeseGit.Core.Tests;

public class GraphLayoutTests
{
    /// <summary>
    /// Builds a random history: mostly linear branches, with forks, merges,
    /// octopus merges and extra roots. Commits are created oldest first and then
    /// listed newest first, which is a valid topological order.
    /// </summary>
    private static CommitDag RandomDag(int seed, int count)
    {
        var rng = new Random(seed);
        var parentsOf = new List<int[]>(); // by creation order
        var heads = new List<int>();
        for (var c = 0; c < count; c++)
        {
            int[] parents;
            if (c == 0 || rng.NextDouble() < 0.01)
            {
                parents = []; // a new root
            }
            else
            {
                var first = heads.Count > 0 && rng.NextDouble() < 0.9 ? heads[rng.Next(heads.Count)] : rng.Next(c);
                var extra = rng.NextDouble() switch { < 0.12 => 1, < 0.13 => 3, _ => 0 };
                parents = [first, .. Enumerable.Range(0, extra).Select(_ => rng.Next(c)).Where(p => p != first).Distinct()];
            }
            parentsOf.Add(parents);
            foreach (var p in parents) heads.Remove(p);
            heads.Add(c);
            if (rng.NextDouble() < 0.05 && heads.Count > 1) heads.RemoveAt(rng.Next(heads.Count)); // abandon a branch
        }

        var builder = new CommitDag.Builder();
        for (var c = count - 1; c >= 0; c--)
        {
            builder.BeginCommit(FakeOid(c), c);
            foreach (var p in parentsOf[c]) builder.AddParent(FakeOid(p));
        }
        return builder.Build();
    }

    // The counter goes first: Oid hashes its first 8 bytes (real ids are random), so
    // zero-padded ids all collided and building a 80k-commit DAG took 67 s.
    private static Oid FakeOid(int n) => Oid.ParseHex(n.ToString("x8") + new string('0', 32));

    public static TheoryData<int> Seeds => [.. Enumerable.Range(1, 40)];

    [Theory]
    [MemberData(nameof(Seeds))]
    public void Every_edge_is_drawn(int seed)
    {
        var dag = RandomDag(seed, 2000);
        var mainTip = new Random(seed).Next(dag.Count / 10);
        foreach (var maxEdge in new[] { int.MaxValue, 100, 10, 1 })
        {
            var layout = GraphLayout.Compute(dag, mainTip, maxEdge);
            Assert.Null(LayoutValidator.Validate(dag, layout));
        }
    }

    [Theory]
    [MemberData(nameof(Seeds))]
    public void Main_chain_stays_in_lane_zero(int seed)
    {
        var dag = RandomDag(seed, 2000);
        var layout = GraphLayout.Compute(dag, mainTip: 0, maxEdgeRows: 5);
        for (var c = 0; c >= 0;)
        {
            Assert.Equal(0, layout.NodeLane(c));
            var parents = dag.ParentsOf(c);
            c = parents.Length > 0 ? parents[0] : -1;
        }
        // The main chain's own edges are straight lines in lane 0.
        for (var c = 0; c >= 0;)
        {
            var parents = dag.ParentsOf(c);
            if (parents.Length == 0 || parents[0] < 0) break;
            var firstEdge = layout.SegmentsOf(c).ToArray().First(s => s.Kind is SegmentKind.Bottom or SegmentKind.StubDown);
            Assert.Equal((0, 0, SegmentKind.Bottom), ((int)firstEdge.FromLane, (int)firstEdge.ToLane, firstEdge.Kind));
            c = parents[0];
        }
        for (var row = 0; row < dag.Count; row++)
        {
            Assert.True(layout.NodeLane(row) != 0 || IsOnFirstParentChain(dag, 0, row), $"row {row} is not main but uses lane 0");
        }
    }

    [Theory]
    [MemberData(nameof(Seeds))]
    public void Hiding_branches_keeps_every_reachable_commit_and_edge(int seed)
    {
        var dag = RandomDag(seed, 2000);
        var rng = new Random(seed);
        var tips = Enumerable.Range(0, 5).Select(_ => rng.Next(dag.Count)).ToList();
        var keep = dag.ReachableFrom(tips);
        var (sub, original) = dag.Filter(keep);

        // Closed under parents: every parent of a kept commit is kept, so no edge is lost.
        for (var i = 0; i < sub.Count; i++)
        {
            Assert.Equal(dag.ParentsOf(original[i]).Length, sub.ParentsOf(i).Length);
            foreach (var p in sub.ParentsOf(i)) Assert.True(p > i);
        }
        Assert.Equal(Count(keep), sub.Count);
        Assert.Null(LayoutValidator.Validate(sub, GraphLayout.Compute(sub, sub.Count > 0 ? 0 : -1, 50)));
    }

    [Fact]
    public void Colors_stay_valid_past_32k_side_chains()
    {
        // main merges a one-commit side branch 40,000 times: every side commit starts a new
        // colour chain. Colours are shorts and were incremented without bound, so they wrapped
        // negative, and the palette lookup (colour % palette size) indexed out of range.
        const int merges = 40_000;
        var builder = new CommitDag.Builder();
        for (var i = merges; i >= 1; i--)
        {
            // merge i: parents main (merge i-1, or the root) and side i.
            builder.BeginCommit(FakeOid(2 * i), 2 * i);
            builder.AddParent(FakeOid(i == 1 ? 0 : 2 * (i - 1)));
            builder.AddParent(FakeOid(2 * i - 1));
            builder.BeginCommit(FakeOid(2 * i - 1), 2 * i - 1);
            builder.AddParent(FakeOid(i == 1 ? 0 : 2 * (i - 1)));
        }
        builder.BeginCommit(FakeOid(0), 0);
        var dag = builder.Build();
        var layout = GraphLayout.Compute(dag, 0);
        Assert.Null(LayoutValidator.Validate(dag, layout));
        for (var row = 0; row < layout.RowCount; row++)
        {
            Assert.True(layout.NodeColor(row) >= 0, $"row {row} has colour {layout.NodeColor(row)}");
            foreach (var s in layout.SegmentsOf(row)) Assert.True(s.Color >= 0, $"row {row} has a segment of colour {s.Color}");
        }
    }

    [Fact]
    public void Merging_master_into_a_branch_draws_the_edge_in_master_colour()
    {
        // feature merges master: the edge to master's tip opened a lane in a new colour,
        // which turned into master's colour only at the tip's dot.
        //   4 merge (feature)  parents 3, 2
        //   3 feature          parent 1
        //   2 master tip       parent 1
        //   1 master           parent 0
        //   0 root
        var builder = new CommitDag.Builder();
        void Commit(int c, params int[] parents)
        {
            builder.BeginCommit(FakeOid(c), c);
            foreach (var p in parents) builder.AddParent(FakeOid(p));
        }
        Commit(4, 3, 2);
        Commit(3, 1);
        Commit(2, 1);
        Commit(1, 0);
        Commit(0);
        var dag = builder.Build();
        // Rows follow the listing: 0 merge, 1 feature, 2 master tip, 3 master, 4 root.
        bool[] isBase = [false, false, true, true, true];

        foreach (var maxEdge in new[] { int.MaxValue, 1 })
        {
            var layout = GraphLayout.Compute(dag, mainTip: 0, maxEdgeRows: maxEdge, baseRows: isBase);
            Assert.Null(LayoutValidator.Validate(dag, layout));
            Assert.Equal(0, layout.NodeColor(2));
            var feature = layout.NodeColor(0);
            Assert.NotEqual(0, feature);
            // Only two chains exist, so only two colours may be drawn.
            for (var row = 0; row < dag.Count; row++)
            {
                foreach (var seg in layout.SegmentsOf(row))
                {
                    Assert.True(seg.Color == 0 || seg.Color == feature, $"row {row}: {seg} (max edge {maxEdge})");
                }
            }
        }
    }

    [Fact]
    public void Validator_rejects_a_dropped_edge()
    {
        // The old layout's bug class: a merge whose second-parent edge is not drawn.
        static CommitDag Diamond(bool withMergeEdge)
        {
            var builder = new CommitDag.Builder();
            builder.BeginCommit(FakeOid(3), 3);
            builder.AddParent(FakeOid(2));
            if (withMergeEdge) builder.AddParent(FakeOid(1));
            builder.BeginCommit(FakeOid(2), 2);
            builder.AddParent(FakeOid(0));
            builder.BeginCommit(FakeOid(1), 1);
            builder.AddParent(FakeOid(0));
            builder.BeginCommit(FakeOid(0), 0);
            return builder.Build();
        }

        var real = Diamond(withMergeEdge: true);
        Assert.Null(LayoutValidator.Validate(real, GraphLayout.Compute(real, 0)));
        var layoutMissingEdge = GraphLayout.Compute(Diamond(withMergeEdge: false), 0);
        Assert.NotNull(LayoutValidator.Validate(real, layoutMissingEdge));
    }

    private static bool IsOnFirstParentChain(CommitDag dag, int tip, int target)
    {
        for (var c = tip; c >= 0;)
        {
            if (c == target) return true;
            var parents = dag.ParentsOf(c);
            c = parents.Length > 0 ? parents[0] : -1;
        }
        return false;
    }

    private static int Count(BitArray bits)
    {
        var n = 0;
        foreach (bool b in bits) if (b) n++;
        return n;
    }
}
