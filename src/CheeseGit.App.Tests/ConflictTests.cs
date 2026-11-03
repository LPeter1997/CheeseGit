using System.Diagnostics;
using Avalonia.Headless.XUnit;
using CheeseGit.App.ViewModels;
using CheeseGit.Core.Platform;
using CheeseGit.Core.Repository;
using CheeseGit.Core.Tests;

namespace CheeseGit.App.Tests;

/// <summary>
/// Phase 5: merging from the branch dropdown, reverting from the history and the conflict dialog.
/// Ports the old app's e2e specs 14 (clean merge), 15 (conflict), 16 (abort), 17 and 36 (resolution
/// strategies), 18 (revert) and 38 (merge alerts) to real repositories.
/// </summary>
public class ConflictTests
{
    /// <summary>
    /// The default branch and "feature" both change src/main.ts (and CHANGELOG.md); "clean" only adds
    /// a file. HEAD is on the default branch.
    /// </summary>
    private static TempRepo Branches()
    {
        var repo = TempRepo.Init();
        repo.Write("src/main.ts", "export const greeting = \"hello\";\n");
        repo.Write("CHANGELOG.md", "# Changelog\n");
        repo.CommitAll("initial");
        repo.Raw("switch", "-q", "-c", "feature");
        repo.Write("src/main.ts", "export const greeting = \"hi from feature\";\n");
        repo.Write("CHANGELOG.md", "# Changelog\n- feature\n");
        repo.CommitAll("feature greeting");
        repo.Raw("switch", "-q", "-");
        repo.Raw("switch", "-q", "-c", "clean");
        repo.CommitFile("src/format.ts", "export const format = 1;\n", "add format");
        repo.Raw("switch", "-q", "-");
        repo.Write("src/main.ts", "export const greeting = \"hello from main\";\n");
        repo.Write("CHANGELOG.md", "# Changelog\n- main\n");
        repo.CommitAll("main greeting");
        return repo;
    }

    private static async Task<LocalBranchRow> BranchRowAsync(AppHarness app, string name)
    {
        await app.Tab.BranchList.LoadAsync();
        return app.Tab.BranchList.Items.OfType<LocalBranchRow>().Single(r => r.Name == name);
    }

    private static async Task<ConflictDialogViewModel> DialogAsync(AppHarness app)
    {
        await AppHarness.Until(() => app.Main.Dialog is ConflictDialogViewModel { Loaded: true }, "the conflict dialog");
        return (ConflictDialogViewModel)app.Main.Dialog!;
    }

    private static async Task<(Task Merging, ConflictDialogViewModel Dialog)> MergeWithConflictsAsync(AppHarness app, string branch = "feature")
    {
        var merging = (await BranchRowAsync(app, branch)).MergeCommand.ExecuteAsync(null);
        return (merging, await DialogAsync(app));
    }

    private static bool Clean(TempRepo repo) => repo.Raw("status", "--porcelain").Trim().Length == 0;

    [AvaloniaFact]
    public async Task A_clean_merge_says_how_many_commits_came_in_and_then_that_it_is_up_to_date()
    {
        using var repo = Branches();
        using var app = await AppHarness.OpenAsync(repo);
        var branch = repo.CurrentBranch();
        await (await BranchRowAsync(app, "clean")).MergeCommand.ExecuteAsync(null);

        Assert.Null(app.Main.Dialog);
        Assert.Equal(branch, repo.CurrentBranch());
        Assert.True(repo.Exists("src/format.ts"));
        Assert.Contains(app.Main.Alerts, a => !a.IsError && a.Message == "Merged 1 commit from clean");

        await (await BranchRowAsync(app, "clean")).MergeCommand.ExecuteAsync(null);
        Assert.Contains(app.Main.Alerts, a => !a.IsError && a.Message == "Already up to date: nothing to merge from clean");
    }

    [AvaloniaFact]
    public async Task The_merge_tooltip_predicts_conflicts_without_touching_anything()
    {
        using var repo = Branches();
        using var app = await AppHarness.OpenAsync(repo);
        var head = repo.Raw("rev-parse", "HEAD");

        var feature = await BranchRowAsync(app, "feature");
        Assert.Equal("Merge feature into the current branch", feature.MergeToolTip);
        await feature.PredictAsync();
        Assert.Equal("Merge feature into the current branch\nConflicts expected in 2 files: CHANGELOG.md, src/main.ts", feature.MergeToolTip);

        var clean = await BranchRowAsync(app, "clean");
        await clean.PredictAsync();
        Assert.Equal("No conflicts expected", clean.Prediction);

        Assert.Equal(head, repo.Raw("rev-parse", "HEAD"));
        Assert.True(Clean(repo));
        Assert.Null(app.Main.Dialog);

        // After the merge the rows are rebuilt; the old prediction does not carry over.
        await clean.MergeCommand.ExecuteAsync(null);
        var again = await BranchRowAsync(app, "clean");
        Assert.Null(again.Prediction);
        await again.PredictAsync();
        Assert.Equal("Already up to date: nothing to merge", again.Prediction);
    }

