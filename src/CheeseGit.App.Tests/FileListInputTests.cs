using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using CheeseGit.App.Services;
using CheeseGit.App.ViewModels;
using CheeseGit.App.Views;
using CheeseGit.Core.Tests;

namespace CheeseGit.App.Tests;

/// <summary>Pointer and keyboard input on the file lists, through the real views on the headless platform.</summary>
public class FileListInputTests
{
    private static async Task<(MainWindow Window, RepoTabViewModel Tab)> ShowAsync(TempRepo repo)
    {
        var main = AppHarness.CreateMain(new AppSettings { OpenRepositories = [repo.Path] });
        var window = new MainWindow { Width = 1280, Height = 800, DataContext = main };
        window.Show();
        await AppHarness.Until(() => main.Tabs.Count == 1 && main.Tabs[0].IsLoaded && main.Tabs[0].Staging.Loaded, "the tab to open");
        window.UpdateLayout();
        return (window, main.Tabs[0]);
    }

    private static TempRepo Repo()
    {
        var repo = TempRepo.Init();
        repo.Write("a.txt", "a\n");
        repo.Write("b.txt", "b\n");
        repo.Write("c.txt", "c\n");
        repo.CommitAll("initial");
        repo.Write("a.txt", "a2\n");
        repo.Write("b.txt", "b2\n");
        repo.Write("c.txt", "c2\n");
        return repo;
    }

    private static FileListView ListView(MainWindow window, FileListViewModel list) =>
        window.GetVisualDescendants().OfType<FileListView>().Single(v => v.DataContext == list);

    private static Control RowBorder(Control view, string path) =>
        view.GetVisualDescendants().OfType<Border>().Single(b => b.Classes.Contains("file-row") && b.DataContext is FileRow r && r.DisplayPath == path);

