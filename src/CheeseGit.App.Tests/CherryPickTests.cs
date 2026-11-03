using Avalonia.Headless.XUnit;
using CheeseGit.App.ViewModels;
using CheeseGit.Core.Git;
using CheeseGit.Core.Repository;
using CheeseGit.Core.Tests;

namespace CheeseGit.App.Tests;

/// <summary>
/// Cherry-picking from the history: multi-selection, the target picker and the conflict dialog for
/// a paused sequence. Ports the old app's e2e spec 34, plus what it could not check.
/// </summary>
public class CherryPickTests
{
    /// <summary>
    /// The default branch has "initial", then a.txt, b.txt (conflicts with "target") and c.txt on top;
    /// "target" branches off "initial" and edits b.txt. HEAD is on the default branch.
    /// </summary>
    private static TempRepo Repo()
    {
        var repo = TempRepo.Init();
        repo.CommitFile("b.txt", "base\n", "initial");
        repo.Raw("branch", "target");
        repo.CommitFile("a.txt", "a\n", "add a");
        repo.CommitFile("b.txt", "source version\n", "edit b");
        repo.CommitFile("c.txt", "c\n", "add c");
        repo.Raw("switch", "-q", "target");
        repo.CommitFile("b.txt", "target version\n", "target edit");
        repo.Raw("switch", "-q", "-");
        return repo;
    }

    private static async Task<HistoryViewModel> ShowHistoryAsync(AppHarness app)
    {
        app.Tab.LeftTab = LeftPanelTab.History;
        var history = app.Tab.History;
        await AppHarness.Until(() => history.Rows.Count > 0, "the history to load");
        return history;
    }

    private static CommitRow RowOf(HistoryViewModel history, TempRepo repo, string revision)
    {
        var oid = Oid.ParseHex(repo.Raw("rev-parse", revision).Trim());
        return history.DisplayRows.Single(r => r.Oid == oid);
    }

    private static async Task<CherryPickDialogViewModel> PickerAsync(AppHarness app)
    {
        await AppHarness.Until(() => app.Main.Dialog is CherryPickDialogViewModel { Loading: false }, "the branch picker");
        return (CherryPickDialogViewModel)app.Main.Dialog!;
    }

    private static List<string> Subjects(TempRepo repo, int count) => [.. repo.Raw("log", "--format=%s", $"-{count}").Trim().Split('\n')];

    [AvaloniaFact]
    public async Task Two_selected_commits_go_to_an_existing_branch_oldest_first()
    {
        using var repo = Repo();
        using var app = await AppHarness.OpenAsync(repo);
        var history = await ShowHistoryAsync(app);

        history.Click(RowOf(history, repo, "HEAD"), ctrl: false, shift: false);
        history.Click(RowOf(history, repo, "HEAD~2"), ctrl: true, shift: false);
        Assert.Equal(2, history.PickedCount);
        Assert.Equal("Cherry-pick 2 commits", history.CherryPickLabel);

        var picking = history.CherryPickCommand.ExecuteAsync(null);
        var picker = await PickerAsync(app);
        Assert.Equal("Cherry-pick 2 commits to…", picker.Title);
        picker.Query = "TAR";
        Assert.Equal(["target"], picker.Branches.Select(b => b.Name));
        Assert.True(picker.CanCreate); // not an existing name: offered as a new branch
        picker.Query = "Target";
        Assert.False(picker.CanCreate);
        picker.Branches[0].ChooseCommand.Execute(null);
        await picking;

        Assert.Equal("target", repo.CurrentBranch());
        Assert.Equal(["add c", "add a", "target edit"], Subjects(repo, 3));
        Assert.Contains(app.Main.Alerts, a => !a.IsError && a.Message == "Cherry-picked 2 commits to target");
        Assert.Equal(0, history.PickedCount);
    }

    [AvaloniaFact]
    public async Task Shift_selects_a_range_and_ctrl_toggles_out_of_it()
    {
        using var repo = Repo();
        using var app = await AppHarness.OpenAsync(repo);
        var history = await ShowHistoryAsync(app);
        var (head, a) = (RowOf(history, repo, "HEAD"), RowOf(history, repo, "HEAD~2"));

        history.Click(head, ctrl: false, shift: false);
        history.Click(a, ctrl: false, shift: true);
        Assert.Equal(3, history.PickedCount);
        Assert.Same(a, history.Selected); // the details follow the clicked row
        Assert.Equal([a.Oid, RowOf(history, repo, "HEAD~1").Oid, head.Oid], history.PickedOldestFirst());

        history.Click(a, ctrl: true, shift: false);
        Assert.Equal(2, history.PickedCount);
        Assert.False(a.IsSelected);
        Assert.NotSame(a, history.Selected);

        // A plain click starts over; keyboard moves do too.
        history.Click(a, ctrl: false, shift: false);
        Assert.Equal(1, history.PickedCount);
        history.Move(-1);
        Assert.Equal(1, history.PickedCount);
        Assert.True(RowOf(history, repo, "HEAD~1").IsSelected);
    }

