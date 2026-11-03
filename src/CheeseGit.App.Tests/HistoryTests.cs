using Avalonia.Headless.XUnit;
using CheeseGit.App.ViewModels;
using CheeseGit.Core.Tests;

namespace CheeseGit.App.Tests;

/// <summary>Phase 3: the history tab over real repositories.</summary>
public class HistoryTests
{
    /// <summary>main: initial, base work, main work; feature forks after "base work".</summary>
    private static TempRepo RepoWithBranches()
    {
        var repo = TempRepo.Init();
        repo.Write("a.txt", "a\n");
        repo.CommitAll("initial");
        repo.CommitFile("b.txt", "b\n", "base work");
        var main = repo.CurrentBranch();
        repo.Raw("switch", "-q", "-c", "feature");
        repo.CommitFile("f.txt", "f\n", "feature work");
        repo.Raw("switch", "-q", main);
        repo.CommitFile("a.txt", "a\nmore\n", "main work");
        return repo;
    }

    private static async Task<HistoryViewModel> ShowHistoryAsync(AppHarness app)
    {
        app.Tab.LeftTab = LeftPanelTab.History;
        var history = app.Tab.History;
        await AppHarness.Until(() => history.Rows.Count > 0, "the history to load");
        return history;
    }

    [AvaloniaFact]
    public async Task A_switch_blocks_the_view_until_the_history_shows_the_new_branch()
    {
        using var repo = RepoWithBranches();
        var featureHead = repo.Raw("rev-parse", "feature").Trim();
        using var app = await AppHarness.OpenAsync(repo);
        var history = await ShowHistoryAsync(app);

        var switching = app.Tab.SwitchAsync("Switch to feature", "feature", (r, ct) => r.SwitchBranchAsync("feature", ct));
        // Input is refused at once; the dimming waits, so a quick switch does not flash.
        Assert.True(app.Tab.IsBlocked);
        Assert.Equal("Switch to feature…", app.Tab.BlockingText);
        Assert.False(app.Tab.ShowBlockingOverlay);

        await AppHarness.Until(() => switching.IsCompleted, "the switch");
        Assert.True(await switching);

        // Unblocked only once the history shows the branch it switched to.
        Assert.False(app.Tab.IsBlocked);
        Assert.False(app.Tab.ShowBlockingOverlay);
        Assert.Equal("feature", repo.CurrentBranch());
        Assert.Equal(featureHead, history.Rows[0].Oid.ToHex());
    }

    [AvaloniaFact]
    public async Task A_failed_switch_unblocks_the_view()
    {
        using var repo = RepoWithBranches();
        using var app = await AppHarness.OpenAsync(repo);
        await ShowHistoryAsync(app);

        Assert.False(await app.Tab.SwitchAsync("Switch to nowhere", "nowhere", (r, ct) => r.SwitchBranchAsync("nowhere", ct)));
        Assert.False(app.Tab.IsBlocked);
        Assert.False(app.Tab.ShowBlockingOverlay);
    }

    /// <summary>Reads every row's text the way realized rows do, and waits for it.</summary>
    private static async Task<List<string>> SummariesAsync(HistoryViewModel history)
    {
        _ = history.Rows.Select(r => r.Summary).ToList();
        await AppHarness.Until(() => history.Rows.All(r => r.Summary.Length > 0), "every row's metadata");
        return history.Rows.Select(r => r.Summary).ToList();
    }

    [AvaloniaFact]
    public async Task History_shows_the_current_branch_and_metadata_arrives_for_every_row()
    {
        using var repo = RepoWithBranches();
        using var app = await AppHarness.OpenAsync(repo);
        var history = await ShowHistoryAsync(app);

        // Only the required branches at first (old app): feature's own commit is hidden.
        Assert.Equal(["main work", "base work", "initial"], await SummariesAsync(history));
        Assert.True(history.Rows[0].IsHead);
        Assert.True(history.Rows[0].CanUndo);
        Assert.Equal(0, history.View!.Layout.NodeLane(0));
        await AppHarness.Until(() => history.Rows[0].Stats is not null, "the first row's stats");
        Assert.Equal("+1", history.Rows[0].AdditionsText);
    }

