using Avalonia.Headless.XUnit;
using CheeseGit.App.Controls;
using CheeseGit.App.ViewModels;
using CheeseGit.Core.Diff;
using CheeseGit.Core.Git;
using CheeseGit.Core.Repository;
using CheeseGit.Core.Tests;

namespace CheeseGit.App.Tests;

/// <summary>
/// Situations from daily use that the phase tests did not cover: diverged pulls, a rebase paused
/// on conflicts, conflicted files in the staging list, the commit form during a merge, opening a
/// folder inside a repository, and the alerts commits leave behind.
/// </summary>
public class DailyUseTests
{
    /// <summary>A repository with origin (bare) and a second clone that pushes a conflicting change to a.txt.</summary>
    private static (TempRepo Repo, TempRepo Bare, TempRepo Other) Diverged(bool conflicting)
    {
        var bare = TempRepo.InitBare();
        var repo = TempRepo.Init();
        repo.CommitFile("a.txt", "one\ntwo\nthree\n", "first");
        repo.Raw("remote", "add", "origin", bare.Path);
        repo.Raw("push", "-q", "-u", "origin", "HEAD");
        var other = TempRepo.CloneOf(bare);
        other.CommitFile("a.txt", "one\ntheirs\nthree\n", "theirs");
        other.Raw("push", "-q", "origin", "HEAD");
        if (conflicting) repo.CommitFile("a.txt", "one\nours\nthree\n", "ours");
        else repo.CommitFile("b.txt", "b\n", "ours");
        repo.Raw("fetch", "-q", "origin");
        return (repo, bare, other);
    }

    private static async Task<RemoteViewModel> PullButtonAsync(AppHarness app)
    {
        var remote = app.Tab.Remote;
        await AppHarness.Until(() => remote.Action == RemoteAction.Pull, $"the remote button to offer a pull (it is {remote.Action})");
        return remote;
    }

    [AvaloniaFact]
    public async Task Copying_an_error_shows_that_it_was_copied_then_goes_back()
    {
        var main = AppHarness.CreateMain();
        main.ShowError("fatal: something went wrong");
        var alert = Assert.Single(main.Alerts);
        Assert.False(alert.Copied);

        await alert.CopyCommand.ExecuteAsync(null);
        Assert.True(alert.Copied);
        Assert.False(alert.NotCopied);
        await AppHarness.Until(() => !alert.Copied, "the copied mark to go", timeoutMs: 5000);
        Assert.True(alert.NotCopied);
    }

    [AvaloniaFact]
    public async Task Clicking_the_path_above_the_diff_copies_the_whole_path()
    {
        using var repo = TempRepo.Init();
        repo.CommitFile("deep/nested/dir/File.cs", "a\n", "first");
        repo.Write("deep/nested/dir/File.cs", "b\n");
        using var app = await AppHarness.OpenAsync(repo);
        var platform = new FakePlatform();
        app.Main.Platform = platform;
        await app.ShowAsync(app.Unstaged("deep/nested/dir/File.cs"));

        await app.Diff.CopyPathCommand.ExecuteAsync(null);
        Assert.Equal("deep/nested/dir/File.cs", platform.Clipboard);
        Assert.True(app.Diff.PathCopied);
        await AppHarness.Until(() => !app.Diff.PathCopied, "the copied mark to go", timeoutMs: 5000);
    }

    [AvaloniaFact]
    public async Task Pulling_diverged_branches_merges_without_pull_config()
    {
        var (repo, bare, other) = Diverged(conflicting: false);
        using var _ = repo;
        using var __ = bare;
        using var ___ = other;
        using var app = await AppHarness.OpenAsync(repo);

        // git 2.27+ refuses this pull without pull.rebase or pull.ff ("Need to specify how to reconcile").
        await (await PullButtonAsync(app)).RunCommand.ExecuteAsync(null);

        Assert.DoesNotContain(app.Main.Alerts, a => a.IsError);
        repo.Raw("merge-base", "--is-ancestor", other.Head(), "HEAD");
        Assert.Equal("b\n", repo.ReadNormalized("b.txt"));
    }

