using CheeseGit.Core.Git;
using CheeseGit.Core.Graph;

namespace CheeseGit.Core.Repository;

/// <summary>
/// What the history view draws: the commits reachable from the visible
/// branches, laid out. Hiding a branch filters the loaded DAG by reachability
/// and lays it out again (docs/architecture.md, Graph); the old app reassigned commits to
/// branches by a first-parent ownership walk and dropped the rest (#59).
/// </summary>
public sealed class HistoryView
{
    /// <summary>
    /// Edges that would hold a lane open for longer are drawn as arrow stubs.
    /// Drawn literally, git/git needs 157 parallel lanes; with stubs and lane
    /// compaction, a median of 6 (Phase 0).
    /// </summary>
    public const int MaxEdgeRows = 150;

    private readonly int[] originalIndex;

    private HistoryView(HistoryGraph graph, IReadOnlyList<string> visible, CommitDag dag, int[] originalIndex, GraphLayout layout, int mainTip)
    {
        this.Graph = graph;
        this.VisibleBranches = visible;
        this.Dag = dag;
        this.originalIndex = originalIndex;
        this.Layout = layout;
        this.MainTip = mainTip;
    }

    public HistoryGraph Graph { get; }

    /// <summary>The full ref names whose history is drawn.</summary>
    public IReadOnlyList<string> VisibleBranches { get; }

    /// <summary>The drawn commits, in display order.</summary>
    public CommitDag Dag { get; }

    public GraphLayout Layout { get; }

    /// <summary>The row whose first-parent chain sits in lane 0, or -1.</summary>
    public int MainTip { get; }

    public int Count => this.Dag.Count;

    /// <summary>The row's index in <see cref="HistoryGraph.Dag"/>.</summary>
    public int OriginalIndex(int row) => this.originalIndex[row];

    public bool IsLocalOnly(int row) => this.Graph.LocalOnly.Contains(this.Dag.OidAt(row));

    /// <summary>
    /// Lays out the history of <paramref name="visibleBranches"/> (full ref
    /// names; unknown ones are ignored). <paramref name="mainBranch"/> (the
    /// current branch, or the one whose history is being browsed) is always
    /// shown, and its first-parent chain sits in lane 0. A detached
    /// <paramref name="head"/> is always shown too.
    /// </summary>
    public static HistoryView Create(HistoryGraph graph, IEnumerable<string> visibleBranches, string? mainBranch, Oid? head)
    {
        var visible = new List<string>();
        foreach (var branch in mainBranch is null ? visibleBranches : visibleBranches.Prepend(mainBranch))
        {
            if (graph.Tips.ContainsKey(branch) && !visible.Contains(branch)) visible.Add(branch);
        }

        var full = graph.Dag;
        var tips = visible.Select(b => full.IndexOf(graph.Tips[b])).ToList();
        var headRow = head is { } h ? full.IndexOf(h) : -1;
        tips.Add(headRow);

        var (dag, originalIndex) = full.Filter(full.ReachableFrom(tips));
        var mainTip = mainBranch is not null && graph.Tips.TryGetValue(mainBranch, out var tip) ? dag.IndexOf(tip)
            : head is { } detached ? dag.IndexOf(detached)
            : -1;
        var layout = GraphLayout.Compute(dag, mainTip, MaxEdgeRows, BaseRows(graph, dag, mainBranch));
        return new HistoryView(graph, visible, dag, originalIndex, layout, mainTip);
    }

    /// <summary>
    /// The default branch's first-parent chain, walked in the full history so that its commits
    /// keep their colour when the branch itself is hidden. Null when there is no default branch
    /// or it is the main branch (then lane 0 is its colour anyway).
    /// </summary>
    private static bool[]? BaseRows(HistoryGraph graph, CommitDag dag, string? mainBranch)
    {
        if (graph.DefaultBranch is not { } name || name == mainBranch || !graph.Tips.TryGetValue(name, out var tip)) return null;
        var full = graph.Dag;
        var rows = new bool[dag.Count];
        var any = false;
        for (var c = full.IndexOf(tip); c >= 0;)
        {
            if (dag.IndexOf(full.OidAt(c)) is var row and >= 0) rows[row] = any = true;
            var parents = full.ParentsOf(c);
            c = parents.Length > 0 ? parents[0] : -1;
        }
        return any ? rows : null;
    }

    /// <summary>
    /// Branches that cannot be hidden: <paramref name="mainBranch"/> and the
    /// local branches whose tips lie on its first-parent chain (it was built
    /// on top of them), as in the old app. Hiding one of those would hide
    /// nothing anyway, because its commits are reachable from the main branch.
    /// </summary>
    public static IReadOnlyList<string> RequiredBranches(HistoryGraph graph, string? mainBranch)
    {
        if (mainBranch is null || !graph.Tips.TryGetValue(mainBranch, out var tip)) return [];
        var tipOwners = new Dictionary<Oid, List<string>>();
        foreach (var (name, target) in graph.Tips)
        {
            if (name == mainBranch || !name.StartsWith("refs/heads/", StringComparison.Ordinal)) continue;
            if (!tipOwners.TryGetValue(target, out var list)) tipOwners[target] = list = [];
            list.Add(name);
        }

        var required = new List<string> { mainBranch };
        var dag = graph.Dag;
        for (var c = dag.IndexOf(tip); c >= 0;)
        {
            if (tipOwners.TryGetValue(dag.OidAt(c), out var owners)) required.AddRange(owners.Order(StringComparer.Ordinal));
            var parents = dag.ParentsOf(c);
            c = parents.Length > 0 ? parents[0] : -1;
        }
        return required;
    }
}