    [AvaloniaFact]
    public async Task Browsing_an_older_commit_the_picker_creates_a_branch_there()
    {
        using var repo = Repo();
        using var app = await AppHarness.OpenAsync(repo);
        var history = await ShowHistoryAsync(app);
        var main = repo.CurrentBranch();
        var start = repo.Raw("rev-parse", "HEAD~3").Trim();
        await history.CheckoutCommand.ExecuteAsync(RowOf(history, repo, "HEAD~3"));
        await AppHarness.Until(() => app.Tab.BrowsingHistory && history.DisplayRows.Count == 4, "browsing the initial commit");

        history.Select(RowOf(history, repo, main));
        var picking = history.CherryPickCommand.ExecuteAsync(null);
        var picker = await PickerAsync(app);
        picker.Query = "  backport  ";
        Assert.True(picker.CanCreate);
        Assert.Equal("Create and cherry-pick to \"backport\"", picker.CreateLabel);
        Assert.True(picker.NoMatches);
        picker.CreateCommand.Execute(null);
        await picking;

        Assert.Equal("backport", repo.CurrentBranch());
        Assert.Equal(start, repo.Raw("rev-parse", "HEAD~1").Trim());
        Assert.True(repo.Exists("c.txt"));
        Assert.False(repo.Exists("a.txt"));
        Assert.Contains(app.Main.Alerts, a => a.Message == "Cherry-picked 1 commit to backport");
    }

    [AvaloniaFact]
    public async Task Cancelling_the_picker_keeps_the_selection_and_changes_nothing()
    {
        using var repo = Repo();
        using var app = await AppHarness.OpenAsync(repo);
        var history = await ShowHistoryAsync(app);
        var (branch, head) = (repo.CurrentBranch(), repo.Head());

        history.Select(RowOf(history, repo, "HEAD"));
        var picking = history.CherryPickCommand.ExecuteAsync(null);
        (await PickerAsync(app)).CancelCommand.Execute(null);
        await picking;

        Assert.Equal((branch, head), (repo.CurrentBranch(), repo.Head()));
        Assert.Equal(1, history.PickedCount);
    }

    [AvaloniaFact]
    public async Task A_conflicting_sequence_pauses_in_the_dialog_once_per_conflicting_commit()
    {
        using var repo = Repo();
        // c.txt conflicts on target too, so the sequence pauses twice.
        repo.Raw("switch", "-q", "target");
        repo.CommitFile("c.txt", "target c\n", "target c");
        repo.Raw("switch", "-q", "-");
        using var app = await AppHarness.OpenAsync(repo);
        var history = await ShowHistoryAsync(app);

        history.Click(RowOf(history, repo, "HEAD"), ctrl: false, shift: false);
        history.Click(RowOf(history, repo, "HEAD~2"), ctrl: false, shift: true);
        var picking = history.CherryPickCommand.ExecuteAsync(null);
        var picker = await PickerAsync(app);
        picker.Branches.Single(b => b.Name == "target").ChooseCommand.Execute(null);

        // The command finishes when the conflict dialog closes (as revert does).
        await AppHarness.Until(() => app.Main.Dialog is ConflictDialogViewModel { Loaded: true }, "the conflict dialog");
        var dialog = (ConflictDialogViewModel)app.Main.Dialog!;
        Assert.Equal("Cherry-pick conflicts", dialog.Title);
        var first = dialog.Incoming;
        Assert.Equal(["b.txt"], dialog.Files.Select(f => f.DisplayPath));
        dialog.Files[0].ChooseIncomingCommand.Execute(null);
        await dialog.CompleteCommand.ExecuteAsync(null);

        // Same dialog, next commit.
        Assert.Same(dialog, app.Main.Dialog);
        await AppHarness.Until(() => dialog.Loaded && dialog.Files.Count == 1 && dialog.Files[0].DisplayPath == "c.txt", "the second conflict");
        Assert.NotEqual(first, dialog.Incoming);
        Assert.Equal($"Cherry-picking {dialog.Incoming}", dialog.Subtitle);
        dialog.Files[0].ChooseCurrentCommand.Execute(null);
        await dialog.CompleteCommand.ExecuteAsync(null);

        await picking;
        Assert.Null(app.Main.Dialog);
        await AppHarness.Until(() => app.Tab.Snapshot?.Operation.Kind == OperationKind.None, "the cherry-pick to end");
        Assert.Equal("source version\n", repo.ReadNormalized("b.txt"));
        Assert.Equal("target c\n", repo.ReadNormalized("c.txt"));
        // "add c" resolved to the current side has nothing left: it is skipped, not an empty commit.
        Assert.Equal(["edit b", "add a", "target c"], Subjects(repo, 3));
    }
}