    private static Point Center(Control control, MainWindow window) =>
        control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), window)!.Value;

    /// <summary>Right-clicks <paramref name="row"/> and clicks the item of the menu it opened.</summary>
    private static async Task ClickMenuItemAsync(MainWindow window, Control row, string header)
    {
        var at = Center(row, window);
        window.MouseMove(at);
        window.MouseDown(at, MouseButton.Right);
        window.MouseUp(at, MouseButton.Right);
        var menu = row.ContextMenu ?? throw new Xunit.Sdk.XunitException("the row has no context menu");
        await AppHarness.Until(() => menu.IsOpen, "the context menu to open");
        var item = menu.Items.OfType<MenuItem>().Single(i => (string?)i.Header == header);
        item.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(MenuItem.ClickEvent));
        menu.Close();
    }

    [AvaloniaFact]
    public async Task Right_clicking_a_file_copies_its_path_or_name()
    {
        using var repo = TempRepo.Init();
        repo.Write("src/deep/One.cs", "1\n");
        repo.Write("src/deep/Two.cs", "2\n");
        repo.CommitAll("initial");
        repo.Write("src/deep/One.cs", "1b\n");
        var (window, tab) = await ShowAsync(repo);
        var platform = new FakePlatform();
        tab.MainShell.Platform = platform;
        try
        {
            var view = ListView(window, tab.Staging.Unstaged);
            await ClickMenuItemAsync(window, RowBorder(view, "src/deep/One.cs"), "Copy path");
            await AppHarness.Until(() => platform.Clipboard == "src/deep/One.cs", "the path to be copied");
            await ClickMenuItemAsync(window, RowBorder(view, "src/deep/One.cs"), "Copy file name");
            await AppHarness.Until(() => platform.Clipboard == "One.cs", "the file name to be copied");
            Assert.Null(tab.Diff.DisplayPath); // Copying does not select the file.

            // A commit's files, in the history.
            tab.LeftTab = LeftPanelTab.History;
            await AppHarness.Until(() => tab.History.Rows.Count == 1, "the history to load");
            tab.History.Select(tab.History.Rows[0]);
            await AppHarness.Until(() => tab.History.Details.Files.Count == 2, "the commit's files");
            window.UpdateLayout();
            var details = window.GetVisualDescendants().OfType<ChangeDetailsView>().Single(v => v.IsEffectivelyVisible);
            await ClickMenuItemAsync(window, RowBorder(details, "src/deep/Two.cs"), "Copy path");
            await AppHarness.Until(() => platform.Clipboard == "src/deep/Two.cs", "the commit file's path to be copied");
            await ClickMenuItemAsync(window, RowBorder(details, "src/deep/Two.cs"), "Copy file name");
            await AppHarness.Until(() => platform.Clipboard == "Two.cs", "the commit file's name to be copied");
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task The_row_arrow_stages_and_unstages_a_file()
    {
        using var repo = Repo();
        var (window, tab) = await ShowAsync(repo);
        try
        {
            var view = ListView(window, tab.Staging.Unstaged);
            var row = RowBorder(view, "b.txt");
            window.MouseDown(Center(row, window), MouseButton.Left);
            window.MouseUp(Center(row, window), MouseButton.Left);
            await AppHarness.Until(() => tab.Diff.Diff is { } d && d.Path.ToString() == "b.txt", "the diff of b.txt");
            window.UpdateLayout();
            var button = row.GetVisualDescendants().OfType<Button>().Single();
            var at = Center(button, window);
            window.MouseMove(at);
            window.MouseDown(at, MouseButton.Left);
            window.MouseUp(at, MouseButton.Left);
            await AppHarness.Until(() => tab.Staging.Staged.All.Any(r => r.DisplayPath == "b.txt"), "b.txt to be staged");
            window.UpdateLayout();

            button = RowBorder(view, "c.txt").GetVisualDescendants().OfType<Button>().Single();
            at = Center(button, window);
            window.MouseMove(at);
            window.MouseDown(at, MouseButton.Left);
            window.MouseUp(at, MouseButton.Left);
            await AppHarness.Until(() => tab.Staging.Staged.All.Any(r => r.DisplayPath == "c.txt"), "c.txt to be staged");
            window.UpdateLayout();

            var staged = ListView(window, tab.Staging.Staged);
            button = RowBorder(staged, "b.txt").GetVisualDescendants().OfType<Button>().Single();
            at = Center(button, window);
            window.MouseMove(at);
            window.MouseDown(at, MouseButton.Left);
            window.MouseUp(at, MouseButton.Left);
            await AppHarness.Until(() => tab.Staging.Staged.All.Count == 1, "b.txt to be unstaged");
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task A_row_arrow_clicked_while_the_previous_stage_runs_still_counts()
    {
        // Headless clicks finish the stage before MouseUp returns, so this drives the command as the
        // button does. On a real repository the stage and its refresh take long enough to click again.
        using var repo = Repo();
        using var app = await AppHarness.OpenAsync(repo);
        var command = app.Staging.Unstaged.RowActionCommand;
        var (a, b) = (app.Unstaged("a.txt"), app.Unstaged("b.txt"));
        command.Execute(a);
        Assert.True(command.IsRunning);
        Assert.True(command.CanExecute(b), "the button for b.txt is disabled while a.txt is staged");
        command.Execute(b);
        await AppHarness.Until(() => app.StagedPaths.Count == 2 && !command.IsRunning, "both files to be staged");
        Assert.Equal(["c.txt"], app.UnstagedPaths);
    }

    [AvaloniaFact]
    public async Task Arrow_keys_move_through_the_file_list()
    {
        using var repo = Repo();
        var (window, tab) = await ShowAsync(repo);
        try
        {
            var view = ListView(window, tab.Staging.Unstaged);
            var at = Center(RowBorder(view, "a.txt"), window);
            window.MouseDown(at, MouseButton.Left);
            window.MouseUp(at, MouseButton.Left);
            Assert.Equal("a.txt", tab.Staging.Unstaged.Rows.Single(r => r.IsCurrent).DisplayPath);

            window.KeyPressQwerty(PhysicalKey.ArrowDown, RawInputModifiers.None);
            Assert.Equal("b.txt", tab.Staging.Unstaged.Rows.Single(r => r.IsCurrent).DisplayPath);
            window.KeyPressQwerty(PhysicalKey.ArrowDown, RawInputModifiers.None);
            Assert.Equal("c.txt", tab.Staging.Unstaged.Rows.Single(r => r.IsCurrent).DisplayPath);
            window.KeyPressQwerty(PhysicalKey.ArrowUp, RawInputModifiers.None);
            Assert.Equal("b.txt", tab.Staging.Unstaged.Rows.Single(r => r.IsCurrent).DisplayPath);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task Arrow_keys_move_through_a_commits_files()
    {
        using var repo = Repo();
        repo.CommitAll("three files");
        var (window, tab) = await ShowAsync(repo);
        try
        {
            tab.LeftTab = LeftPanelTab.History;
            var history = tab.History;
            await AppHarness.Until(() => history.Rows.Count > 0, "the history to load");
            history.Select(history.Rows[0]);
            await AppHarness.Until(() => history.Details.Files.Count == 3, "the commit's files");
            window.UpdateLayout();

            var view = window.GetVisualDescendants().OfType<ChangeDetailsView>().Single(v => v.DataContext == history.Details);
            var at = Center(RowBorder(view, "a.txt"), window);
            window.MouseDown(at, MouseButton.Left);
            window.MouseUp(at, MouseButton.Left);
            Assert.Equal("a.txt", history.Details.Files.Single(f => f.IsCurrent).DisplayPath);

            window.KeyPressQwerty(PhysicalKey.ArrowDown, RawInputModifiers.None);
            Assert.Equal("b.txt", history.Details.Files.Single(f => f.IsCurrent).DisplayPath);
            window.KeyPressQwerty(PhysicalKey.ArrowUp, RawInputModifiers.None);
            Assert.Equal("a.txt", history.Details.Files.Single(f => f.IsCurrent).DisplayPath);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task Each_list_shows_its_row_arrow_and_shift_shows_discard()
    {
        // The rules once said UserControl.staged, which matches no FileListView (type selectors are
        // exact), so every icon stayed hidden and the button shrank to 6×6 px.
        using var repo = Repo();
        repo.Raw("add", "c.txt");
        var (window, tab) = await ShowAsync(repo);
        try
        {
            static List<string> Shown(MainWindow window, FileListViewModel list, string path)
            {
                var button = RowBorder(ListView(window, list), path).GetVisualDescendants().OfType<Button>().Single();
                Assert.True(button.Bounds.Width >= 18, $"button width {button.Bounds.Width}");
                return button.GetVisualDescendants().OfType<CheeseGit.App.Controls.Icon>().Where(i => i.IsVisible).SelectMany(i => i.Classes).ToList();
            }

            Assert.Equal(["stage"], Shown(window, tab.Staging.Unstaged, "a.txt"));
            Assert.Equal(["unstage"], Shown(window, tab.Staging.Staged, "c.txt"));
            tab.ShiftHeld = true;
            window.UpdateLayout();
            Assert.Equal(["discard"], Shown(window, tab.Staging.Unstaged, "a.txt"));
            Assert.Equal(["discard"], Shown(window, tab.Staging.Staged, "c.txt"));
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task A_shift_released_outside_the_window_ends_discard_mode_on_the_next_pointer_move()
    {
        using var repo = Repo();
        var (window, tab) = await ShowAsync(repo);
        try
        {
            window.KeyPressQwerty(PhysicalKey.ShiftLeft, RawInputModifiers.Shift);
            Assert.True(tab.ShiftHeld);
            // No key-up arrives: the snipping tool had focus by then.
            window.MouseMove(new Point(100, 300));
            Assert.False(tab.ShiftHeld);
            window.MouseMove(new Point(110, 300), RawInputModifiers.Shift);
            Assert.True(tab.ShiftHeld);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task An_empty_staged_list_offers_an_empty_commit()
    {
        using var repo = Repo();
        using var app = await AppHarness.OpenAsync(repo);
        Assert.True(app.Staging.Staged.ShowEmptyCommitLink);
        Assert.False(app.Staging.Unstaged.ShowEmptyCommitLink);
        app.Staging.Staged.EnableEmptyCommitCommand.Execute(null);
        Assert.True(app.Staging.EmptyCommitMode);
        Assert.False(app.Staging.Staged.ShowEmptyCommitLink);
        Assert.StartsWith("Make empty commit to", app.Staging.CommitButtonText);
    }

    [AvaloniaFact]
    public async Task The_more_actions_button_is_as_tall_as_the_commit_button()
    {
        using var repo = Repo();
        repo.Raw("add", "a.txt");
        var (window, tab) = await ShowAsync(repo);
        try
        {
            var commit = window.GetVisualDescendants().OfType<Button>().Single(b => b.Classes.Contains("commit"));
            var more = window.GetVisualDescendants().OfType<Button>().Single(b => b.Classes.Contains("more"));
            Assert.True(more.IsEffectivelyVisible);
            Assert.Equal(commit.Bounds.Height, more.Bounds.Height, tolerance: 0.5);
            Assert.Equal(commit.Bounds.Y, more.Bounds.Y, tolerance: 0.5);
        }
        finally
        {
            window.Close();
        }
    }
}
