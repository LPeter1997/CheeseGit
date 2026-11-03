using CheeseGit.Core.Git;
using CheeseGit.Core.Repository;

namespace CheeseGit.Fixtures;

/// <summary>
/// One history shape of the graph corpus (docs/testing.md, Graph corpus): the tricky topologies, and the shapes
/// the old app drew wrong (#59: commits reachable only through a merge were dropped, and
/// repeated merge-backs were deduplicated to one edge).
/// </summary>
public sealed record GraphCase(string Name, string Description, Action<GraphCaseBuilder> Build)
{
    public override string ToString() => this.Name;
}

/// <summary>Describes a corpus repository: its commits, refs, HEAD and which branches the graph shows.</summary>
public sealed class GraphCaseBuilder
{
    internal FastImportWriter Writer { get; } = new();

    /// <summary>The checked-out branch (lane 0). With <see cref="Detach"/>, the branch being browsed.</summary>
    public string Main { get; set; } = "main";

    /// <summary>A commit to detach HEAD at (history browsing).</summary>
    public int? Detach { get; set; }

    /// <summary>Branches shown besides the required ones; <see cref="ShowAll"/> shows every branch.</summary>
    public List<string> Visible { get; } = [];

    public bool ShowAll { get; set; }

    /// <summary>origin's branches (name → commit); origin/main becomes origin's default branch.</summary>
    public Dictionary<string, int> Remote { get; } = [];

    /// <summary>A commit on <paramref name="branch"/>; the first parent comes first, none makes a root.</summary>
    public int Commit(string branch, string message, params int[] parents) =>
        this.Writer.Commit("refs/heads/" + branch, message, [FileOp.Write(branch.Replace('/', '-') + ".txt", message + "\n")], parents);

    /// <summary>
    /// <paramref name="count"/> commits on <paramref name="branch"/> after <paramref name="parent"/>; returns the last.
    /// They are numbered per branch ("main 3"), so the labelled review images name each commit once.
    /// </summary>
    public int Chain(string branch, int parent, int count)
    {
        for (var i = 0; i < count; i++)
        {
            var n = this.chained[branch] = this.chained.GetValueOrDefault(branch) + 1;
            parent = this.Commit(branch, $"{branch} {n}", parent);
        }
        return parent;
    }

    private readonly Dictionary<string, int> chained = [];

    /// <summary>Points a branch at a commit (for branches whose tip is not their last commit).</summary>
    public void Branch(string branch, int mark) => this.Writer.Ref("refs/heads/" + branch, mark);
}

/// <summary>A generated corpus repository and how the graph is to be shown.</summary>
public sealed record GraphFixture(GraphCase Case, string Path, string MainBranch, IReadOnlyList<string> Visible, bool ShowAll, string? Remote)
{
    /// <summary>Loads the history and chooses the visible branches the way the history tab does.</summary>
    public async Task<HistoryView> LoadViewAsync(GitRunner? git = null, CancellationToken ct = default)
    {
        using var repo = await GitRepository.OpenAsync(this.Path, git, ct);
        var graph = await repo.LoadHistoryAsync(new HistoryRequest(Remote: this.Remote), ct);
        var head = await repo.HeadAsync(ct);
        IEnumerable<string> visible = this.ShowAll ? graph.Branches : [.. HistoryView.RequiredBranches(graph, this.MainBranch), .. this.Visible];
        return HistoryView.Create(graph, visible, this.MainBranch, head.Commit);
    }
}

