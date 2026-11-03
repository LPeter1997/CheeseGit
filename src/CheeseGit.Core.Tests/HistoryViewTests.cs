using CheeseGit.Core.Git;
using CheeseGit.Core.Graph;
using CheeseGit.Core.Repository;

namespace CheeseGit.Core.Tests;

/// <summary>Branch visibility in the history view: filtering by reachability, required branches, lane 0.</summary>
public class HistoryViewTests
{
    private static async Task<(HistoryGraph Graph, string Main, Oid Head)> LoadAsync(TempRepo repo, GitRepository git)
    {
        var graph = await git.LoadHistoryAsync(new HistoryRequest());
        return (graph, "refs/heads/" + repo.CurrentBranch(), Oid.ParseHex(repo.Head()));
    }

    private static string[] Summaries(GitRepository git, HistoryView view) =>
        [.. Enumerable.Range(0, view.Count).Select(row => git.GetCommit(view.Dag.OidAt(row))!.Summary)];

    [Fact]
    public async Task Hiding_a_branch_removes_only_its_own_commits()
    {
        using var repo = RepositoryTests.RepoWithCommit();
        var main = repo.CurrentBranch();
        repo.Raw("switch", "-q", "-c", "feature");
        repo.CommitFile("f.txt", "f", "feature work");
        repo.Raw("switch", "-q", main);
        repo.CommitFile("m.txt", "m", "main work");
        using var git = await repo.OpenAsync();
        var (graph, mainRef, head) = await LoadAsync(repo, git);

        var shown = HistoryView.Create(graph, graph.Branches, mainRef, head);
        Assert.Contains("feature work", Summaries(git, shown));
        Assert.Null(LayoutValidator.Validate(shown.Dag, shown.Layout));

        var hidden = HistoryView.Create(graph, [], mainRef, head);
        Assert.Equal(["main work", "initial commit"], Summaries(git, hidden));
        Assert.Equal([mainRef], hidden.VisibleBranches);
        Assert.Null(LayoutValidator.Validate(hidden.Dag, hidden.Layout));
    }

    [Fact]
    public async Task Hiding_a_merged_branch_keeps_every_commit_reachable_through_the_merge()
    {
        // #59: the old layout dropped commits reachable only through a merge's
        // second parent once their branch was hidden.
        using var repo = RepositoryTests.RepoWithCommit();
        var main = repo.CurrentBranch();
        repo.Raw("switch", "-q", "-c", "topic");
        repo.CommitFile("t1.txt", "1", "topic 1");
        repo.CommitFile("t2.txt", "2", "topic 2");
        repo.Raw("switch", "-q", main);
        repo.CommitFile("m.txt", "m", "main work");
        repo.Raw("merge", "-q", "--no-ff", "-m", "merge topic", "topic");
        using var git = await repo.OpenAsync();
        var (graph, mainRef, head) = await LoadAsync(repo, git);

        var view = HistoryView.Create(graph, [], mainRef, head);
        Assert.Equal(5, view.Count);
        Assert.Contains("topic 1", Summaries(git, view));
        Assert.Null(LayoutValidator.Validate(view.Dag, view.Layout));
        // The merge's first-parent chain is lane 0; the topic commits are not.
        Assert.Equal(0, view.Layout.NodeLane(0));
        var topicRow = Array.IndexOf(Summaries(git, view), "topic 2");
        Assert.NotEqual(0, view.Layout.NodeLane(topicRow));
    }

    [Fact]
    public async Task Branches_under_the_current_one_are_required()
    {
        using var repo = RepositoryTests.RepoWithCommit();
        var main = repo.CurrentBranch();
        repo.Raw("branch", "base"); // tip on main's first-parent chain
        repo.CommitFile("a.txt", "a", "on top of base");
        repo.Raw("switch", "-q", "-c", "side", "HEAD~1");
        repo.CommitFile("s.txt", "s", "side work"); // forks off: not required
        repo.Raw("switch", "-q", main);
        repo.Raw("branch", "same-tip"); // points at main's tip
        using var git = await repo.OpenAsync();
        var (graph, mainRef, _) = await LoadAsync(repo, git);

        var required = HistoryView.RequiredBranches(graph, mainRef);
        Assert.Equal(mainRef, required[0]);
        Assert.Contains("refs/heads/base", required);
        Assert.Contains("refs/heads/same-tip", required);
        Assert.DoesNotContain("refs/heads/side", required);
        Assert.Empty(HistoryView.RequiredBranches(graph, "refs/heads/missing"));
    }

    [Fact]
    public async Task Detached_head_is_shown_and_the_browsed_branch_keeps_lane_zero()
    {
        using var repo = RepositoryTests.RepoWithCommit();
        var main = repo.CurrentBranch();
        repo.Raw("switch", "-q", "-c", "other");
        repo.CommitFile("o1.txt", "1", "other 1");
        repo.CommitFile("o2.txt", "2", "other 2");
        repo.Raw("switch", "-q", main);
        repo.CommitFile("m.txt", "m", "main work");
        repo.Raw("switch", "-q", "--detach", "other~1");
        using var git = await repo.OpenAsync();
        var graph = await git.LoadHistoryAsync(new HistoryRequest());
        var head = Oid.ParseHex(repo.Head());

        // Browsing main's history while HEAD sits on a commit only "other" has.
        var view = HistoryView.Create(graph, [], "refs/heads/" + main, head);
        var summaries = Summaries(git, view);
        Assert.Contains("other 1", summaries);
        Assert.DoesNotContain("other 2", summaries);
        Assert.Equal(0, view.Layout.NodeLane(view.MainTip));
        Assert.Equal("main work", summaries[view.MainTip]);
        Assert.Null(LayoutValidator.Validate(view.Dag, view.Layout));
    }

    [Fact]
    public async Task Unknown_branches_are_ignored_and_rows_map_back()
    {
        using var repo = RepositoryTests.RepoWithCommit();
        repo.CommitFile("a.txt", "a", "second");
        using var git = await repo.OpenAsync();
        var (graph, mainRef, head) = await LoadAsync(repo, git);

        var view = HistoryView.Create(graph, ["refs/heads/gone", mainRef], mainRef, head);
        Assert.Equal([mainRef], view.VisibleBranches);
        for (var row = 0; row < view.Count; row++)
        {
            Assert.Equal(view.Dag.OidAt(row), graph.Dag.OidAt(view.OriginalIndex(row)));
        }
    }

    [Fact]
    public async Task Commit_summaries_cover_every_branch_with_subject_and_author()
    {
        using var repo = RepositoryTests.RepoWithCommit();
        var main = repo.CurrentBranch();
        repo.Raw("switch", "-q", "-c", "side");
        repo.Write("s.txt", "s");
        repo.Raw("add", "s.txt");
        repo.Raw("-c", "user.name=Zoë Ünicode", "commit", "-q", "-m", "Größe ändern\n\nbody line");
        repo.Raw("switch", "-q", main);
        using var git = await repo.OpenAsync();

        var summaries = await git.CommitSummariesAsync([$"refs/heads/{main}", "refs/heads/side"]);
        Assert.Equal(2, summaries.Count);
        var side = summaries.Single(s => s.Oid == Oid.ParseHex(repo.Raw("rev-parse", "side").Trim()));
        Assert.Equal("Größe ändern", side.Summary);
        Assert.Equal("Zoë Ünicode", side.Author);
        Assert.Empty(await git.CommitSummariesAsync([]));
    }
}
