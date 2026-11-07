using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using CheeseGit.App.Controls;
using CheeseGit.App.Services;
using CheeseGit.App.ViewModels;
using CheeseGit.App.Views;
using CheeseGit.Core.Tests;

namespace CheeseGit.App.Tests;

/// <summary>The pointer on the branch dropdown and the graph's dots, through the real views on the headless platform.</summary>
public class BranchDropdownInputTests
{
    [AvaloniaFact]
    public async Task The_pointer_on_a_row_highlights_its_branch_and_its_copy_button_copies()
    {
        using var repo = TempRepo.Init();
        repo.Write("a.txt", "a\n");
        repo.CommitAll("initial");
        var main = repo.CurrentBranch();
        repo.Raw("switch", "-q", "-c", "feature");
        repo.CommitFile("f.txt", "f\n", "feature work");
        repo.Raw("switch", "-q", main);
        repo.CommitFile("m.txt", "m\n", "main work");

        var shell = AppHarness.CreateMain(new AppSettings { OpenRepositories = [repo.Path] });
        var window = new MainWindow { Width = 1280, Height = 800, DataContext = shell };
        window.Show();
        try
        {
            await AppHarness.Until(() => shell.Tabs.Count == 1 && shell.Tabs[0].IsLoaded, "the tab to open");
            var tab = shell.Tabs[0];
            var platform = new FakePlatform();
            tab.MainShell.Platform = platform;
            tab.LeftTab = LeftPanelTab.History;
            var history = tab.History;
            await AppHarness.Until(() => history.Rows.Count == 2, "the history");
            history.Branches.Single(b => b.Name == "feature").ToggleCommand.Execute(null);
            await AppHarness.Until(() => history.Rows.Count == 3, "feature's commit");
            window.UpdateLayout();

            var view = window.GetVisualDescendants().OfType<RepoView>().Single(v => v.IsEffectivelyVisible);
            view.OpenBranchDropdown();
            await AppHarness.Until(() => tab.BranchList.IsLoaded && tab.BranchList.Items.Count == 2, "the branches");

            // The dropdown is a popup with its own top level.
            Grid? rowGrid = null;
            await AppHarness.Until(() =>
            {
                rowGrid = view.BranchListBox.GetVisualDescendants().OfType<Grid>()
                    .FirstOrDefault(g => g.DataContext is LocalBranchRow { Name: "feature" } && g.Height == 30);
                return rowGrid is not null;
            }, "the feature row");
            var row = rowGrid!;
            var top = TopLevel.GetTopLevel(row)!;
            top.UpdateLayout();

            var at = row.TranslatePoint(new Point(row.Bounds.Width / 2, row.Bounds.Height / 2), top)!.Value;
            top.MouseMove(at);
            Assert.Equal(1, history.GraphFade);
            Assert.Equal(history.View!.Layout.NodeColor(history.Rows.Single(r => r.Oid.ToHex() == repo.Raw("rev-parse", "feature").Trim()).Row), history.HighlightColor);

            var copy = row.GetVisualDescendants().OfType<Button>().Single(b => b.Classes.Contains("copy-name"));
            Assert.Equal(0.45, copy.Opacity); // Shown on the row under the pointer.
            var other = view.BranchListBox.GetVisualDescendants().OfType<Button>()
                .Single(b => b.Classes.Contains("copy-name") && b.DataContext is LocalBranchRow { IsCurrent: true });
            Assert.Equal(0, other.Opacity);

            var onCopy = copy.TranslatePoint(new Point(copy.Bounds.Width / 2, copy.Bounds.Height / 2), top)!.Value;
            top.MouseMove(onCopy);
            top.MouseDown(onCopy, MouseButton.Left);
            top.MouseUp(onCopy, MouseButton.Left);
            await AppHarness.Until(() => platform.Clipboard == "feature", "the name to be copied");
            Assert.True(tab.BranchList.IsOpen);
            Assert.Equal(main, tab.Snapshot!.Head.Branch);

            // Off the rows: back to normal.
            top.MouseMove(new Point(1, 1));
            Assert.Equal(0, history.GraphFade);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task The_pointer_on_a_dot_shows_the_branch_tooltip()
    {
        using var repo = TempRepo.Init();
        repo.Write("a.txt", "a\n");
        repo.CommitAll("initial");
        repo.CommitFile("b.txt", "b\n", "second");
        var main = repo.CurrentBranch();

        var shell = AppHarness.CreateMain(new AppSettings { OpenRepositories = [repo.Path] });
        var window = new MainWindow { Width = 1280, Height = 800, DataContext = shell };
        window.Show();
        try
        {
            await AppHarness.Until(() => shell.Tabs.Count == 1 && shell.Tabs[0].IsLoaded, "the tab to open");
            shell.Tabs[0].LeftTab = LeftPanelTab.History;
            await AppHarness.Until(() => shell.Tabs[0].History.Rows.Count == 2, "the history");
            window.UpdateLayout();
            GraphCell? cell = null;
            await AppHarness.Until(() =>
            {
                window.UpdateLayout();
                cell = window.GetVisualDescendants().OfType<GraphCell>().FirstOrDefault(c => c.IsEffectivelyVisible && c.Commit is { Row: 1 });
                return cell is not null;
            }, "the second row");

            var shown = cell!;
            var dot = shown.TranslatePoint(new Point(GraphCell.PadLeft + (GraphCell.LaneWidth / 2), shown.Bounds.Height / 2), window)!.Value;
            window.MouseMove(dot);
            var tip = Assert.IsType<ToolTip>(ToolTip.GetTip(shown));
            Assert.Equal($"{main}\nCheck out this commit", tip.Content);
            Assert.Contains("name", tip.Classes);

            window.MouseMove(new Point(800, 400));
            Assert.Null(ToolTip.GetTip(shown));
        }
        finally
        {
            window.Close();
        }
    }
}
