using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Controls.Presenters;
using Avalonia.Headless.XUnit;
using System.IO.Compression;
using System.Text;
using Avalonia.VisualTree;
using CheeseGit.App.Controls;
using CheeseGit.App.Services;
using CheeseGit.App.ViewModels;
using CheeseGit.Core.Tests;

namespace CheeseGit.App.Tests;

/// <summary>Problem reports: nothing the repository makes private reaches the anonymized screenshot or log.</summary>
public class ProblemReportTests
{
    /// <summary>Unique marker words in every kind of private string the app shows.</summary>
    internal static readonly string[] Markers = ["zebracorn", "platypus", "quokka", "wombatremote", "hunter2pass", "acmecorp", "secretproj", "axolotl", "narwhal"];

    internal static TempRepo CanaryRepo()
    {
        var repo = TempRepo.Init();
        repo.Write("zebracorn-dir/platypus.txt", "narwhal line one\nnarwhal line two\n");
        repo.CommitAll("narwhal initial");
        repo.Raw("checkout", "-q", "-b", "feature/quokka");
        repo.Raw("remote", "add", "wombatremote", "https://user:hunter2pass@example.com/acmecorp/secretproj.git");
        repo.Write("zebracorn-dir/platypus.txt", "narwhal line one\naxolotl line two\n");
        return repo;
    }

    internal static void AssertNoMarkers(string text, TempRepo repo, string where)
    {
        foreach (var marker in Markers.Append(repo.Path).Append(Path.GetFileName(repo.Path)))
        {
            Assert.False(text.Contains(marker, StringComparison.OrdinalIgnoreCase), $"{where} still contains '{marker}':\n{text}");
        }
    }

    [AvaloniaFact]
    public async Task The_anonymized_log_keeps_git_readable_and_maps_names_like_the_UI()
    {
        using var repo = CanaryRepo();
        using var app = await AppHarness.OpenAsync(repo);
        await app.Staging.RowActionAsync(app.Unstaged("zebracorn-dir/platypus.txt"));
        await AppHarness.Until(() => app.StagedPaths.Count == 1, "the file to be staged");
        app.Staging.Summary = "axolotl summary";
        await app.Staging.CommitCommand.ExecuteAsync(null);
        await AppHarness.Until(() => app.Staging.IsClean, "the commit");

        var anonymizer = ReportAnonymizer.Create(app.Main);
        var plain = app.Main.CommandLog.ExportText(anonymizer: null);
        Assert.Contains("platypus", plain);
        var export = app.Main.CommandLog.ExportText(anonymizer);

        AssertNoMarkers(export, repo, "the log");
        Assert.Contains("$ git ", export);
        Assert.Contains("--end-of-options", export);
        // Paths reach git through stdin, so the label is where the file shows; its name maps as in the file list.
        Assert.Contains($"Stage {anonymizer.Path("platypus.txt")} (", export);
        Assert.EndsWith("/" + anonymizer.Path("platypus.txt"), anonymizer.Path("zebracorn-dir/platypus.txt"));
    }

    /// <summary>Every text the window shows: text blocks and their runs, text boxes, paths, the diff's cells.</summary>
    private static string ShownText(Visual root)
    {
        var texts = new List<string>();
        foreach (var visual in root.GetSelfAndVisualDescendants())
        {
            switch (visual)
            {
                case TextBlock block:
                    texts.Add(block.Text ?? "");
                    if (block.Inlines is { } inlines) texts.AddRange(inlines.OfType<Run>().Select(r => r.Text ?? ""));
                    break;
                case TextPresenter presenter when presenter.Opacity > 0: // a covered text box (ReportCapture.Cover)
                    texts.Add(presenter.Text ?? "");
                    break;
                case SmartPath path:
                    texts.Add(path.Text ?? "");
                    break;
                case DiffView diff:
                    texts.AddRange(diff.ShownTexts());
                    break;
            }
        }
        return string.Join("\n", texts);
    }

    private static async Task<(MainWindow Window, MainViewModel Main, RepoTabViewModel Tab)> ShowAsync(TempRepo repo)
    {
        var main = AppHarness.CreateMain(new Services.AppSettings { OpenRepositories = [repo.Path] });
        var window = new MainWindow { Width = 1200, Height = 800, DataContext = main };
        window.Show();
        await AppHarness.Until(() => main.Tabs.Count == 1 && main.Tabs[0].IsLoaded && main.Tabs[0].Staging.Loaded, "the tab to open");
        return (window, main, main.Tabs[0]);
    }

    private static async Task CheckAsync(MainWindow window, MainViewModel main, TempRepo repo, string where)
    {
        window.UpdateLayout();
        var before = ShownText(window);
        Assert.Contains("platypus", before); // the test shows what it means to hide

        using (ReportCapture.Apply(window, ReportAnonymizer.Create(main)))
        {
            window.UpdateLayout();
            AssertNoMarkers(ShownText(window), repo, where);
        }

        window.UpdateLayout();
        Assert.Equal(before, ShownText(window));
    }

