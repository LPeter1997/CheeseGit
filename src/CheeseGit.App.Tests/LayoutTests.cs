using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media.Transformation;
using Avalonia.VisualTree;
using CheeseGit.App.Controls;
using CheeseGit.App.Services;
using CheeseGit.App.ViewModels;
using CheeseGit.Core.Tests;

namespace CheeseGit.App.Tests;

/// <summary>The window's layout and pointer behaviour, driven through the headless platform.</summary>
public class LayoutTests
{
    private static async Task<(MainWindow Window, MainViewModel Main)> ShowAsync(double width, params TempRepo[] repos)
    {
        var settings = new AppSettings { OpenRepositories = repos.Select(r => r.Path).ToList() };
        var main = AppHarness.CreateMain(settings);
        var window = new MainWindow { Width = width, Height = 700, DataContext = main };
        window.Show();
        await AppHarness.Until(() => main.Tabs.Count == repos.Length && main.Tabs.All(t => t.IsLoaded), "the tabs to open");
        window.UpdateLayout();
        return (window, main);
    }

    private static TempRepo Repo()
    {
        var repo = TempRepo.Init();
        repo.Write("a.txt", "a\n");
        repo.CommitAll("initial");
        return repo;
    }

    private static Control Container(MainWindow window, int index) =>
        (Control)window.FindControl<ItemsControl>("TabStrip")!.ContainerFromIndex(index)!;

    private static double TranslateX(Control control) =>
        control.RenderTransform is TransformOperations t ? t.Value.M31 : 0;

