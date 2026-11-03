using System.Text;
using Avalonia.Headless.XUnit;
using CheeseGit.App.ViewModels;
using CheeseGit.Core.Diff;
using CheeseGit.Core.Tests;

namespace CheeseGit.App.Tests;

/// <summary>The daily loop of Phase 2's exit criterion: stage, commit, undo, discard.</summary>
public class StagingTests
{
    private static TempRepo RepoWithChanges()
    {
        var repo = TempRepo.Init();
        repo.Write("a.txt", string.Concat(Enumerable.Range(1, 30).Select(i => $"line {i}\n")));
        repo.Write("b.txt", "b\n");
        repo.CommitAll("initial");
        repo.Write("a.txt", repo.ReadNormalized("a.txt").Replace("line 3\n", "line three\n").Replace("line 25\n", "line twenty-five\n"));
        repo.Write("new.txt", "new\n");
        repo.Delete("b.txt");
        return repo;
    }

    [AvaloniaFact]
    public async Task Opening_lists_the_changes_and_suggests_nothing_until_something_is_staged()
    {
        using var repo = RepoWithChanges();
        using var app = await AppHarness.OpenAsync(repo);

        Assert.Equal(["a.txt", "b.txt", "new.txt"], app.UnstagedPaths.Order());
        Assert.Empty(app.StagedPaths);
        Assert.False(app.Staging.CommitCommand.CanExecute(null));
    }

    [AvaloniaFact]
    public async Task Staging_and_unstaging_a_file_moves_it_between_the_lists()
    {
        using var repo = RepoWithChanges();
        using var app = await AppHarness.OpenAsync(repo);

        await app.Staging.RowActionAsync(app.Unstaged("new.txt"));
        await AppHarness.Until(() => app.StagedPaths.Contains("new.txt"), "new.txt to be staged");
        Assert.DoesNotContain("new.txt", app.UnstagedPaths);
        Assert.Equal("Add new.txt", app.Staging.DefaultSummary);

        await app.Staging.RowActionAsync(app.StagedRow("new.txt"));
        await AppHarness.Until(() => app.UnstagedPaths.Contains("new.txt"), "new.txt to be unstaged");
        Assert.Empty(app.StagedPaths);
    }

    [AvaloniaFact]
    public async Task Commit_uses_the_summary_and_undo_brings_the_changes_and_message_back()
    {
        using var repo = RepoWithChanges();
        var before = repo.Head();
        using var app = await AppHarness.OpenAsync(repo);
        await app.Staging.BulkActionAsync(app.Staging.Unstaged);
        await AppHarness.Until(() => app.StagedPaths.Count == 3, "everything to be staged");

        app.Staging.Summary = "Rework the files";
        app.Staging.Description = "Longer explanation.";
        Assert.True(app.Staging.CommitCommand.CanExecute(null));
        await app.Staging.CommitCommand.ExecuteAsync(null);

        Assert.Equal("Rework the files\n\nLonger explanation.", repo.Raw("log", "-1", "--format=%B").TrimEnd());
        await AppHarness.Until(() => app.Staging.IsClean, "the working tree to be clean");
        Assert.Equal("", app.Staging.Summary);
        var alert = Assert.Single(app.Main.Alerts);
        Assert.False(alert.IsError);
        Assert.Equal("Undo", alert.ActionLabel);

        await alert.RunActionCommand.ExecuteAsync(null);
        Assert.Equal(before, repo.Head());
        await AppHarness.Until(() => app.StagedPaths.Count == 3, "the undone changes to be staged again");
        Assert.Equal("Rework the files", app.Staging.Summary);
        Assert.Equal("Longer explanation.", app.Staging.Description);
    }

    [AvaloniaFact]
    public async Task An_empty_summary_commits_with_the_suggested_one()
    {
        using var repo = RepoWithChanges();
        using var app = await AppHarness.OpenAsync(repo);
        await app.Staging.RowActionAsync(app.Unstaged("b.txt"));
        await AppHarness.Until(() => app.Staging.DefaultSummary == "Delete b.txt", "the suggested summary");

        await app.Staging.CommitCommand.ExecuteAsync(null);

        Assert.Equal("Delete b.txt", repo.Raw("log", "-1", "--format=%s").Trim());
    }