public static class GraphCorpus
{
    public static IReadOnlyList<GraphCase> Cases { get; } =
    [
        new("linear", "One branch, no merges: a single straight lane.", b =>
        {
            b.Chain("main", b.Commit("main", "root"), 5);
        }),

        new("feature-merged", "A feature branch forked and merged back with --no-ff, shown.", b =>
        {
            var m = b.Chain("main", b.Commit("main", "root"), 2);
            var f = b.Chain("feature", m, 3);
            m = b.Commit("main", "main work", m);
            b.Commit("main", "merge feature", m, f);
            b.Visible.Add("feature");
        }),

        new("octopus", "One merge with four parents.", b =>
        {
            var m = b.Chain("main", b.Commit("main", "root"), 1);
            var x = b.Chain("oct-a", m, 2);
            var y = b.Chain("oct-b", m, 1);
            var z = b.Chain("oct-c", m, 3);
            m = b.Commit("main", "main work", m);
            b.Commit("main", "octopus merge", m, x, y, z);
            b.ShowAll = true;
        }),

        new("criss-cross", "Two branches merge each other, then both reach main.", b =>
        {
            var m = b.Chain("main", b.Commit("main", "root"), 1);
            var x1 = b.Commit("cross-x", "x1", m);
            var y1 = b.Commit("cross-y", "y1", m);
            var x2 = b.Commit("cross-x", "x2 merges y1", x1, y1);
            var y2 = b.Commit("cross-y", "y2 merges x1", y1, x1);
            m = b.Commit("main", "merge cross-x", m, x2);
            b.Commit("main", "merge cross-y", m, y2);
            b.ShowAll = true;
        }),

        new("orphan-roots", "An unrelated history merged in, and an orphan branch that never merged.", b =>
        {
            var m = b.Chain("main", b.Commit("main", "root"), 2);
            var r = b.Chain("imported", b.Commit("imported", "imported root"), 2);
            var docs = b.Chain("docs", b.Commit("docs", "docs root"), 2);
            m = b.Commit("main", "merge unrelated history", m, r);
            b.Chain("main", m, 1);
            b.Commit("docs", "docs later", docs);
            b.ShowAll = true;
        }),

        new("wide", "Ten feature branches open at once, merged back in a staggered order.", b =>
        {
            var m = b.Chain("main", b.Commit("main", "root"), 1);
            var tips = new List<int>();
            for (var i = 0; i < 10; i++)
            {
                tips.Add(b.Chain($"f{i}", m, 1 + i % 3));
                m = b.Commit("main", $"main work {i}", m);
            }
            foreach (var i in new[] { 3, 0, 7, 1, 9, 4 }) m = b.Commit("main", $"merge f{i}", m, tips[i]);
            b.ShowAll = true;
        }),

        new("long-edge", "A branch forked 160 commits back and merged at the top: the edge becomes arrow stubs.", b =>
        {
            var fork = b.Chain("main", b.Commit("main", "root"), 1);
            var f = b.Chain("old-topic", fork, 2);
            var m = b.Chain("main", fork, 160);
            b.Commit("main", "merge old-topic", m, f);
            b.Visible.Add("old-topic");
        }),

        new("issue-59-hidden-merge", "#59: a merged branch, hidden. Its commits stay, reached through the merge.", b =>
        {
            var m = b.Chain("main", b.Commit("main", "root"), 1);
            var f = b.Chain("feature", m, 3);
            m = b.Commit("main", "main work", m);
            m = b.Commit("main", "merge feature", m, f);
            b.Chain("main", m, 1);
        }),

        new("issue-59-nested-merges", "#59: a branch merged into a branch merged into main; only main shown. Nothing beyond the first merge segment may be dropped.", b =>
        {
            var m = b.Chain("main", b.Commit("main", "root"), 1);
            var a = b.Chain("topic-a", m, 2);
            var c = b.Chain("topic-b", a, 2);
            a = b.Commit("topic-a", "topic-a work", a);
            a = b.Commit("topic-a", "merge topic-b", a, c);
            m = b.Commit("main", "main work", m);
            b.Commit("main", "merge topic-a", m, a);
        }),

        new("repeated-merge-backs", "A long-lived branch merged into main three times and main merged into it: every merge keeps its own edge.", b =>
        {
            var m = b.Chain("main", b.Commit("main", "root"), 1);
            var d = b.Commit("develop", "develop starts", m);
            for (var i = 0; i < 3; i++)
            {
                d = b.Chain("develop", d, 1);
                m = b.Commit("main", $"main work {i}", m);
                m = b.Commit("main", $"merge develop {i}", m, d);
                if (i == 1) d = b.Commit("develop", "sync with main", d, m);
            }
            b.Chain("develop", d, 1);
            b.Visible.Add("develop");
        }),

        new("detached-head", "HEAD detached on a hidden branch while browsing main: the commit is still shown.", b =>
        {
            var m = b.Chain("main", b.Commit("main", "root"), 2);
            var spike = b.Chain("spike", m, 2);
            b.Commit("spike", "spike 3", spike);
            b.Chain("main", m, 2);
            b.Detach = spike;
        }),

        new("local-only", "main is two commits ahead of origin/main, and an unpublished branch: hollow dots.", b =>
        {
            var m = b.Chain("main", b.Commit("main", "root"), 2);
            b.Remote["main"] = m;
            b.Chain("wip", m, 2);
            b.Chain("main", m, 2);
            b.Visible.Add("wip");
        }),

        new("stacked-on-main", "feature checked out, three commits straight on top of main: one line, main's part keeps main's colour.", b =>
        {
            var m = b.Chain("main", b.Commit("main", "root"), 2);
            b.Chain("feature", m, 3);
            b.Main = "feature";
        }),

        new("branch-off-moved-main", "feature checked out in lane 0 while main moved on: main's own commits and the shared ones share main's colour.", b =>
        {
            var m = b.Chain("main", b.Commit("main", "root"), 2);
            b.Chain("feature", m, 3);
            b.Chain("main", m, 2);
            b.Main = "feature";
            b.Visible.Add("main");
        }),

        new("branch-off-hidden-main", "As above with main hidden: the shared commits still have main's colour.", b =>
        {
            var m = b.Chain("main", b.Commit("main", "root"), 2);
            b.Chain("feature", m, 3);
            b.Chain("main", m, 2);
            b.Main = "feature";
        }),

        new("main-merged-into-feature", "feature checked out after merging main into it: the edge to main's tip has main's colour all the way, not a stray one.", b =>
        {
            var m = b.Chain("main", b.Commit("main", "root"), 2);
            var f = b.Chain("feature", m, 2);
            m = b.Chain("main", m, 2);
            b.Commit("feature", "merge main into feature", f, m);
            b.Main = "feature";
        }),
    ];

