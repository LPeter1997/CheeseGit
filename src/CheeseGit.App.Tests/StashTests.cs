using Avalonia.Headless.XUnit;
using CheeseGit.App.ViewModels;
using CheeseGit.Core.Repository;
using CheeseGit.Core.Tests;

namespace CheeseGit.App.Tests;

/// <summary>
/// The Stash tab: ports of the old app's e2e specs 08 (list, details, apply, drop) and 21 (diff panel,
/// pop into staging), plus what they could not check: untracked files, conflicts on pop, and entries
/// whose index moved underneath the view.
/// </summary>
public class StashTests
{
    /// <summary>Three entries, newest first: "constants for beta" (with an untracked file), a WIP stash on feature, "config overhaul".</summary>
    private static TempRepo Stashes()
    {
        var repo = TempRepo.Init();
        repo.Write("src/config.ts", "export const config = {};\n");
        repo.Write("src/constants.ts", "export const A = 1;\n");
        repo.CommitAll("initial");
        repo.Write("src/config.ts", "export const config = { debug: true };\n");
        repo.Raw("stash", "push", "-q", "-m", "config overhaul");
        repo.Raw("switch", "-q", "-c", "feature");
        repo.Write("src/constants.ts", "export const A = 2;\n");
        repo.Raw("stash", "push", "-q");
        repo.Raw("switch", "-q", "-");
        repo.Write("src/constants.ts", "export const A = 1;\nexport const BETA = true;\n");
        repo.Write("notes/beta.md", "beta notes\n");
        repo.Raw("stash", "push", "-q", "--include-untracked", "-m", "constants for beta");
        return repo;
    }

    private static async Task<StashViewModel> OpenStashAsync(AppHarness app, int count)
    {
        app.Tab.LeftTab = LeftPanelTab.Stash;
        await AppHarness.Until(() => app.Tab.Stash.Rows.Count == count, $"{count} stash entries");
        return app.Tab.Stash;
    }

    private static async Task ConfirmAsync(AppHarness app, string question)
    {
        await AppHarness.Until(() => app.Main.Dialog is ConfirmDialogViewModel, "the confirmation");
        var dialog = (ConfirmDialogViewModel)app.Main.Dialog!;
        Assert.Equal(question, dialog.Message);
        dialog.ConfirmCommand.Execute(null);
    }

    [AvaloniaFact]
    public async Task The_tab_lists_entries_with_their_message_branch_author_and_hash()
    {
        using var repo = Stashes();
        using var app = await AppHarness.OpenAsync(repo);
        Assert.Equal("Stash (3)", app.Tab.StashTabLabel);
        var stash = await OpenStashAsync(app, 3);
        var main = repo.CurrentBranch();

        Assert.Equal("constants for beta", stash.Rows[0].Title);
        Assert.Equal(repo.Raw("log", "-1", "--format=%h", "HEAD").Trim() + " initial", stash.Rows[1].Title);
        Assert.Equal("config overhaul", stash.Rows[2].Title);
        Assert.Equal($"(On {main})", stash.Rows[0].Context);
        Assert.Equal("(WIP on feature)", stash.Rows[1].Context);
        Assert.All(stash.Rows, r => Assert.Equal("Test User", r.Author));
        Assert.Equal(repo.Raw("rev-parse", "stash@{2}").Trim()[..7], stash.Rows[2].ShortHash);
        await AppHarness.Until(() => stash.Rows.All(r => r.Stats is not null), "the stats");
        Assert.Equal("+2", stash.Rows[0].AdditionsText); // one line in constants.ts, one untracked
    }

    [AvaloniaFact]
    public async Task Selecting_an_entry_shows_its_files_with_untracked_ones_and_their_diffs()
    {
        using var repo = Stashes();
        using var app = await AppHarness.OpenAsync(repo);
        var stash = await OpenStashAsync(app, 3);

        stash.Select(stash.Rows[0]);
        Assert.Equal("stash@{0} - constants for beta", stash.ContextLabel);
        Assert.Same(stash.Diff, app.Tab.ActiveDiff);
        await AppHarness.Until(() => stash.Details.Files.Count == 2, "the stash's files");
        var untracked = stash.Details.Files.Single(f => f.DisplayPath == "notes/beta.md");
        Assert.Equal(CheeseGit.Core.Git.FileStatus.Untracked, untracked.Change.Status);

        stash.Details.SelectFile(untracked);
        await AppHarness.Until(() => stash.Diff.Diff is { } d && d.Path.ToString() == "notes/beta.md", "the untracked file's diff");
        Assert.Contains(stash.Diff.Diff!.Hunks.SelectMany(h => h.Lines), l => System.Text.Encoding.UTF8.GetString(l.Content) == "beta notes");

        Assert.Same(stash.Rows[1], stash.Move(1));
        await AppHarness.Until(() => stash.Details.Files.Count == 1, "the next entry's files");
        Assert.Equal("src/constants.ts", stash.Details.Files[0].DisplayPath);
    }