    [AvaloniaFact]
    public async Task Staging_one_line_stages_only_that_line()
    {
        using var repo = RepoWithChanges();
        using var app = await AppHarness.OpenAsync(repo);
        await app.ShowAsync(app.Unstaged("a.txt"));
        var diff = app.Diff.Diff!;
        Assert.Equal(2, diff.Hunks.Count);
        var added = diff.Hunks[0].Lines.Select((l, i) => (l, i)).Single(x => x.l.Kind == DiffLineKind.Addition).i;

        await app.Diff.ApplyLinesAsync([new LineSelection(0, added)], discard: false);

        var staged = repo.Raw("diff", "--no-ext-diff", "--no-color", "--cached", "--", "a.txt");
        Assert.Contains("+line three", staged);
        Assert.DoesNotContain("-line 3\n", staged); // The deletion stays unstaged: only the addition was picked.
        Assert.DoesNotContain("twenty-five", staged);
        await AppHarness.Until(() => app.StagedPaths.Contains("a.txt") && app.UnstagedPaths.Contains("a.txt"), "a.txt to be partly staged");
    }

    [AvaloniaFact]
    public async Task Discarding_asks_first_and_cancel_keeps_the_change()
    {
        using var repo = RepoWithChanges();
        var modified = repo.ReadNormalized("a.txt");
        using var app = await AppHarness.OpenAsync(repo);
        app.Tab.ShiftHeld = true; // Shift turns the row action into discard.

        var cancelled = app.Staging.RowActionAsync(app.Unstaged("a.txt"));
        await AppHarness.Until(() => app.Main.Dialog is ConfirmDialogViewModel, "the confirmation");
        app.Main.Dialog!.CancelCommand.Execute(null);
        await cancelled;
        Assert.Equal(modified, repo.ReadNormalized("a.txt"));

        var confirmed = app.Staging.RowActionAsync(app.Unstaged("a.txt"));
        await AppHarness.Until(() => app.Main.Dialog is ConfirmDialogViewModel, "the confirmation");
        ((ConfirmDialogViewModel)app.Main.Dialog!).ConfirmCommand.Execute(null);
        await confirmed;
        Assert.Contains("line 3\n", repo.ReadNormalized("a.txt"));
        await AppHarness.Until(() => !app.UnstagedPaths.Contains("a.txt"), "a.txt to leave the list");
    }

    [AvaloniaFact]
    public async Task A_failing_action_shows_the_error_and_marks_the_operation_in_the_command_log()
    {
        using var repo = RepoWithChanges();
        using var app = await AppHarness.OpenAsync(repo);
        // Another git process holding the index lock is the classic real-world failure.
        File.WriteAllBytes(Path.Combine(repo.Path, ".git", "index.lock"), []);

        await app.Staging.RowActionAsync(app.Unstaged("new.txt"));

        var alert = Assert.Single(app.Main.Alerts);
        Assert.True(alert.IsError);
        Assert.Contains("index.lock", alert.Message);
        await AppHarness.Until(() => app.Main.CommandLog.ErrorCount == 1, "the command log to count the failure");
        var entry = app.Main.CommandLog.Entries.First(e => e.Failed);
        Assert.Equal("Stage new.txt", entry.Label);
        Assert.False(entry.Background);
        Assert.Contains("index.lock", entry.Error);
    }

    [AvaloniaFact]
    public async Task Background_refreshes_fold_and_do_not_count_as_errors()
    {
        using var repo = RepoWithChanges();
        using var app = await AppHarness.OpenAsync(repo);
        for (var i = 0; i < 3; i++) await app.Tab.Session!.RefreshAsync(Core.Repository.SnapshotParts.All);
        await AppHarness.Until(() => app.Main.CommandLog.Entries.Any(e => e.Label == "Refresh" && e.Count >= 3), "the refreshes to fold");

        Assert.Equal(0, app.Main.CommandLog.ErrorCount);
        Assert.Single(app.Main.CommandLog.Entries, e => e.Label == "Refresh");
    }
}