    [AvaloniaFact]
    public async Task Rows_above_a_selected_row_still_get_their_metadata()
    {
        using var repo = TempRepo.Init();
        for (var i = 0; i < 40; i++) repo.CommitFile("a.txt", $"{i}\n", $"commit {i}");
        using var app = await AppHarness.OpenAsync(repo);
        var history = await ShowHistoryAsync(app);

        // The snapshot's order: select a row (its label reads its text) before the list realizes the rows above it.
        history.Select(history.Rows[3]);
        _ = history.ContextLabel;
        await AppHarness.Until(() => history.Details.Files.Count > 0, "the commit's files");
        Assert.Equal(40, (await SummariesAsync(history)).Count);
    }

    [AvaloniaFact]
    public async Task Showing_and_hiding_a_branch_lays_the_graph_out_again()
    {
        using var repo = RepoWithBranches();
        using var app = await AppHarness.OpenAsync(repo);
        var history = await ShowHistoryAsync(app);

        var feature = history.Branches.Single(b => b.Name == "feature");
        Assert.False(feature.IsVisible);
        Assert.True(history.Branches.Single(b => b.Name == repo.CurrentBranch()).IsRequired);
        feature.ToggleCommand.Execute(null);
        await AppHarness.Until(() => history.Rows.Count == 4, "feature's commit to appear");
        Assert.Contains("feature work", await SummariesAsync(history));
        Assert.True(history.AllBranchesVisible);

        history.ToggleAllCommand.Execute(null);
        await AppHarness.Until(() => history.Rows.Count == 3, "feature's commit to disappear");
        Assert.False(history.Branches.Single(b => b.Name == "feature").IsVisible);
    }

    /// <summary>A repository with an origin whose default branch (origin/HEAD) is <c>pick(current branch)</c>.</summary>
    private static TempRepo RepoWithOrigin(Func<string, (string Branch, string Target)> pick)
    {
        var repo = RepoWithBranches();
        var (branch, target) = pick(repo.CurrentBranch());
        repo.Raw("remote", "add", "origin", "https://example.invalid/repo.git");
        repo.Raw("update-ref", $"refs/remotes/origin/{branch}", target);
        repo.Raw("symbolic-ref", "refs/remotes/origin/HEAD", $"refs/remotes/origin/{branch}");
        return repo;
    }

    [AvaloniaFact]
    public async Task The_remote_default_branch_gets_no_row_beside_its_local_branch_but_show_all_draws_it()
    {
        // origin/main beside main, one commit behind.
        using var repo = RepoWithOrigin(main => (main, $"{main}~1"));
        var main = repo.CurrentBranch();
        using var app = await AppHarness.OpenAsync(repo);
        var history = await ShowHistoryAsync(app);
        await AppHarness.Until(() => history.Branches.Count > 0, "the branch list");

        Assert.Equal([main, "feature"], history.Branches.Select(b => b.Name));
        Assert.DoesNotContain(history.BranchItems, i => i is BranchListHeader);

        history.ToggleAllCommand.Execute(null);
        await AppHarness.Until(() => history.View!.VisibleBranches.Contains($"refs/remotes/origin/{main}"), "show all to draw the remote branch");
        Assert.True(history.AllBranchesVisible);
        history.ToggleAllCommand.Execute(null);
        await AppHarness.Until(() => !history.View!.VisibleBranches.Contains($"refs/remotes/origin/{main}"), "hide all to hide it");
    }

    [AvaloniaFact]
    public async Task A_remote_default_branch_with_no_local_branch_is_listed_under_remote()
    {
        using var repo = RepoWithOrigin(_ => ("upstream-only", "feature"));
        using var app = await AppHarness.OpenAsync(repo);
        var history = await ShowHistoryAsync(app);
        await AppHarness.Until(() => history.Branches.Count > 0, "the branch list");

        var main = repo.CurrentBranch();
        Assert.Equal([main, "feature", "Remote", "upstream-only"],
            history.BranchItems.Select(i => i is GraphBranch b ? b.Name : ((BranchListHeader)i).Text));
        var remote = history.Branches[^1];
        Assert.True(remote.IsRemote);
        Assert.Equal("origin", remote.Remote);
        Assert.False(remote.IsVisible);
    }

