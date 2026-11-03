using Avalonia.Headless.XUnit;
using CheeseGit.App.ViewModels;
using CheeseGit.Core.Tests;

namespace CheeseGit.App.Tests;

/// <summary>Phase 4: the branch dropdown (find, switch, create with a base, delete) over real repositories.</summary>
public class BranchListTests
{
    /// <summary>
    /// main (3 commits, pushed), feature at main, old at main~1; origin also has
    /// remote-only "experiment" and "release/1.0".
    /// </summary>
    private static (TempRepo Repo, TempRepo Bare) RepoWithRemote()
    {
        var bare = TempRepo.InitBare();
        var repo = TempRepo.Init();
        repo.CommitFile("a.txt", "a\n", "first");
        repo.CommitFile("b.txt", "b\n", "second");
        repo.CommitFile("c.txt", "c\n", "third");
        repo.Raw("remote", "add", "origin", bare.Path);
        repo.Raw("push", "-q", "-u", "origin", "HEAD");
        repo.Raw("branch", "feature");
        repo.Raw("branch", "old", "HEAD~1");
        repo.Raw("push", "-q", "origin", "HEAD~2:refs/heads/release/1.0", "HEAD:refs/heads/experiment");
        repo.Raw("fetch", "-q", "origin");
        return (repo, bare);
    }

    private static async Task<BranchListViewModel> OpenAsync(AppHarness app)
    {
        var list = app.Tab.BranchList;
        list.IsOpen = true;
        await AppHarness.Until(() => list.IsLoaded, "the branch list to load");
        return list;
    }

    private static List<string> Names(BranchListViewModel list) => list.Items.Select(i => i switch
    {
        LocalBranchRow l => (l.IsCurrent ? "* " : "") + l.Name,
        RemoteBranchRow r => $"{r.Remote}/{r.Name}",
        BranchListHeader h => "-- " + h.Text,
        _ => "?",
    }).ToList();

    [AvaloniaFact]
    public async Task Lists_local_branches_then_remote_only_ones_and_filters()
    {
        var (repo, bare) = RepoWithRemote();
        using var _ = repo;
        using var __ = bare;
        using var app = await AppHarness.OpenAsync(repo);
        var main = repo.CurrentBranch();
        var list = await OpenAsync(app);

        var names = Names(list);
        Assert.Equal(["* " + main, "feature", "old"], names.TakeWhile(n => !n.StartsWith("--")).Order(StringComparer.Ordinal));
        Assert.Equal(["-- Remote", "origin/experiment", "origin/release/1.0"], names.SkipWhile(n => !n.StartsWith("--")).Order(StringComparer.Ordinal).ToList());

        list.Query = "REL";
        Assert.Equal(["-- Remote", "origin/release/1.0"], Names(list));
        Assert.True(list.CanCreate);

        // An existing name (any case, local or remote) is not offered for creating.
        list.Query = "Feature";
        Assert.False(list.CanCreate);
        list.Query = "experiment";
        Assert.False(list.CanCreate);

        list.Query = "nothing-like-this";
        Assert.Empty(list.Items);
        Assert.True(list.NoMatches);
        Assert.True(list.CanCreate);
    }