    [AvaloniaFact]
    public async Task A_push_the_remote_rejected_offers_a_pull()
    {
        var bare = TempRepo.InitBare();
        var repo = TempRepo.Init();
        using var _ = repo;
        using var __ = bare;
        repo.CommitFile("a.txt", "a\n", "first");
        repo.Raw("remote", "add", "origin", bare.Path);
        repo.Raw("push", "-q", "-u", "origin", "HEAD");
        using var other = TempRepo.CloneOf(bare);
        other.CommitFile("theirs.txt", "theirs\n", "theirs");
        other.Raw("push", "-q", "origin", "HEAD");
        repo.CommitFile("ours.txt", "ours\n", "ours"); // Not fetched: the app thinks it is 1 ahead.
        using var app = await AppHarness.OpenAsync(repo);
        var remote = app.Tab.Remote;
        await AppHarness.Until(() => remote.Action == RemoteAction.Push, $"the remote button to offer a push (it is {remote.Action})");

        await remote.RunCommand.ExecuteAsync(null);

        Assert.DoesNotContain(app.Main.Alerts, a => a.IsError);
        var alert = Assert.Single(app.Main.Alerts, a => a.Message.StartsWith("Not pushed: origin has 1 commit", StringComparison.Ordinal));
        Assert.Equal("Pull", alert.ActionLabel);
        await AppHarness.Until(() => remote.Action == RemoteAction.Pull, "the fetched commit to show as behind");

        await alert.RunActionCommand.ExecuteAsync(null);
        await AppHarness.Until(() => remote.Action == RemoteAction.Push, $"the merged branch to offer a push (it is {remote.Action})");
        await remote.RunCommand.ExecuteAsync(null);

        Assert.DoesNotContain(app.Main.Alerts, a => a.IsError);
        Assert.Equal(repo.Head(), bare.Raw("rev-parse", "HEAD").Trim());
    }

    [AvaloniaFact]
    public async Task A_pull_that_conflicts_opens_the_conflict_dialog()
    {
        var (repo, bare, other) = Diverged(conflicting: true);
        using var _ = repo;
        using var __ = bare;
        using var ___ = other;
        using var app = await AppHarness.OpenAsync(repo);

        // The button's command finishes when the dialog closes.
        var pulling = (await PullButtonAsync(app)).RunCommand.ExecuteAsync(null);

        await AppHarness.Until(() => app.Main.Dialog is ConflictDialogViewModel { Loaded: true }, "the conflict dialog");
        var dialog = (ConflictDialogViewModel)app.Main.Dialog!;
        Assert.Equal("a.txt", Assert.Single(dialog.Files).DisplayPath);
        Assert.DoesNotContain(app.Main.Alerts, a => a.IsError);
        await dialog.AbortCommand.ExecuteAsync(null);
        await AppHarness.Until(() => pulling.IsCompleted && !app.Tab.HasOperation, "the merge to be aborted");
    }

    [AvaloniaFact]
    public async Task A_rebase_paused_on_conflicts_can_be_continued_from_the_banner()
    {
        var (repo, bare, other) = Diverged(conflicting: true);
        using var _ = repo;
        using var __ = bare;
        using var ___ = other;
        var branch = repo.CurrentBranch();
        repo.Raw("config", "pull.rebase", "true");
        using var app = await AppHarness.OpenAsync(repo);

        await (await PullButtonAsync(app)).RunCommand.ExecuteAsync(null);
        await AppHarness.Until(() => app.Tab.IsRebasing, "the rebase to pause");
        Assert.Equal(branch, app.Tab.BranchText);
        Assert.Equal("(rebasing)", app.Tab.HeadDetail);
        Assert.Equal("Rebase in progress", app.Staging.CommitButtonText);
        Assert.False(app.Staging.CommitCommand.CanExecute(null));

        // Continuing with the conflict open says what to do and changes nothing.
        app.Main.Alerts.Clear();
        await app.Tab.ContinueRebaseCommand.ExecuteAsync(null);
        Assert.Contains(app.Main.Alerts, a => a.IsError && a.Message.Contains("conflicted file needs resolving"));
        Assert.True(app.Tab.IsRebasing);
        Assert.True(app.Tab.History.BrowsingHistory);
        Assert.False(app.Tab.History.ShowJumpToPresent);

        repo.Write("a.txt", "one\nours and theirs\nthree\n");
        repo.Raw("add", "a.txt");
        await AppHarness.Until(() => app.Tab.Snapshot?.Operation.ConflictCount == 0, "the conflict to be staged");
        await app.Tab.ContinueRebaseCommand.ExecuteAsync(null);

        await AppHarness.Until(() => !app.Tab.IsRebasing && !app.Tab.BrowsingHistory, "the rebase to finish");
        Assert.Equal(branch, repo.CurrentBranch());
        Assert.Equal(other.Head(), repo.Raw("rev-parse", "HEAD~1").Trim());
        Assert.Equal("one\nours and theirs\nthree\n", repo.ReadNormalized("a.txt"));
    }