    [AvaloniaFact]
    public async Task Dragging_a_tab_follows_the_pointer_and_reorders_on_release()
    {
        using var a = Repo();
        using var b = Repo();
        using var c = Repo();
        var (window, main) = await ShowAsync(1280, a, b, c);
        try
        {
            var first = main.Tabs[0];
            var width = Container(window, 0).Bounds.Width;
            var y = Container(window, 0).Bounds.Height / 2;
            var start = new Point(width / 2, y);

            window.MouseDown(start, MouseButton.Left);
            window.MouseMove(start + new Point(width * 1.6, 0));
            window.UpdateLayout();

            // Mid-drag: the order is unchanged and the tab sits under the pointer.
            Assert.Same(first, main.Tabs[0]);
            Assert.Equal(width * 1.6, TranslateX(Container(window, 0)), tolerance: 0.5);

            window.MouseUp(start + new Point(width * 1.6, 0), MouseButton.Left);
            Assert.Equal([b.Path, c.Path, a.Path], main.Tabs.Select(t => t.Path));
            Assert.Same(first, main.SelectedTab);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task A_short_drag_is_a_click()
    {
        using var a = Repo();
        using var b = Repo();
        var (window, main) = await ShowAsync(1280, a, b);
        try
        {
            var second = Container(window, 1).Bounds;
            window.MouseDown(second.Center, MouseButton.Left);
            window.MouseMove(second.Center + new Point(3, 0));
            window.MouseUp(second.Center + new Point(3, 0), MouseButton.Left);
            Assert.Equal([a.Path, b.Path], main.Tabs.Select(t => t.Path));
            Assert.Same(main.Tabs[1], main.SelectedTab);
            Assert.Equal(0, TranslateX(Container(window, 1)));
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task Tabs_shrink_to_keep_the_add_button_in_the_window()
    {
        var repos = Enumerable.Range(0, 6).Select(_ => Repo()).ToArray();
        var (window, _) = await ShowAsync(800, repos);
        try
        {
            var panel = window.GetVisualDescendants().OfType<TabStripPanel>().Single();
            Assert.True(panel.ActualTabWidth < 176, $"tab width {panel.ActualTabWidth}");
            var add = window.FindControl<Button>("AddRepositoryButton")!;
            var addRight = add.TranslatePoint(new Point(add.Bounds.Width, 0), window)!.Value.X;
            var options = window.FindControl<Button>("OptionsButton")!;
            var optionsLeft = options.TranslatePoint(default, window)!.Value.X;
            Assert.True(addRight <= optionsLeft, $"add button ends at {addRight}, options start at {optionsLeft}");
        }
        finally
        {
            window.Close();
            foreach (var repo in repos) repo.Dispose();
        }
    }

    [Theory]
    [InlineData(300, 100, 100, 150, 150)] // both fit: equal
    [InlineData(300, 400, 400, 150, 150)] // neither fits: equal
    [InlineData(300, 220, 60, 220, 80)] // the long side grows to what it wants, the short side keeps the rest
    [InlineData(300, 400, 60, 240, 60)] // the long side takes all the slack and trims
    [InlineData(300, 60, 220, 80, 220)] // either side
    [InlineData(300, 140, 400, 140, 160)]
    public void Shared_row_splits_equally_unless_one_side_can_use_the_others_slack(
        double available, double first, double last, double expectedFirst, double expectedLast)
    {
        var (a, b) = SharedRowPanel.Split(available, first, last);
        Assert.Equal(expectedFirst, a, precision: 6);
        Assert.Equal(expectedLast, b, precision: 6);
    }

    [AvaloniaFact]
    public async Task A_long_branch_name_takes_the_remote_buttons_slack()
    {
        using var repo = Repo();
        repo.Raw("switch", "-q", "-c", "feature/a-very-long-branch-name-that-needs-more-than-half-of-the-column");
        var (window, _) = await ShowAsync(1280, repo);
        try
        {
            var branch = window.GetVisualDescendants().OfType<Button>().Single(x => x.Name == "BranchButton");
            var row = branch.GetVisualParent<SharedRowPanel>()!;
            var remote = window.GetVisualDescendants().OfType<Button>().Single(x => x.Name == "RemoteActionButton");
            // An equal split would give each side half of what the copy button leaves.
            var equal = (row.Bounds.Width - row.Children[1].Bounds.Width) / 2;
            Assert.True(branch.Bounds.Width > equal + 10, $"branch {branch.Bounds.Width}, equal share {equal}");

            // The remote button keeps its whole label.
            var label = remote.GetVisualDescendants().OfType<TextBlock>().First();
            var shown = label.Bounds.Width;
            label.Measure(Size.Infinity);
            Assert.True(shown >= label.DesiredSize.Width - 0.5, $"label {shown} wants {label.DesiredSize.Width}");
        }
        finally
        {
            window.Close();
        }
    }

    /// <summary>Fails when <paramref name="control"/> is not fully inside <paramref name="container"/> (in window coordinates).</summary>
    private static void AssertInside(Visual control, Visual container, string what)
    {
        Assert.True(control.IsEffectivelyVisible, $"{what} is hidden");
        var root = (Visual)TopLevel.GetTopLevel(control)!;
        var box = control.Bounds.WithX(0).WithY(0);
        var topLeft = control.TranslatePoint(box.TopLeft, root)!.Value;
        var bottomRight = control.TranslatePoint(box.BottomRight, root)!.Value;
        var outer = container.Bounds.WithX(0).WithY(0);
        var outerLeft = container.TranslatePoint(outer.TopLeft, root)!.Value;
        var outerRight = container.TranslatePoint(outer.BottomRight, root)!.Value;
        Assert.True(topLeft.X >= outerLeft.X - 0.5 && bottomRight.X <= outerRight.X + 0.5,
            $"{what} spans {topLeft.X:0}..{bottomRight.X:0}, outside {outerLeft.X:0}..{outerRight.X:0}");
    }

    private static readonly string LongText = string.Concat(Enumerable.Repeat("a long message that does not fit ", 8));

    [AvaloniaFact]
    public async Task A_long_stash_message_keeps_the_stash_actions_in_the_row()
    {
        using var repo = Repo();
        repo.Write("a.txt", "changed\n");
        repo.Raw("stash", "push", "-q", "-m", LongText);
        var (window, main) = await ShowAsync(720, repo);
        try
        {
            main.Tabs[0].LeftTab = LeftPanelTab.Stash;
            await AppHarness.Until(() => main.Tabs[0].Stash.Rows.Count == 1, "the stash list");
            window.UpdateLayout();
            var drop = window.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "StashDrop");
            var row = drop.GetVisualAncestors().OfType<Border>().First(b => b.Classes.Contains("stash-row"));
            foreach (var name in new[] { "StashApply", "StashPop", "StashDrop" })
            {
                AssertInside(window.GetVisualDescendants().OfType<Button>().Single(b => b.Name == name), row, name);
            }
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task A_long_author_and_commit_context_keep_the_buttons_in_view()
    {
        using var repo = Repo();
        repo.Write("b.txt", "b\n");
        repo.Raw("add", "b.txt");
        repo.Raw("-c", "user.name=" + LongText, "commit", "-q", "-m", LongText);
        var (window, main) = await ShowAsync(720, repo);
        try
        {
            var tab = main.Tabs[0];
            tab.LeftTab = LeftPanelTab.History;
            await AppHarness.Until(() => tab.History.Rows.Count == 2 && tab.History.Rows[0].Summary.Length > 0, "the history");
            tab.History.Select(tab.History.Rows[0]);
            await AppHarness.Until(() => tab.History.ContextLabel is not null && tab.History.Details.Files.Count == 1, "the selected commit's files");
            tab.History.Details.SelectFile(tab.History.Details.Files[0]);
            await AppHarness.Until(() => tab.ActiveDiff?.CanSearch == true, "the file's diff");
            window.UpdateLayout();

            // The row's copy-hash button, after a long author.
            var copy = window.GetVisualDescendants().OfType<Button>().First(b => b.IsEffectivelyVisible && Equals(ToolTip.GetTip(b), "Copy full hash"));
            var row = copy.GetVisualAncestors().OfType<Border>().First(b => b.Classes.Contains("commit-row"));
            AssertInside(copy, row, "copy hash");

            // The diff header: the view toggles and the search after a long "hash - summary".
            var toggles = window.GetVisualDescendants().OfType<StackPanel>().Single(p => p.Name == "DiffModeToggles");
            AssertInside(toggles, window, "Unified/Split");
            AssertInside(window.GetVisualDescendants().OfType<TextBox>().Single(b => b.PlaceholderText == "Search in diff"), window, "diff search");
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task Left_panel_tabs_share_the_width()
    {
        using var repo = Repo();
        var (window, main) = await ShowAsync(1280, repo);
        try
        {
            var tabs = window.GetVisualDescendants().OfType<Button>().Where(x => x.Classes.Contains("left-tab")).ToList();
            var grid = tabs[0].GetVisualParent<UniformGrid>()!;
            // No stash: Staging and History split the row.
            var visible = tabs.Where(x => x.IsVisible).ToList();
            Assert.Equal(2, visible.Count);
            Assert.All(visible, x => Assert.Equal(grid.Bounds.Width / 2, x.Bounds.Width, tolerance: 1));

            repo.Write("a.txt", "changed\n");
            repo.Raw("stash", "-q");
            await AppHarness.Until(() => main.Tabs[0].HasStashes, "the stash tab");
            window.UpdateLayout();
            Assert.All(tabs, x => Assert.Equal(grid.Bounds.Width / 3, x.Bounds.Width, tolerance: 1));
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task The_history_widens_the_panel_for_its_graph_and_staging_restores_it()
    {
        using var repo = Repo();
        var main = repo.CurrentBranch();
        for (var i = 0; i < 6; i++)
        {
            repo.Raw("switch", "-q", "-c", $"b{i}", main);
            repo.CommitFile($"f{i}.txt", $"{i}\n", $"work {i}");
        }
        repo.Raw("switch", "-q", main);
        for (var i = 0; i < 6; i++) repo.Raw("merge", "-q", "--no-ff", "--no-edit", $"b{i}");

        var (window, shell) = await ShowAsync(1280, repo);
        try
        {
            var tab = shell.Tabs[0];
            var layout = window.GetVisualDescendants().OfType<Views.RepoView>().Single().FindControl<Grid>("Layout")!;
            double Width() => layout.ColumnDefinitions[0].ActualWidth;
            var chosen = Width();

            tab.LeftTab = LeftPanelTab.History;
            await AppHarness.Until(() => tab.History.View is not null, "the history");
            var needed = GraphCell.ColumnWidth(tab.History.View!.Layout.LaneCount) + 320;
            Assert.True(needed > chosen, $"the graph needs {needed}, the panel is {chosen}");
            await AppHarness.Until(() => { AvaloniaHeadlessPlatform.ForceRenderTimerTick(); window.UpdateLayout(); return Math.Abs(Width() - needed) < 1; },
                $"the panel to grow to {needed} (is {Width()})");

            tab.LeftTab = LeftPanelTab.Staging;
            await AppHarness.Until(() => { AvaloniaHeadlessPlatform.ForceRenderTimerTick(); window.UpdateLayout(); return Math.Abs(Width() - chosen) < 1; },
                $"the panel to return to {chosen} (is {Width()})");
        }
        finally
        {
            window.Close();
        }
    }
}