    /// <summary>
    /// Typing letter by letter keeps the create button offered, so the label must follow the query:
    /// it was only raised with CanCreate and stayed at the first letter ("Create branch “l”").
    /// </summary>
    [AvaloniaFact]
    public async Task Create_label_follows_the_query_while_typing()
    {
        using var repo = TempRepo.Init();
        repo.CommitFile("a.txt", "a\n", "first");
        using var app = await AppHarness.OpenAsync(repo);
        var list = await OpenAsync(app);
        var raised = 0;
        list.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(BranchListViewModel.CreateLabel)) raised++;
        };

        foreach (var text in new[] { "l", "le", "lev", "level-prototype" }) list.Query = text;

        Assert.True(list.CanCreate);
        Assert.Equal("Create branch “level-prototype”", list.CreateLabel);
        Assert.Equal(4, raised);
    }

    [AvaloniaFact]
    public async Task Creates_a_branch_from_the_chosen_base()
    {
        var (repo, bare) = RepoWithRemote();
        using var _ = repo;
        using var __ = bare;
        using var app = await AppHarness.OpenAsync(repo);
        var list = await OpenAsync(app);
        var closed = 0;
        list.CloseRequested += () => closed++;

        list.Query = "topic";
        Assert.Equal(repo.CurrentBranch() + " (current)", list.Base!.Label);
        Assert.Contains(list.Bases, b => b.Label == "origin/release/1.0");
        Assert.DoesNotContain(list.Bases, b => b.Label.EndsWith("/HEAD", StringComparison.Ordinal));
        list.Base = list.Bases.Single(b => b.Label == "old");
        await list.CreateCommand.ExecuteAsync(null);

        Assert.Equal(1, closed);
        await AppHarness.Until(() => app.Tab.BranchText == "topic", "the switch to topic");
        Assert.Equal(repo.Raw("rev-parse", "old").Trim(), repo.Head());
        // Created from another branch, not tracking it.
        Assert.Equal("", repo.Raw("for-each-ref", "--format=%(upstream)", "refs/heads/topic").Trim());
    }

    [AvaloniaFact]
    public async Task Enter_switches_to_an_exact_match_and_remote_rows_create_a_tracking_branch()
    {
        var (repo, bare) = RepoWithRemote();
        using var _ = repo;
        using var __ = bare;
        using var app = await AppHarness.OpenAsync(repo);
        var list = await OpenAsync(app);

        list.Query = "OLD";
        await list.SubmitCommand.ExecuteAsync(null);
        await AppHarness.Until(() => app.Tab.BranchText == "old", "the switch to old");

        await AppHarness.Until(() => list.Items.OfType<LocalBranchRow>().Any(b => b is { Name: "old", IsCurrent: true }), "the list to follow the switch");
        list.Query = "";
        var remote = list.Items.OfType<RemoteBranchRow>().Single(r => r.Name == "release/1.0");
        await remote.SwitchCommand.ExecuteAsync(null);
        await AppHarness.Until(() => app.Tab.BranchText == "release/1.0", "the checkout of release/1.0");
        Assert.Equal("refs/remotes/origin/release/1.0", repo.Raw("for-each-ref", "--format=%(upstream)", "refs/heads/release/1.0").Trim());
        await AppHarness.Until(() => !list.Items.OfType<RemoteBranchRow>().Any(r => r.Name == "release/1.0"), "the remote row to become a local one");
    }

    [AvaloniaFact]
    public async Task Deleting_offers_the_remote_branch_and_deletes_both()
    {
        var (repo, bare) = RepoWithRemote();
        using var _ = repo;
        using var __ = bare;
        repo.Raw("push", "-q", "-u", "origin", "feature");
        using var app = await AppHarness.OpenAsync(repo);
        var list = await OpenAsync(app);

        var row = list.Items.OfType<LocalBranchRow>().Single(b => b.Name == "feature");
        var deleting = row.DeleteCommand.ExecuteAsync(null);
        var dialog = Assert.IsType<DeleteBranchDialogViewModel>(app.Main.Dialog);
        await AppHarness.Until(() => !dialog.CheckingRemote, "the remote check");
        Assert.True(dialog.CanDeleteRemote);
        Assert.Equal("Also delete on remote (origin)", dialog.DeleteRemoteLabel);
        dialog.DeleteRemote = true;
        await dialog.DeleteCommand.ExecuteAsync(null);
        await deleting;

        Assert.Null(app.Main.Dialog);
        Assert.Equal("", repo.Raw("for-each-ref", "refs/heads/feature").Trim());
        Assert.Equal("", bare.Raw("for-each-ref", "refs/heads/feature").Trim());
        Assert.Empty(app.Main.Alerts);
    }

    [AvaloniaFact]
    public async Task A_branch_only_here_is_deleted_without_a_remote_choice_and_the_current_one_cannot_be()
    {
        var (repo, bare) = RepoWithRemote();
        using var _ = repo;
        using var __ = bare;
        using var app = await AppHarness.OpenAsync(repo);
        var list = await OpenAsync(app);

        var current = list.Items.OfType<LocalBranchRow>().Single(b => b.IsCurrent);
        await current.DeleteCommand.ExecuteAsync(null);
        Assert.Null(app.Main.Dialog);

        var row = list.Items.OfType<LocalBranchRow>().Single(b => b.Name == "old");
        var deleting = row.DeleteCommand.ExecuteAsync(null);
        var dialog = Assert.IsType<DeleteBranchDialogViewModel>(app.Main.Dialog);
        await AppHarness.Until(() => !dialog.CheckingRemote, "the remote check");
        Assert.False(dialog.CanDeleteRemote);
        dialog.CancelCommand.Execute(null);
        await deleting;
        Assert.NotEqual("", repo.Raw("for-each-ref", "refs/heads/old").Trim());
    }

    [AvaloniaFact]
    public async Task Rows_carry_the_graphs_eye_toggles()
    {
        var (repo, bare) = RepoWithRemote();
        using var _ = repo;
        using var __ = bare;
        repo.Raw("switch", "-q", "old");
        repo.CommitFile("d.txt", "d\n", "diverged");
        repo.Raw("switch", "-q", "-");
        using var app = await AppHarness.OpenAsync(repo);
        app.Tab.History.EnsureLoaded();
        await AppHarness.Until(() => app.Tab.History.Branches.Count > 0, "the graph's branches");
        var list = await OpenAsync(app);

        var old = list.Items.OfType<LocalBranchRow>().Single(b => b.Name == "old");
        Assert.True(old.HasGraph);
        Assert.False(old.Graph!.IsVisible);
        old.Graph.ToggleCommand.Execute(null);
        await AppHarness.Until(() => app.Tab.History.View!.VisibleBranches.Contains("refs/heads/old"), "old to be shown");
        // The layout made new toggles; the rows follow them.
        await AppHarness.Until(() => list.Items.OfType<LocalBranchRow>().Single(b => b.Name == "old").Graph is { IsVisible: true }, "the row's toggle to update");
        // Remote-only branches are not in the graph (only the remote's default branch is): no toggle.
        Assert.All(list.Items.OfType<RemoteBranchRow>(), r => Assert.False(r.HasGraph));
    }
}