    [AvaloniaFact]
    public async Task A_rebase_whose_resolution_leaves_nothing_skips_the_commit()
    {
        var (repo, bare, other) = Diverged(conflicting: true);
        using var _ = repo;
        using var __ = bare;
        using var ___ = other;
        repo.Raw("config", "pull.rebase", "true");
        using var app = await AppHarness.OpenAsync(repo);
        await (await PullButtonAsync(app)).RunCommand.ExecuteAsync(null);
        await AppHarness.Until(() => app.Tab.IsRebasing, "the rebase to pause");

        // Keep their version: the rebased commit has nothing left.
        repo.Raw("checkout", "--theirs", "--", "a.txt");
        repo.Raw("checkout", "HEAD", "--", "a.txt");
        await AppHarness.Until(() => app.Tab.Snapshot?.Operation.ConflictCount == 0, "the conflict to be resolved");
        await app.Tab.ContinueRebaseCommand.ExecuteAsync(null);

        await AppHarness.Until(() => !app.Tab.IsRebasing, "the rebase to finish");
        Assert.Equal(other.Head(), repo.Head());
    }

    [AvaloniaFact]
    public async Task Aborting_a_rebase_asks_and_restores_the_branch()
    {
        var (repo, bare, other) = Diverged(conflicting: true);
        using var _ = repo;
        using var __ = bare;
        using var ___ = other;
        var before = repo.Head();
        repo.Raw("config", "pull.rebase", "true");
        using var app = await AppHarness.OpenAsync(repo);
        await (await PullButtonAsync(app)).RunCommand.ExecuteAsync(null);
        await AppHarness.Until(() => app.Tab.IsRebasing, "the rebase to pause");

        var aborting = app.Tab.AbortRebaseCommand.ExecuteAsync(null);
        var confirm = Assert.IsType<ConfirmDialogViewModel>(app.Main.Dialog);
        confirm.ConfirmCommand.Execute(null);
        await aborting;

        await AppHarness.Until(() => !app.Tab.IsRebasing, "the rebase to be aborted");
        Assert.Equal(before, repo.Head());
    }

    /// <summary>main and feature change the same line of a.txt; a merge of feature is paused on it, with b.txt staged.</summary>
    private static TempRepo PausedMerge()
    {
        var repo = TempRepo.Init();
        repo.CommitFile("a.txt", "one\ntwo\nthree\n", "first");
        repo.Raw("switch", "-q", "-c", "feature");
        repo.CommitFile("a.txt", "one\nfeature\nthree\n", "feature");
        repo.Raw("switch", "-q", "-");
        repo.CommitFile("a.txt", "one\nmain\nthree\n", "main");
        try
        {
            repo.Raw("merge", "feature");
        }
        catch (Exception)
        {
            // Exits 1 on conflicts.
        }
        repo.Write("b.txt", "b\n");
        repo.Raw("add", "b.txt");
        return repo;
    }