    [AvaloniaFact]
    public async Task The_anonymized_window_shows_no_private_text_and_comes_back_unchanged()
    {
        using var repo = CanaryRepo();
        repo.Raw("stash", "push", "-q", "-m", "narwhal stash");
        repo.Write("zebracorn-dir/platypus.txt", "narwhal line one\naxolotl line two\n");
        var (window, main, tab) = await ShowAsync(repo);
        try
        {
            // Staging: the file list, its diff, a summary being typed.
            var row = tab.Staging.Unstaged.All.Single();
            tab.Staging.Unstaged.Click(row, ctrl: false, shift: false);
            await AppHarness.Until(() => tab.Diff.Diff is not null, "the diff");
            tab.Staging.Summary = "axolotl draft summary";
            await CheckAsync(window, main, repo, "the staging view");
            Assert.Equal("axolotl draft summary", tab.Staging.Summary); // the two-way binding was not touched

            // History: the commit list and a selected commit's files and diff.
            tab.LeftTab = LeftPanelTab.History;
            await AppHarness.Until(() => tab.History.Rows.Count > 0 && tab.History.Rows.All(r => r.Info is not null), "the history to load");
            tab.History.Click(tab.History.Rows[^1], ctrl: false, shift: false);
            await AppHarness.Until(() => tab.History.Details.Files.Count > 0, "the commit's files");
            tab.History.Details.SelectFile(tab.History.Details.Files[0]);
            await AppHarness.Until(() => tab.History.Diff.Diff is not null, "the commit's diff");
            await CheckAsync(window, main, repo, "the history");

            // Stash: the entry and its message.
            tab.LeftTab = LeftPanelTab.Stash;
            await AppHarness.Until(() => tab.Stash.Rows.Count > 0, "the stashes");
            tab.Stash.Select(tab.Stash.Rows[0]);
            await AppHarness.Until(() => tab.Stash.Details.Files.Count > 0, "the stash's files");
            window.UpdateLayout();
            Assert.Contains("narwhal stash", ShownText(window));
            using (ReportCapture.Apply(window, ReportAnonymizer.Create(main)))
            {
                window.UpdateLayout();
                AssertNoMarkers(ShownText(window), repo, "the stash view");
            }
        }
        finally
        {
            window.Close();
            main.Dispose();
        }
    }

    private static Dictionary<string, byte[]> Unzip(byte[] zip)
    {
        using var archive = new ZipArchive(new MemoryStream(zip));
        return archive.Entries.ToDictionary(e => e.FullName, e =>
        {
            using var stream = e.Open();
            using var copy = new MemoryStream();
            stream.CopyTo(copy);
            return copy.ToArray();
        });
    }

    [AvaloniaFact]
    public async Task The_report_dialog_saves_the_anonymized_report_unless_told_otherwise()
    {
        using var repo = CanaryRepo();
        using var app = await AppHarness.OpenAsync(repo);
        var platform = new FakePlatform();
        app.Main.Platform = platform;
        await app.Staging.RowActionAsync(app.Unstaged("zebracorn-dir/platypus.txt"));
        await AppHarness.Until(() => app.StagedPaths.Count == 1, "the file to be staged");

        await app.Main.ReportProblemCommand.ExecuteAsync(null);
        var dialog = Assert.IsType<ProblemReportDialogViewModel>(app.Main.Dialog);
        Assert.True(dialog.Anonymized);
        AssertNoMarkers(dialog.LogPreview, repo, "the log preview");
        Assert.NotNull(dialog.Screenshot); // the preview decodes

        await dialog.SaveCommand.ExecuteAsync(null);
        Assert.Null(app.Main.Dialog);
        var (name, content) = platform.Saved!.Value;
        Assert.EndsWith(".zip", name);
        var files = Unzip(content);
        Assert.Equal([ProblemReport.LogName, ProblemReport.EnvironmentName, ProblemReport.ScreenshotName], files.Keys.Order());
        Assert.Equal(1, files[ProblemReport.ScreenshotName][^1]); // FakePlatform marks an anonymized capture
        var log = Encoding.UTF8.GetString(files[ProblemReport.LogName]);
        var environment = Encoding.UTF8.GetString(files[ProblemReport.EnvironmentName]);
        AssertNoMarkers(log + environment, repo, "the saved report");
        Assert.Contains("Stage ", log);
        Assert.Contains("Anonymized: yes", environment);
        Assert.Contains("1 changed files", environment);

        // Turned off, the same report as it is.
        await app.Main.ReportProblemCommand.ExecuteAsync(null);
        dialog = Assert.IsType<ProblemReportDialogViewModel>(app.Main.Dialog);
        dialog.Anonymized = false;
        Assert.Contains("platypus", dialog.LogPreview);
        await dialog.SaveCommand.ExecuteAsync(null);
        files = Unzip(platform.Saved!.Value.Content);
        Assert.Equal(0, files[ProblemReport.ScreenshotName][^1]);
        Assert.Contains("Stage platypus.txt", Encoding.UTF8.GetString(files[ProblemReport.LogName]));
        Assert.Contains("Anonymized: no", Encoding.UTF8.GetString(files[ProblemReport.EnvironmentName]));
    }

    [Fact]
    public void Pathspec_magic_stays_readable()
    {
        var anonymizer = new Core.Diagnostics.Anonymizer();
        var args = CommandCall.AnonymizeArgs(["ls-files", "-z", "--", ":(glob)secret/**/.gitattributes", "secret/notes.txt"], anonymizer);
        Assert.Equal(["ls-files", "-z", "--", ":(glob)" + anonymizer.Path("secret/**/.gitattributes"), anonymizer.Path("secret/notes.txt")], args);
        Assert.DoesNotContain("secret", string.Concat(args));
    }

    [Fact]
    public void Refs_messages_and_numbers_in_arguments_are_treated_by_kind()
    {
        var anonymizer = new Core.Diagnostics.Anonymizer();
        var args = CommandCall.AnonymizeArgs(["merge", "--no-edit", "-m", "Merge secret work", "--end-of-options", "refs/heads/secret"], anonymizer);
        Assert.Equal(["merge", "--no-edit", "-m", anonymizer.Text("Merge secret work"), "--end-of-options", "refs/heads/" + anonymizer.Ref("secret")], args);
        Assert.Equal(["cherry-pick", "-m", "1"], CommandCall.AnonymizeArgs(["cherry-pick", "-m", "1"], anonymizer));
    }
}