    public static async Task<GraphFixture> CreateAsync(GraphCase graphCase, string directory, CancellationToken ct = default)
    {
        var b = new GraphCaseBuilder();
        graphCase.Build(b);
        Directory.CreateDirectory(directory);
        var emptyConfig = System.IO.Path.Combine(directory, ".fixture-gitconfig");
        await File.WriteAllTextAsync(emptyConfig, "", ct);
        var git = new FixtureGenerator.FixtureGit(emptyConfig);
        var repo = System.IO.Path.Combine(directory, "repo");

        foreach (var (name, mark) in b.Remote) b.Writer.Ref($"refs/remotes/origin/{name}", mark);
        if (b.Detach is { } detach) b.Writer.Ref("refs/corpus/detach", detach);
        await git.ImportAsync(repo, b.Writer, ct);

        if (b.Main != "main")
        {
            await git.RunAsync(repo, ["symbolic-ref", "HEAD", "refs/heads/" + b.Main], ct);
            await git.RunAsync(repo, ["reset", "-q", "--hard"], ct);
        }
        if (b.Detach is not null)
        {
            await git.RunAsync(repo, ["checkout", "-q", "--detach", "refs/corpus/detach"], ct);
            await git.RunAsync(repo, ["update-ref", "-d", "refs/corpus/detach"], ct);
        }
        string? remote = null;
        if (b.Remote.Count > 0)
        {
            remote = "origin";
            await git.RunAsync(repo, ["remote", "add", "origin", "https://example.invalid/corpus.git"], ct);
            if (b.Remote.ContainsKey("main")) await git.RunAsync(repo, ["symbolic-ref", "refs/remotes/origin/HEAD", "refs/remotes/origin/main"], ct);
        }
        return new GraphFixture(graphCase, repo, "refs/heads/" + b.Main, [.. b.Visible.Select(v => "refs/heads/" + v)], b.ShowAll, remote);
    }
}