    [AvaloniaFact]
    public async Task A_large_history_without_a_commit_graph_is_offered_one_once()
    {
        using var repo = RepoWithBranches();
        using var app = await AppHarness.OpenAsync(repo);
        var (shell, tab) = (app.Main, app.Tab);
        tab.History.CommitGraphOfferCommits = 3; // The history loads when its tab is first shown.
        await ShowHistoryAsync(app);

        await AppHarness.Until(() => shell.Alerts.Any(a => a.HasAction && a.ActionLabel == "Write commit-graph"), "the offer");
        Assert.Contains(repo.Path, shell.Settings.CommitGraphOffered);
        var offer = shell.Alerts.Single(a => a.ActionLabel == "Write commit-graph");
        await offer.RunActionCommand.ExecuteAsync(null);
        Assert.True(File.Exists(repo.FullPath(".git/objects/info/commit-graph")));
        using (var git = await repo.OpenAsync()) Assert.False(await git.CommitGraphMissingAsync());

        // Never again for this repository, even after the file is gone.
        // git writes it read-only, which Windows will not delete.
        File.SetAttributes(repo.FullPath(".git/objects/info/commit-graph"), FileAttributes.Normal);
        File.Delete(repo.FullPath(".git/objects/info/commit-graph"));
        Assert.False(tab.MarkCommitGraphOffered());
    }

    [AvaloniaFact]
    public async Task Hovering_a_dot_fades_the_other_lane_chains()
    {
        using var repo = RepoWithBranches();
        using var app = await AppHarness.OpenAsync(repo);
        var history = await ShowHistoryAsync(app);
        history.Branches.Single(b => b.Name == "feature").ToggleCommand.Execute(null);
        await AppHarness.Until(() => history.Rows.Count == 4, "feature's commit");
        history.SetVisibleRange(0, 3);
        var summaries = await SummariesAsync(history);
        var feature = history.Rows[summaries.IndexOf("feature work")];
        var main = history.Rows[summaries.IndexOf("main work")];
        Assert.All(history.Rows, r => Assert.Equal(1, r.TextOpacity));

        var notified = new List<string>();
        main.PropertyChanged += (_, e) => notified.Add(e.PropertyName!);
        history.HoverDot(feature);
        Assert.Equal(1, history.GraphFade);
        Assert.Equal(history.View!.Layout.NodeColor(feature.Row), history.HighlightColor);
        Assert.Equal(1, feature.TextOpacity);
        Assert.Equal(0.3, main.TextOpacity);
        Assert.Contains(nameof(CommitRow.TextOpacity), notified);

        history.HoverDot(null);
        Assert.Equal(0, history.GraphFade);
        Assert.All(history.Rows, r => Assert.Equal(1, r.TextOpacity));
    }

    [AvaloniaFact]
    public async Task Selecting_a_commit_lists_its_files_and_shows_their_diff()
    {
        using var repo = RepoWithBranches();
        using var app = await AppHarness.OpenAsync(repo);
        var history = await ShowHistoryAsync(app);
        await SummariesAsync(history);

        history.Select(history.Rows[0]);
        await AppHarness.Until(() => history.Details.Files.Count > 0, "the commit's files");
        var file = Assert.Single(history.Details.Files);
        Assert.Equal("a.txt", file.DisplayPath);
        Assert.Equal("+1", file.AdditionsText);
        Assert.Equal($"{history.Rows[0].ShortHash} - main work", history.ContextLabel);

        history.Details.SelectFile(file);
        await AppHarness.Until(() => history.Diff.Diff is not null, "the file's diff");
        Assert.Equal(1, history.Diff.Diff!.Additions);
        Assert.Same(history.Diff, app.Tab.ActiveDiff);
    }

    [AvaloniaFact]
    public async Task A_new_commit_reloads_the_history_and_keeps_the_selection()
    {
        using var repo = RepoWithBranches();
        using var app = await AppHarness.OpenAsync(repo);
        var history = await ShowHistoryAsync(app);
        await SummariesAsync(history);
        history.Select(history.Rows[1]);

        repo.CommitFile("c.txt", "c\n", "outside commit");
        await AppHarness.Until(() => history.Rows.Count == 4, "the new commit");
        Assert.Equal("outside commit", (await SummariesAsync(history))[0]);
        Assert.Equal("base work", history.Selected?.Summary);
        Assert.True(history.Rows[2].IsSelected);
        Assert.True(history.Rows[0].IsHead);
    }

    [AvaloniaFact]
    public async Task Checking_out_a_commit_browses_history_and_jumping_back_returns_to_the_branch()
    {
        using var repo = RepoWithBranches();
        var main = repo.CurrentBranch();
        using var app = await AppHarness.OpenAsync(repo);
        var history = await ShowHistoryAsync(app);
        await SummariesAsync(history);

        await history.CheckoutCommand.ExecuteAsync(history.Rows[1]);
        await AppHarness.Until(() => app.Tab.BrowsingHistory, "history browsing");
        await AppHarness.Until(() => history.Rows.Any(r => r.IsHead && r.Summary == "base work"), "HEAD on base work");
        // The browsed branch keeps lane 0 and its newer commits stay visible.
        Assert.Equal("main work", (await SummariesAsync(history))[0]);
        Assert.Equal(0, history.View!.Layout.NodeLane(0));
        Assert.False(history.Rows[1].CanUndo);

        await history.JumpToPresentCommand.ExecuteAsync(null);
        await AppHarness.Until(() => !app.Tab.BrowsingHistory, "the branch to be checked out again");
        Assert.Equal(main, repo.CurrentBranch());
    }

