using Avalonia.Headless.XUnit;
using CheeseGit.App.Services;
using CheeseGit.App.ViewModels;
using CheeseGit.Core.Tests;

namespace CheeseGit.App.Tests;

/// <summary>#52: what happens to local changes when switching branches from the app.</summary>
public class SwitchChangesTests
{
    /// <summary>main and "other" at the same commit; main has an unstaged and an untracked change.</summary>
    private static TempRepo DirtyRepo()
    {
        var repo = TempRepo.Init();
        repo.Write("a.txt", "a\n");
        repo.CommitAll("initial");
        repo.Raw("branch", "other");
        repo.Write("a.txt", "changed\n");
        repo.Write("new.txt", "new\n");
        return repo;
    }

    private static async Task<(BranchListViewModel List, LocalBranchRow Row)> RowAsync(AppHarness app, string name)
    {
        var list = app.Tab.BranchList;
        list.IsOpen = true;
        await AppHarness.Until(() => list.IsLoaded && list.Items.OfType<LocalBranchRow>().Any(b => b.Name == name), $"{name} in the branch list");
        return (list, list.Items.OfType<LocalBranchRow>().Single(b => b.Name == name));
    }

    private static string Porcelain(TempRepo repo) => repo.Raw("status", "--porcelain", "--untracked-files=all");

    [AvaloniaFact]
    public async Task Leaving_changes_keeps_them_on_their_branch_until_switching_back()
    {
        using var repo = DirtyRepo();
        var main = repo.CurrentBranch();
        var dirty = Porcelain(repo);
        using var app = await AppHarness.OpenAsync(repo);
        await AppHarness.Until(() => app.UnstagedPaths.Count == 2, "the changes to show");

        var (_, other) = await RowAsync(app, "other");
        var switching = other.SwitchCommand.ExecuteAsync(null);
        var dialog = Assert.IsType<SwitchChangesDialogViewModel>(app.Main.Dialog);
        Assert.Equal($"You have changes on {main}", dialog.Title);
        Assert.Equal("Bring to other", dialog.BringLabel);
        dialog.LeaveCommand.Execute(null);
        await switching;

        Assert.Equal("other", repo.CurrentBranch());
        Assert.Equal("", Porcelain(repo));
        await AppHarness.Until(() => app.UnstagedPaths.Count == 0 && app.Tab.StashCount == 1, "the clean tree and the stash entry");

        // Back on main: a clean tree, so no question, and the changes return.
        var (_, back) = await RowAsync(app, main);
        await back.SwitchCommand.ExecuteAsync(null);
        Assert.Null(app.Main.Dialog);
        Assert.Equal(main, repo.CurrentBranch());
        Assert.Equal(dirty, Porcelain(repo));
        await AppHarness.Until(() => app.Tab.StashCount == 0, "the stash entry to go");
        Assert.Empty(app.Main.Alerts);
    }

    [AvaloniaFact]
    public async Task Remembered_bring_stops_asking_and_cancel_stays()
    {
        using var repo = DirtyRepo();
        var main = repo.CurrentBranch();
        var dirty = Porcelain(repo);
        using var app = await AppHarness.OpenAsync(repo);
        await AppHarness.Until(() => app.UnstagedPaths.Count == 2, "the changes to show");

        // Cancel: nothing happens.
        var (_, other) = await RowAsync(app, "other");
        var switching = other.SwitchCommand.ExecuteAsync(null);
        Assert.IsType<SwitchChangesDialogViewModel>(app.Main.Dialog).CancelCommand.Execute(null);
        await switching;
        Assert.Equal(main, repo.CurrentBranch());

        switching = other.SwitchCommand.ExecuteAsync(null);
        var dialog = Assert.IsType<SwitchChangesDialogViewModel>(app.Main.Dialog);
        dialog.Remember = true;
        dialog.BringCommand.Execute(null);
        await switching;
        Assert.Equal("other", repo.CurrentBranch());
        Assert.Equal(dirty, Porcelain(repo));
        Assert.Equal(SwitchChanges.Bring, app.Main.Settings.SwitchChanges);

        var (_, back) = await RowAsync(app, main);
        await back.SwitchCommand.ExecuteAsync(null);
        Assert.Null(app.Main.Dialog);
        Assert.Equal(main, repo.CurrentBranch());
    }

    /// <summary>
    /// A user moving a fix with "Bring" got git's "would be overwritten by checkout": main's change
    /// and other's commit touch the same file. The changes now go through the stash and are merged.
    /// </summary>
    [AvaloniaFact]
    public async Task Bring_merges_changes_git_refuses_to_carry_and_reports_conflicts()
    {
        using var repo = TempRepo.Init();
        repo.Write("a.txt", "1\n2\n3\n4\n5\n6\n7\n8\n");
        repo.CommitAll("initial");
        var main = repo.CurrentBranch();
        repo.Raw("switch", "-q", "-c", "other");
        repo.Write("a.txt", "1\n2\n3\n4\n5\n6\n7\n8 on other\n");
        repo.CommitAll("other");
        repo.Raw("switch", "-q", main);
        repo.Write("a.txt", "1 here\n2\n3\n4\n5\n6\n7\n8\n");
        using var app = await AppHarness.OpenAsync(repo);
        app.Main.Settings.SwitchChanges = SwitchChanges.Bring;
        await AppHarness.Until(() => app.UnstagedPaths.Count == 1, "the change to show");

        var (_, other) = await RowAsync(app, "other");
        await other.SwitchCommand.ExecuteAsync(null);
        Assert.Equal("other", repo.CurrentBranch());
        Assert.Equal("1 here\n2\n3\n4\n5\n6\n7\n8 on other\n", File.ReadAllText(Path.Combine(repo.Path, "a.txt")).Replace("\r", ""));
        await AppHarness.Until(() => app.Tab.StashCount == 0 && app.UnstagedPaths.Count == 1, "the merged change");
        Assert.Empty(app.Main.Alerts);

        // Back to main, which changes line 8 the other way: a conflict, said so, with the stash kept.
        repo.Raw("commit", "-q", "-am", "keep");
        repo.Raw("switch", "-q", main);
        repo.Write("a.txt", "1\n2\n3\n4\n5\n6\n7\n8 on main\n");
        await AppHarness.Until(() => app.Tab.BranchText == main && app.UnstagedPaths.Count == 1, "the new change on main");
        (_, other) = await RowAsync(app, "other");
        await other.SwitchCommand.ExecuteAsync(null);
        Assert.Equal("other", repo.CurrentBranch());
        Assert.Contains("UU a.txt", Porcelain(repo));
        Assert.Contains(app.Main.Alerts, a => !a.IsError && a.Message.StartsWith("Your changes conflict with other.", StringComparison.Ordinal));
        await AppHarness.Until(() => app.Tab.StashCount == 1, "the kept stash entry");
    }

    [AvaloniaFact]
    public async Task Creating_from_the_current_branch_takes_the_changes_without_asking()
    {
        using var repo = DirtyRepo();
        var dirty = Porcelain(repo);
        using var app = await AppHarness.OpenAsync(repo);
        await AppHarness.Until(() => app.UnstagedPaths.Count == 2, "the changes to show");
        var (list, _) = await RowAsync(app, "other");

        list.Query = "topic";
        await list.CreateCommand.ExecuteAsync(null);
        Assert.Null(app.Main.Dialog);
        Assert.Equal("topic", repo.CurrentBranch());
        Assert.Equal(dirty, Porcelain(repo));
    }
}