    [AvaloniaFact]
    public async Task A_conflicted_file_shows_its_markers_read_only()
    {
        using var repo = PausedMerge();
        using var app = await AppHarness.OpenAsync(repo);

        await app.ShowAsync(app.Unstaged("a.txt"));

        Assert.True(app.Diff.IsConflicted);
        Assert.False(app.Diff.CanEditLines);
        Assert.Null(app.Diff.Message);
        var added = app.Diff.Diff!.Hunks.SelectMany(h => h.Lines).Where(l => l.Kind == DiffLineKind.Addition).Select(l => l.Text).ToList();
        Assert.Contains(added, l => l.StartsWith("<<<<<<<", StringComparison.Ordinal));
        Assert.Contains("feature", added);
    }

    [AvaloniaFact]
    public async Task The_commit_form_does_not_finish_a_paused_merge()
    {
        using var repo = PausedMerge();
        using var app = await AppHarness.OpenAsync(repo);
        await AppHarness.Until(() => app.Tab.HasOperation, "the paused merge");

        app.Staging.Summary = "Would finish the merge with this message";
        Assert.False(app.Staging.CanCommit);
        Assert.False(app.Staging.CommitCommand.CanExecute(null));
        Assert.False(app.Staging.ShowMoreActions);
        Assert.Equal("Merge in progress", app.Staging.CommitButtonText);
        Assert.Contains("Resolve conflicts", app.Staging.CommitToolTip);
    }

    [AvaloniaFact]
    public async Task Staging_a_file_with_conflict_markers_asks_first()
    {
        using var repo = PausedMerge();
        using var app = await AppHarness.OpenAsync(repo);
        var row = app.Unstaged("a.txt");

        var staging = app.Staging.Unstaged.RowActionCommand.ExecuteAsync(row);
        await AppHarness.Until(() => app.Main.Dialog is ConfirmDialogViewModel, "the question");
        var confirm = (ConfirmDialogViewModel)app.Main.Dialog!;
        Assert.Contains("conflict markers", confirm.Message);
        confirm.CancelCommand.Execute(null);
        await staging;
        Assert.Contains("UU a.txt", repo.Raw("status", "--porcelain"));

        // Resolved in an editor: no question.
        repo.Write("a.txt", "one\nmain and feature\nthree\n");
        await app.Staging.Unstaged.RowActionCommand.ExecuteAsync(app.Unstaged("a.txt"));
        Assert.Null(app.Main.Dialog);
        await AppHarness.Until(() => app.StagedPaths.Contains("a.txt"), "a.txt to be staged");
    }

    [AvaloniaFact]
    public async Task Opening_a_folder_inside_a_repository_opens_the_repository()
    {
        using var repo = TempRepo.WithCommit();
        repo.CommitFile("sub/dir/file.txt", "x\n", "nested");
        using var main = AppHarness.CreateMain();

        await main.OpenRepositoryAsync(repo.FullPath("sub/dir"));
        var tab = Assert.Single(main.Tabs);
        Assert.Equal(Path.GetFullPath(repo.Path).TrimEnd(Path.DirectorySeparatorChar), tab.Path);
        Assert.Equal(Path.GetFileName(repo.Path.TrimEnd(Path.DirectorySeparatorChar)), tab.Name);
        Assert.Equal([tab.Path], main.Settings.OpenRepositories);

        // Another folder of the same repository selects the tab instead of opening a second one.
        await main.OpenRepositoryAsync(repo.FullPath("sub"));
        Assert.Same(tab, Assert.Single(main.Tabs));
        Assert.Same(tab, main.SelectedTab);
    }