    [AvaloniaFact]
    public async Task Checking_out_a_branch_tip_switches_to_the_branch()
    {
        using var repo = RepoWithBranches();
        using var app = await AppHarness.OpenAsync(repo);
        var history = await ShowHistoryAsync(app);
        history.Branches.Single(b => b.Name == "feature").ToggleCommand.Execute(null);
        await AppHarness.Until(() => history.Rows.Count == 4, "feature's commit");
        var summaries = await SummariesAsync(history);

        await history.CheckoutCommand.ExecuteAsync(history.Rows[summaries.IndexOf("feature work")]);
        await AppHarness.Until(() => app.Tab.Snapshot?.Head.Branch == "feature" && !app.Tab.BrowsingHistory, "the switch to feature");
        Assert.Equal("feature", repo.CurrentBranch());
    }

    [AvaloniaFact]
    public async Task Undo_from_the_history_restores_the_commit_to_staging()
    {
        using var repo = RepoWithBranches();
        using var app = await AppHarness.OpenAsync(repo);
        var history = await ShowHistoryAsync(app);
        await SummariesAsync(history);

        await history.UndoCommitCommand.ExecuteAsync(history.Rows[0]);
        await AppHarness.Until(() => app.StagedPaths.Contains("a.txt"), "the undone change to be staged");
        Assert.Equal(LeftPanelTab.Staging, app.Tab.LeftTab);
        Assert.Equal("main work", app.Staging.Summary);
        await AppHarness.Until(() => history.Rows.Count == 2, "the history to drop the commit");
    }

    [AvaloniaFact]
    public async Task Search_matches_subject_author_id_and_branch_names_across_the_whole_history()
    {
        using var repo = RepoWithBranches();
        using var app = await AppHarness.OpenAsync(repo);
        var history = await ShowHistoryAsync(app);
        history.ToggleAllCommand.Execute(null);
        await AppHarness.Until(() => history.Rows.Count == 4, "every branch");

        async Task<List<string>> SearchAsync(string query)
        {
            history.SearchQuery = query;
            await AppHarness.Until(() => history.ShownQuery == query, $"results for '{query}'");
            _ = history.DisplayRows.Select(r => r.Summary).ToList();
            await AppHarness.Until(() => history.DisplayRows.All(r => r.Summary.Length > 0), "the results' text");
            return history.DisplayRows.Select(r => r.Summary).ToList();
        }

        Assert.Equal(["base work", "feature work", "main work"], (await SearchAsync("WORK")).Order());
        Assert.True(history.IsSearching);
        Assert.Equal(["feature work"], await SearchAsync("feature")); // the subject and the branch name
        var initial = history.Rows.Single(r => r.Summary == "initial");
        Assert.Equal(["initial"], await SearchAsync(initial.Oid.ToHex()[..10]));
        Assert.Empty(await SearchAsync("no such commit"));
        Assert.True(history.NoMatches);

        // Up/Down move through the results only.
        var results = await SearchAsync("work");
        Assert.Equal(results[0], history.Move(1)?.Summary);
        Assert.Equal(results[1], history.Move(1)?.Summary);
        Assert.Equal(results[2], history.Move(1)?.Summary);

        history.ClearSearchCommand.Execute(null);
        Assert.False(history.IsSearching);
        Assert.Same(history.Rows, history.DisplayRows);
    }

    [Theory]
    [InlineData(0.5, "just now")]
    [InlineData(5, "5m ago")]
    [InlineData(120, "2h ago")]
    [InlineData(60 * 30, "yesterday")]
    [InlineData(60 * 24 * 3, "3 days ago")]
    [InlineData(60 * 24 * 45, "2026-09-16")]
    public void Relative_dates_match_the_old_app(double minutesAgo, string expected)
    {
        var now = new DateTimeOffset(2026, 10, 31, 12, 0, 0, TimeSpan.Zero);
        Assert.Equal(expected, CommitRow.RelativeDate(now.AddMinutes(-minutesAgo), now));
    }
}