    [Fact]
    public void Many_predicted_conflicts_are_summarised()
    {
        var files = new[] { "a", "b", "c", "d", "e" }.Select(p => (Core.Git.GitPath)p).ToList();
        Assert.Equal("Conflicts expected in 5 files: a, b, c and 2 more", MergeableBranchRow.Describe(new MergePrediction.Conflicts(files)));
        Assert.Equal("Conflicts expected in 1 file: a", MergeableBranchRow.Describe(new MergePrediction.Conflicts(files[..1])));
        Assert.Null(MergeableBranchRow.Describe(new MergePrediction.Unknown()));
        Assert.Null(MergeableBranchRow.Describe(null));
    }

    [AvaloniaFact]
    public async Task Conflicts_open_the_dialog_and_completing_commits_the_merge()
    {
        using var repo = Branches();
        using var app = await AppHarness.OpenAsync(repo);
        var branch = repo.CurrentBranch();
        var (merging, dialog) = await MergeWithConflictsAsync(app);

        Assert.Equal("Merge conflicts", dialog.Title);
        Assert.Equal($"Merging feature into {branch}", dialog.Subtitle);
        Assert.Equal(["CHANGELOG.md", "src/main.ts"], dialog.Files.Select(f => f.DisplayPath).Order());
        var main = dialog.Files.Single(f => f.DisplayPath == "src/main.ts");
        Assert.Equal(("src/", "main.ts", 1), (main.Directory, main.FileName, main.Count));
        // git's prepared message, without its "# Conflicts" comment.
        Assert.StartsWith("Merge branch 'feature'", dialog.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("#", dialog.Message);
        Assert.Equal("Complete merge", dialog.CompleteLabel);
        Assert.False(dialog.CompleteCommand.CanExecute(null));

        foreach (var file in dialog.Files) file.ChooseCurrentCommand.Execute(null);
        Assert.True(dialog.CompleteCommand.CanExecute(null));
        // A second click on the chosen side clears it.
        main.ChooseCurrentCommand.Execute(null);
        Assert.Null(main.Resolution);
        Assert.False(dialog.CompleteCommand.CanExecute(null));
        main.ChooseCurrentCommand.Execute(null);

        dialog.Message = "Merge the feature greeting";
        await dialog.CompleteCommand.ExecuteAsync(null);
        await merging;

        Assert.Null(app.Main.Dialog);
        Assert.Equal(branch, repo.CurrentBranch());
        Assert.Equal("Merge the feature greeting", repo.Raw("log", "-1", "--format=%s").Trim());
        Assert.Equal(2, repo.Raw("log", "-1", "--format=%P").Trim().Split(' ').Length);
        Assert.True(Clean(repo));
        Assert.Contains("hello from main", repo.Read("src/main.ts"));
        await AppHarness.Until(() => !app.Tab.HasOperation, "the banner to go");
        Assert.Contains(app.Main.Alerts, a => a.Message == "Merged feature");
    }

    [AvaloniaFact]
    public async Task Aborting_restores_the_branch_as_it_was()
    {
        using var repo = Branches();
        using var app = await AppHarness.OpenAsync(repo);
        var head = repo.Head();
        var (merging, dialog) = await MergeWithConflictsAsync(app);
        Assert.Equal("Abort merge", dialog.AbortLabel);

        await dialog.AbortCommand.ExecuteAsync(null);
        await merging;
        Assert.Null(app.Main.Dialog);
        Assert.Equal(head, repo.Head());
        Assert.True(Clean(repo));
        Assert.Contains("hello from main", repo.Read("src/main.ts"));
        await AppHarness.Until(() => !app.Tab.HasOperation, "the banner to go");
    }

    [AvaloniaTheory]
    [InlineData(ConflictResolution.AcceptCurrent, "export const greeting = \"hello from main\";\n")]
    [InlineData(ConflictResolution.AcceptIncoming, "export const greeting = \"hi from feature\";\n")]
    [InlineData(ConflictResolution.AcceptBoth, "export const greeting = \"hello from main\";\nexport const greeting = \"hi from feature\";\n")]
    public async Task Each_strategy_writes_its_version(ConflictResolution resolution, string expected)
    {
        using var repo = Branches();
        using var app = await AppHarness.OpenAsync(repo);
        var (merging, dialog) = await MergeWithConflictsAsync(app);
        foreach (var file in dialog.Files)
        {
            (resolution switch
            {
                ConflictResolution.AcceptCurrent => file.ChooseCurrentCommand,
                ConflictResolution.AcceptIncoming => file.ChooseIncomingCommand,
                _ => file.ChooseBothCommand,
            }).Execute(null);
        }
        await dialog.CompleteCommand.ExecuteAsync(null);
        await merging;
        Assert.Equal(expected, repo.ReadNormalized("src/main.ts"));
        Assert.True(Clean(repo));
    }

    [AvaloniaFact]
    public async Task Files_resolved_or_staged_elsewhere_are_noticed_and_kept()
    {
        using var repo = Branches();
        using var app = await AppHarness.OpenAsync(repo);
        var (merging, dialog) = await MergeWithConflictsAsync(app);
        var main = dialog.Files.Single(f => f.DisplayPath == "src/main.ts");
        var changelog = dialog.Files.Single(f => f.DisplayPath == "CHANGELOG.md");

        // Merged by hand in an editor: the markers are gone, the file is not staged.
        repo.Write("src/main.ts", "export const greeting = \"merged by hand\";\n");
        // Resolved in a terminal: staged, so git no longer lists it.
        repo.Write("CHANGELOG.md", "# Changelog\n- both\n");
        repo.Raw("add", "CHANGELOG.md");
        app.Tab.RefreshOnFocus();
        await AppHarness.Until(() => main.EditedOutside && changelog.StagedOutside, "both files to show as resolved");
        Assert.True(main.IsResolvedOutside && changelog.IsResolvedOutside);
        Assert.True(dialog.CompleteCommand.CanExecute(null));

        await dialog.CompleteCommand.ExecuteAsync(null);
        await merging;
        Assert.Equal("export const greeting = \"merged by hand\";\n", repo.ReadNormalized("src/main.ts"));
        Assert.Equal("# Changelog\n- both\n", repo.ReadNormalized("CHANGELOG.md"));
        Assert.True(Clean(repo));
    }

    [AvaloniaFact]
    public async Task A_merge_paused_before_opening_shows_the_banner_and_the_dialog_survives_closing()
    {
        using var repo = Branches();
        Assert.NotEqual(0, repo.TryRaw("merge", "feature").ExitCode);
        using var app = await AppHarness.OpenAsync(repo);

        Assert.True(app.Tab.HasOperation);
        Assert.True(app.Tab.CanResolveOperation);
        Assert.Equal("Merge in progress (feature): 2 conflicted files", app.Tab.OperationBanner);

        var resolving = app.Tab.ResolveConflictsCommand.ExecuteAsync(null);
        var dialog = await DialogAsync(app);
        Assert.Equal(2, dialog.Files.Count);
        // "Later": the merge stays paused and the banner offers it again.
        dialog.CancelCommand.Execute(null);
        await resolving;
        Assert.True(app.Tab.HasOperation);

        resolving = app.Tab.ResolveConflictsCommand.ExecuteAsync(null);
        await DialogAsync(app);
        // Aborted in a terminal: the dialog has nothing left to do and closes.
        repo.Raw("merge", "--abort");
        app.Tab.RefreshOnFocus();
        await AppHarness.Until(() => app.Main.Dialog is null, "the dialog to close");
        await resolving;
        await AppHarness.Until(() => !app.Tab.HasOperation, "the banner to go");
    }

    [AvaloniaFact]
    public async Task Reverting_from_the_history_commits_or_opens_the_revert_dialog()
    {
        using var repo = TempRepo.Init();
        repo.CommitFile("file.txt", "line1\nline2\nline3\n", "initial commit");
        repo.CommitFile("other.txt", "x\n", "add other");
        var conflicting = repo.CommitFile("file.txt", "line1\nchanged\nline3\n", "modify line2");
        repo.CommitFile("file.txt", "line1\ndifferent\nline3\n", "modify again");
        using var app = await AppHarness.OpenAsync(repo);
        var history = app.Tab.History;
        app.Tab.LeftTab = LeftPanelTab.History;
        await AppHarness.Until(() => history.DisplayRows.Count == 4, "the history");

        // Clean: "add other" goes away in a new commit.
        await history.RevertCommand.ExecuteAsync(history.DisplayRows[2]);
        Assert.Null(app.Main.Dialog);
        Assert.False(repo.Exists("other.txt"));
        Assert.StartsWith("Revert \"add other\"", repo.Raw("log", "-1", "--format=%s"), StringComparison.Ordinal);

        await AppHarness.Until(() => history.DisplayRows.Count == 5, "the revert in the history");
        var row = history.DisplayRows.Single(r => r.Oid.ToHex() == conflicting);
        var reverting = history.RevertCommand.ExecuteAsync(row);
        var dialog = await DialogAsync(app);
        Assert.Equal("Revert conflicts", dialog.Title);
        Assert.Equal("Abort revert", dialog.AbortLabel);
        Assert.Equal("Complete revert", dialog.CompleteLabel);
        Assert.StartsWith("Revert \"modify line2\"", dialog.Message, StringComparison.Ordinal);
        Assert.Contains("This reverts commit " + conflicting, dialog.Message);

        dialog.Files.Single().ChooseIncomingCommand.Execute(null);
        await dialog.CompleteCommand.ExecuteAsync(null);
        await reverting;
        Assert.Equal("line1\nline2\nline3\n", repo.ReadNormalized("file.txt"));
        Assert.StartsWith("Revert \"modify line2\"", repo.Raw("log", "-1", "--format=%s"), StringComparison.Ordinal);
        Assert.True(Clean(repo));
    }

    [AvaloniaFact]
    public async Task A_cherry_pick_paused_in_a_terminal_can_be_completed()
    {
        using var repo = Branches();
        var feature = repo.Raw("rev-parse", "feature").Trim();
        Assert.NotEqual(0, repo.TryRaw("cherry-pick", feature).ExitCode);
        using var app = await AppHarness.OpenAsync(repo);
        Assert.StartsWith("Cherry-pick in progress", app.Tab.OperationBanner, StringComparison.Ordinal);

        var resolving = app.Tab.ResolveConflictsCommand.ExecuteAsync(null);
        var dialog = await DialogAsync(app);
        Assert.Equal("Cherry-pick conflicts", dialog.Title);
        Assert.StartsWith("feature greeting", dialog.Message, StringComparison.Ordinal);
        foreach (var file in dialog.Files) file.ChooseIncomingCommand.Execute(null);
        await dialog.CompleteCommand.ExecuteAsync(null);
        await resolving;
        Assert.Equal("feature greeting", repo.Raw("log", "-1", "--format=%s").Trim());
        Assert.Contains("hi from feature", repo.Read("src/main.ts"));
        Assert.True(Clean(repo));
    }

    [AvaloniaFact]
    public async Task The_merge_tool_gets_the_three_versions_and_the_choice_is_remembered()
    {
        using var repo = Branches();
        using var app = await AppHarness.OpenAsync(repo);
        var started = new List<ProcessStartInfo>();
        app.Main.MergeTools = new MergeToolRegistry(findExecutable: n => n is "code" or "meld" ? "/usr/bin/" + n : null, start: started.Add);
        var (merging, dialog) = await MergeWithConflictsAsync(app);

        Assert.Equal("Visual Studio Code", dialog.Tool?.DisplayName);
        Assert.True(dialog.HasManyTools);
        Assert.Equal("Open in Visual Studio Code", dialog.OpenLabel);
        dialog.ToolChoices.Single(c => c.Tool.Id == "meld").SelectCommand.Execute(null);
        Assert.Equal("meld", app.Main.Settings.PreferredMergeTool);
        Assert.Equal("Open in Meld", dialog.OpenLabel);

        var main = dialog.Files.Single(f => f.DisplayPath == "src/main.ts");
        await main.OpenInToolCommand.ExecuteAsync(null);
        var psi = Assert.Single(started);
        Assert.Equal("/usr/bin/meld", psi.FileName);
        // meld --output MERGED LOCAL BASE REMOTE
        Assert.Equal(repo.FullPath(Path.Combine("src", "main.ts")), psi.ArgumentList[1]);
        Assert.Contains("hello from main", File.ReadAllText(psi.ArgumentList[2]));
        Assert.Contains("\"hello\"", File.ReadAllText(psi.ArgumentList[3]));
        Assert.Contains("hi from feature", File.ReadAllText(psi.ArgumentList[4]));
        Assert.False(dialog.HasError);

        await dialog.AbortCommand.ExecuteAsync(null);
        await merging;
    }

    [AvaloniaFact]
    public async Task Without_merge_tools_the_button_says_so()
    {
        using var repo = Branches();
        using var app = await AppHarness.OpenAsync(repo);
        app.Main.MergeTools = new MergeToolRegistry(findExecutable: _ => null);
        var (merging, dialog) = await MergeWithConflictsAsync(app);
        Assert.False(dialog.HasTool);
        Assert.Equal("No merge tool", dialog.OpenLabel);
        await dialog.AbortCommand.ExecuteAsync(null);
        await merging;
    }
}