    [AvaloniaFact]
    public async Task Apply_asks_then_restores_the_changes_and_keeps_the_entry()
    {
        using var repo = Stashes();
        using var app = await AppHarness.OpenAsync(repo);
        var stash = await OpenStashAsync(app, 3);

        var applying = stash.Rows[2].ApplyCommand.ExecuteAsync(null);
        await ConfirmAsync(app, "Apply this stash without removing it?");
        await applying;

        Assert.Equal(3, repo.Raw("stash", "list").Trim().Split('\n').Length);
        Assert.Contains("debug: true", repo.ReadNormalized("src/config.ts"));
        await AppHarness.Until(() => app.UnstagedPaths.Contains("src/config.ts"), "the change in staging");
        Assert.Equal(3, stash.Rows.Count);
    }

    [AvaloniaFact]
    public async Task Pop_brings_untracked_files_back_and_removes_the_entry()
    {
        using var repo = Stashes();
        using var app = await AppHarness.OpenAsync(repo);
        var stash = await OpenStashAsync(app, 3);
        stash.Select(stash.Rows[1]);

        var popping = stash.Rows[0].PopCommand.ExecuteAsync(null);
        await ConfirmAsync(app, "Apply and remove this stash?");
        await popping;

        await AppHarness.Until(() => stash.Rows.Count == 2, "the entry to go");
        Assert.True(repo.Exists("notes/beta.md"));
        await AppHarness.Until(() => app.UnstagedPaths.Contains("src/constants.ts") && app.UnstagedPaths.Contains("notes/beta.md"), "the changes in staging");
        // The selected entry moved from index 1 to 0 and stays selected.
        Assert.True(stash.Rows[0].IsSelected);
        Assert.StartsWith("stash@{0} - ", stash.ContextLabel);
    }

    [AvaloniaFact]
    public async Task Popping_into_a_conflict_keeps_the_entry_and_says_so()
    {
        using var repo = Stashes();
        repo.CommitFile("src/config.ts", "export const config = { debug: false };\n", "meanwhile");
        using var app = await AppHarness.OpenAsync(repo);
        var stash = await OpenStashAsync(app, 3);

        var popping = stash.Rows[2].PopCommand.ExecuteAsync(null);
        await ConfirmAsync(app, "Apply and remove this stash?");
        await popping;

        Assert.Contains(app.Main.Alerts, a => a.Message == "The stash was applied with conflicts and kept. Resolve them in the staging list.");
        Assert.Equal(3, stash.Rows.Count);
        Assert.Contains("src/config.ts", repo.Raw("diff", "--name-only", "--diff-filter=U"));
    }

    [AvaloniaFact]
    public async Task Drop_asks_and_removes_the_entry_even_when_its_index_moved()
    {
        using var repo = Stashes();
        using var app = await AppHarness.OpenAsync(repo);
        var stash = await OpenStashAsync(app, 3);
        var overhaul = stash.Rows[2];

        // A stash made in a terminal that the view has not seen yet: "config overhaul" is now stash@{3}.
        repo.Write("src/config.ts", "terminal\n");
        repo.Raw("stash", "push", "-q", "-m", "from a terminal");

        var dropping = overhaul.DropCommand.ExecuteAsync(null);
        await ConfirmAsync(app, "Delete this stash? This cannot be undone.");
        await dropping;

        var left = repo.Raw("stash", "list");
        Assert.DoesNotContain("config overhaul", left);
        Assert.Contains("from a terminal", left);
        Assert.Contains("constants for beta", left);
        await AppHarness.Until(() => stash.Rows.Count == 3, "the list to follow");
    }

    [AvaloniaFact]
    public async Task Cancelling_the_confirmation_changes_nothing()
    {
        using var repo = Stashes();
        using var app = await AppHarness.OpenAsync(repo);
        var stash = await OpenStashAsync(app, 3);
        var dropping = stash.Rows[0].DropCommand.ExecuteAsync(null);
        await AppHarness.Until(() => app.Main.Dialog is ConfirmDialogViewModel, "the confirmation");
        ((ConfirmDialogViewModel)app.Main.Dialog!).CancelCommand.Execute(null);
        await dropping;
        Assert.Equal(3, repo.Raw("stash", "list").Trim().Split('\n').Length);
    }

    [Theory]
    [InlineData("On main: fix the counting", "fix the counting", "On main")]
    [InlineData("WIP on feature/x: abc1234 add login", "abc1234 add login", "WIP on feature/x")]
    [InlineData("just a message", "just a message", null)]
    [InlineData("On main: ", "On main:", null)]
    public void Messages_split_into_title_and_branch(string message, string title, string? context) =>
        Assert.Equal((title, context), StashViewModel.FormatMessage(message));
}