    [AvaloniaFact]
    public async Task Commit_alerts_replace_each_other_and_undo_only_the_latest_commit()
    {
        using var repo = TempRepo.WithCommit();
        using var app = await AppHarness.OpenAsync(repo);

        async Task CommitAsync(string file)
        {
            repo.Write(file, file);
            await AppHarness.Until(() => app.UnstagedPaths.Contains(file), $"{file} to show");
            await app.Staging.Unstaged.BulkActionCommand.ExecuteAsync(null);
            await AppHarness.Until(() => app.StagedPaths.Contains(file), $"{file} to be staged");
            app.Staging.Summary = "Add " + file;
            await app.Staging.CommitCommand.ExecuteAsync(null);
        }

        await CommitAsync("one.txt");
        await CommitAsync("two.txt");
        var alert = Assert.Single(app.Main.Alerts);
        Assert.Contains("Add two.txt", alert.Message);

        // A commit made elsewhere since: the alert's Undo would move the branch past it.
        repo.CommitFile("three.txt", "3\n", "from a terminal");
        var head = repo.Head();
        await AppHarness.Until(() => app.Tab.Snapshot?.Head.Commit?.ToHex() == head, "the terminal commit");
        await alert.RunActionCommand.ExecuteAsync(null);
        Assert.Equal(head, repo.Head());
        Assert.Contains(app.Main.Alerts, a => a.IsError && a.Message.Contains("no longer the latest commit"));
    }

    [AvaloniaFact]
    public async Task Deleting_a_branch_warns_about_commits_on_no_other_branch()
    {
        using var repo = TempRepo.WithCommit();
        var main = repo.CurrentBranch();
        repo.Raw("branch", "merged");
        repo.Raw("switch", "-q", "-c", "lonely");
        repo.CommitFile("x.txt", "x\n", "only here");
        repo.CommitFile("y.txt", "y\n", "and here");
        repo.Raw("switch", "-q", main);
        using var app = await AppHarness.OpenAsync(repo);
        await app.Tab.BranchList.LoadAsync();

        async Task<DeleteBranchDialogViewModel> DialogAsync(string name)
        {
            var row = app.Tab.BranchList.Items.OfType<LocalBranchRow>().Single(b => b.Name == name);
            _ = row.DeleteCommand.ExecuteAsync(null);
            var dialog = Assert.IsType<DeleteBranchDialogViewModel>(app.Main.Dialog);
            await AppHarness.Until(() => !dialog.CheckingRemote, "the checks");
            return dialog;
        }

        var lonely = await DialogAsync("lonely");
        Assert.Equal(2, lonely.UniqueCommits);
        Assert.Contains("2 commits", lonely.UniqueCommitsWarning);
        lonely.CancelCommand.Execute(null);

        var merged = await DialogAsync("merged");
        Assert.False(merged.HasUniqueCommits);
        merged.CancelCommand.Execute(null);
    }

    [AvaloniaFact]
    public async Task No_empty_commit_link_while_browsing_history()
    {
        using var repo = TempRepo.WithCommits(2);
        repo.Raw("checkout", "-q", "--detach", "HEAD~1");
        using var app = await AppHarness.OpenAsync(repo);
        await AppHarness.Until(() => app.Tab.BrowsingHistory, "history browsing");
        Assert.True(app.Staging.IsClean);
        Assert.False(app.Staging.ShowEmptyCommitLink);
    }

    [Fact]
    public void Changed_lines_that_differ_in_line_endings_are_labelled()
    {
        static DiffLine Line(DiffLineKind kind, string text, int old, int @new) => new(kind, System.Text.Encoding.UTF8.GetBytes(text), old, @new);
        var converted = new FileDiff(GitPath.FromString("a.txt"), GitPath.FromString("a.txt"), DiffChange.Modified, false, null, null,
        [
            new DiffHunk(1, 1, 1, 1, "", [Line(DiffLineKind.Deletion, "one", 1, 0), Line(DiffLineKind.Addition, "one\r", 0, 1)]),
        ]);
        var rows = DiffRows.Unified(converted);
        Assert.Equal(["LF", "CRLF"], rows.Skip(1).Select(r => r.Left.LineEnding));
        Assert.Equal("one", rows[2].Left.Text);

        // All CRLF: nothing to point out.
        var crlf = converted with
        {
            Hunks = [new DiffHunk(1, 1, 1, 1, "", [Line(DiffLineKind.Deletion, "one\r", 1, 0), Line(DiffLineKind.Addition, "two\r", 0, 1)])],
        };
        Assert.All(DiffRows.Split(crlf), r => Assert.Null(r.Left.LineEnding));
    }
}
