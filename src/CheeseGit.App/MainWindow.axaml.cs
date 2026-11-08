using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Chrome;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Animation;
using Avalonia.Interactivity;
using Avalonia.Media.Transformation;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.Rendering.Composition;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CheeseGit.App.Services;
using CheeseGit.App.Theming;
using CheeseGit.App.ViewModels;

namespace CheeseGit.App;

public partial class MainWindow : Window, IPlatformServices
{
    private (RepoTabViewModel Tab, Point Start, bool Dragging)? tabDrag;

    public MainWindow()
    {
        this.InitializeComponent();
        // A snapshot run opens on the desktop of someone who is working: activated, it took the keyboard
        // focus and their typing landed in its text boxes (and in the image).
        if (Program.SnapshotPath is not null) this.ShowActivated = false;
        this.AddHandler(KeyDownEvent, this.OnAnyKey, RoutingStrategies.Tunnel, handledEventsToo: true);
        this.AddHandler(KeyUpEvent, this.OnAnyKey, RoutingStrategies.Tunnel, handledEventsToo: true);
        this.Activated += (_, _) => this.ViewModel?.SelectedTab?.RefreshOnFocus();
        this.Deactivated += (_, _) => this.SetShift(false);
        // A Shift released outside the window (Win+Shift+S, then the snipping tool) left discard mode
        // on. Pointer events carry the real modifier state, so the next move or click corrects it.
        this.AddHandler(PointerMovedEvent, this.OnAnyPointer, RoutingStrategies.Tunnel, handledEventsToo: true);
        this.AddHandler(PointerPressedEvent, this.OnAnyPointer, RoutingStrategies.Tunnel, handledEventsToo: true);
        this.Opened += async (_, _) => await this.OnOpenedAsync();
        // On Linux, X11 (_NET_WM_MOVERESIZE) and Wayland (xdg_toplevel.move) hand every press on a TitleBar-role
        // element to the window manager before the app sees it, so a double click never arrived. There the
        // title bar handles its presses itself. Windows (HTCAPTION) and macOS maximize on a double click natively.
        if (OperatingSystem.IsLinux() && this.FindControl<Border>("TitleBarDrag") is { } titleBar)
        {
            WindowDecorationProperties.SetElementRole(titleBar, WindowDecorationsElementRole.None);
            titleBar.PointerPressed += this.OnTitleBarPressed;
        }
        this.RestorePlacement();
    }

    // Last normal bounds: maximizing reports the screen's size, which must not become the restored size.
    private Size normalSize;
    private PixelPoint? normalPosition;

    private void RestorePlacement()
    {
        this.normalSize = new Size(this.Width, this.Height);
        if (Program.Store.Settings.Window is { Width: > 0, Height: > 0 } saved)
        {
            var screens = this.Screens.All.Select(s => (s.WorkingArea, s.Scaling)).ToList();
            var (size, position) = WindowPlacement.Fit(saved, screens);
            this.Width = Math.Max(size.Width, this.MinWidth);
            this.Height = Math.Max(size.Height, this.MinHeight);
            this.normalSize = new Size(this.Width, this.Height);
            if (position is { } p)
            {
                this.WindowStartupLocation = WindowStartupLocation.Manual;
                this.Position = p;
                this.normalPosition = p;
            }
            else
            {
                this.WindowStartupLocation = WindowStartupLocation.CenterScreen;
            }
            if (saved.Maximized) this.WindowState = WindowState.Maximized;
        }
        this.Resized += (_, e) =>
        {
            if (this.WindowState == WindowState.Normal) this.normalSize = e.ClientSize;
        };
        this.PositionChanged += (_, e) =>
        {
            if (this.WindowState == WindowState.Normal) this.normalPosition = e.Point;
        };
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        base.OnClosing(e);
        if (e.Cancel || Program.SnapshotPath is not null) return;
        // Wayland does not tell clients where their windows are: Position stays (0, 0) there.
        var knowsPosition = this.normalPosition is { } p && p != default;
        Program.Store.Settings.Window = new WindowPlacement
        {
            Width = this.normalSize.Width,
            Height = this.normalSize.Height,
            X = knowsPosition ? this.normalPosition!.Value.X : null,
            Y = knowsPosition ? this.normalPosition!.Value.Y : null,
            Maximized = this.WindowState == WindowState.Maximized,
        };
        Program.Store.Save();
    }

    private MainViewModel? ViewModel => this.DataContext as MainViewModel;

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (this.ViewModel is not { } vm) return;
        vm.Platform = this;
        this.BuildThemeMenu(vm);
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MainViewModel.Dialog) && vm.Dialog is not null) Dispatcher.UIThread.Post(this.FocusDialog, DispatcherPriority.Loaded);
        };
    }

    /// <summary>A new dialog takes the keyboard: its first text box, else the dialog itself (so Escape and Enter reach it).</summary>
    private void FocusDialog()
    {
        var first = this.DialogHost.GetVisualDescendants().OfType<TextBox>().FirstOrDefault(t => t.IsEffectivelyVisible && t.IsEnabled);
        if (first is not null) first.Focus();
    }

    private async Task OnOpenedAsync()
    {
        if (this.ViewModel is not { } vm) return;
        await vm.RestoreAsync(Program.RepoArgs);
        vm.Updater.ShowWhatsNewIfUpdated(vm.AppVersion);
        if (Program.SnapshotPath is not { } path)
        {
            vm.CheckDesktopEntry();
            await vm.Updater.CheckOnStartAsync();
            return;
        }

        if (Program.SnapshotHistory is { } history && vm.SelectedTab is { } historyTab)
        {
            historyTab.LeftTab = LeftPanelTab.History;
            if (Program.SnapshotSearch is { } query && Program.SnapshotDialog != "cherry-pick")
            {
                await WaitAsync(() => historyTab.History.Rows.Count > 0);
                historyTab.History.SearchQuery = query;
                await WaitAsync(() => historyTab.History.ShownQuery == query);
            }
            if (int.TryParse(history, out var row) && row >= 0)
            {
                var h = historyTab.History;
                await WaitAsync(() => h.DisplayRows.Count > row);
                if (h.DisplayRows.Count > row)
                {
                    h.Select(h.DisplayRows[row]);
                    this.GetVisualDescendants().OfType<Views.HistoryListView>().FirstOrDefault(v => v.IsEffectivelyVisible)?.ScrollTo(h.DisplayRows[row]);
                    await WaitAsync(() => h.Details.Files.Count > 0);
                    var fileRow = Program.SnapshotFile is { } name ? h.Details.Files.FirstOrDefault(f => f.DisplayPath == name) : h.Details.Files.FirstOrDefault();
                    if (fileRow is not null) h.Details.SelectFile(fileRow);
                }
            }
        }
        else if (Program.SnapshotStash is { } stashRow && vm.SelectedTab is { } stashTab)
        {
            stashTab.LeftTab = LeftPanelTab.Stash;
            var st = stashTab.Stash;
            if (int.TryParse(stashRow, out var index) && index >= 0)
            {
                await WaitAsync(() => st.Rows.Count > index);
                if (st.Rows.Count > index)
                {
                    st.Select(st.Rows[index]);
                    await WaitAsync(() => st.Details.Files.Count > 0);
                    var fileRow = Program.SnapshotFile is { } name ? st.Details.Files.FirstOrDefault(f => f.DisplayPath == name) : st.Details.Files.FirstOrDefault();
                    if (fileRow is not null) st.Details.SelectFile(fileRow);
                }
            }
        }
        else if (Program.SnapshotFile is { } file && vm.SelectedTab is { } tab)
        {
            var staging = tab.Staging;
            var row = staging.Unstaged.All.FirstOrDefault(r => r.DisplayPath == file) ?? staging.Staged.All.FirstOrDefault(r => r.DisplayPath == file);
            if (row is not null) (row.Area == Core.Repository.DiffArea.Staged ? staging.Staged : staging.Unstaged).Click(row, false, false);
        }
        if (Program.SnapshotTheme is { } theme) vm.Themes.Select(theme);
        if (Program.SnapshotCommandLog) vm.CommandLog.IsOpen = true;
        if (Program.SnapshotSplit) vm.SetDiffViewMode(Services.DiffViewMode.Split);
        switch (Program.SnapshotDialog)
        {
            case "settings": vm.ShowSettingsCommand.Execute(null); break;
            case "update" or "update-notice": await vm.Updater.CheckNowCommand.ExecuteAsync(null); break;
            case "update-on-exit":
                // Check, download, and hand the update to Velopack, which applies it after this run exits.
                await vm.Updater.CheckNowCommand.ExecuteAsync(null);
                await vm.Updater.UpdateOnExitCommand.ExecuteAsync(null);
                break;
            case "whats-new":
                vm.ShowWhatsNew("1.1.0", "## Highlights\n\n- **Updates** install from inside the app: *Update now*, *On exit* or *Skip*.\n- The Windows build ships as `Setup.exe` and a portable zip.\n\n### Fixes\n\n- File rows show the stage and unstage arrows.\n\nSee [the release page](https://github.com/LPeter1997/CheeseGit/releases) for details.");
                break;
            case "about": vm.ShowAboutCommand.Execute(null); break;
            case "report": await vm.ReportProblemCommand.ExecuteAsync(null); break;
            case "create": vm.ShowCreateRepositoryCommand.Execute(null); break;
            case "clone": vm.ShowCloneRepositoryCommand.Execute(null); break;
            case "branches":
                await WaitAsync(() => vm.SelectedTab?.History.Branches.Count > 0 || this.GetVisualDescendants().OfType<Views.RepoView>().Any());
                this.GetVisualDescendants().OfType<Views.RepoView>().FirstOrDefault(v => v.IsEffectivelyVisible)?.OpenBranchDropdown();
                if (vm.SelectedTab?.BranchList is { } branchList)
                {
                    await WaitAsync(() => branchList.IsLoaded);
                    if (Program.SnapshotSearch is { } query) branchList.Query = query;
                }
                break;
            case "delete-branch" when vm.SelectedTab?.BranchList is { } list:
                await list.LoadAsync();
                if (list.Items.OfType<ViewModels.LocalBranchRow>().FirstOrDefault(b => !b.IsCurrent) is { } branch) branch.DeleteCommand.Execute(null);
                break;
            case "add-remote" when vm.SelectedTab?.Remote is { } remote:
                remote.AddRemoteCommand.Execute(null);
                break;
            case "conflicts" when vm.SelectedTab is { } conflictTab:
                await WaitAsync(() => conflictTab.CanResolveOperation);
                _ = conflictTab.ResolveConflictsCommand.ExecuteAsync(null);
                break;
            case "cherry-pick" when vm.SelectedTab is { } pickTab:
                // With CHEESEGIT_SNAPSHOT_HISTORY=<row>: that row and the one below it are selected.
                var picks = pickTab.History;
                pickTab.LeftTab = LeftPanelTab.History;
                await WaitAsync(() => picks.DisplayRows.Count > 1);
                if (picks.Selected is null) picks.Select(picks.DisplayRows[0]);
                var below = Math.Min(picks.Selected!.Row + 1, picks.DisplayRows.Count - 1);
                picks.Click(picks.DisplayRows[below], ctrl: true, shift: false);
                _ = picks.CherryPickCommand.ExecuteAsync(null);
                if (Program.SnapshotSearch is null) break;
                await WaitAsync(() => vm.Dialog is CherryPickDialogViewModel { Loading: false });
                if (vm.Dialog is CherryPickDialogViewModel picker) picker.Query = Program.SnapshotSearch;
                break;
            case "error-copied":
                vm.ShowError("fatal: unable to access 'https://example.com/repo.git/': Could not resolve host: example.com");
                vm.Alerts[^1].ShowCopiedSample();
                break;
            case "blocking" when vm.SelectedTab is { } blockedTab:
                blockedTab.ShowSampleBlocking();
                break;
            case "remote-progress" when vm.SelectedTab?.Remote is { } remote:
                remote.ShowSampleProgress();
                break;
            case "remotes":
                this.GetVisualDescendants().OfType<Views.RepoView>().FirstOrDefault(v => v.IsEffectivelyVisible)?.OpenRemotePicker();
                break;
            case "askpass":
                _ = vm.AskAsync(new Core.Git.AskpassRequest("Password for 'https://alice@github.com': ", Core.Git.AskpassKind.Secret), CancellationToken.None);
                break;
            case "askpass-host":
                _ = vm.AskAsync(new Core.Git.AskpassRequest(
                    "The authenticity of host 'example.com (192.0.2.10)' can't be established.\nED25519 key fingerprint is SHA256:EXAMPLEexampleEXAMPLEexampleEXAMPLEexample00.\nThis key is not known by any other names.\nAre you sure you want to continue connecting (yes/no/[fingerprint])? ",
                    Core.Git.AskpassKind.Confirm), CancellationToken.None);
                break;
            case "switch-changes":
                _ = vm.ShowDialogAsync(new SwitchChangesDialogViewModel(vm.SelectedTab?.BranchText ?? "main", "feature/login"));
                break;
            case "confirm": _ = vm.ConfirmAsync("Discard all changes to 3 files? This cannot be undone.", "Discard"); break;
        }
        // Let the diff load, layout and the theme animation catch up first.
        DispatcherTimer.RunOnce(async () =>
        {
            if (Program.SnapshotAnonymize) await this.SaveAnonymizedSnapshotAsync(path, Services.ReportAnonymizer.Create(vm));
            else await this.SaveSnapshotAsync(path);
            this.Close();
        }, TimeSpan.FromMilliseconds(900));
    }

    private static async Task WaitAsync(Func<bool> condition)
    {
        for (var i = 0; i < 300 && !condition(); i++) await Task.Delay(20);
    }

    /// <summary>
    /// Renders the window itself (no desktop capture: the user works on this
    /// machine) through the compositor, like the screen. RenderTargetBitmap's
    /// immediate renderer mishandled clips in custom-drawn controls (text
    /// vanished or was drawn scaled) where the window drew them correctly.
    /// </summary>
    public async Task SaveSnapshotAsync(string path)
    {
        using var bitmap = await this.RenderAsync(this.SnapshotRoot);
        bitmap.Save(path, PngBitmapEncoderOptions.Default);
        Console.WriteLine($"snapshot {path}, first frame after {Program.SinceStart.ElapsedMilliseconds} ms");
    }

    // The layer manager, not the window: the window visual includes the
    // decoration margin. The layer manager holds the content plus the
    // overlay layer, where snapshot mode puts popups (see Program), and the adorner layer.
    private Visual SnapshotRoot =>
        this.GetVisualDescendants().OfType<Avalonia.Controls.Primitives.VisualLayerManager>().FirstOrDefault() ?? (Visual)this.Content!;

    private Task<Avalonia.Media.Imaging.Bitmap> RenderAsync(Visual root)
    {
        var visual = ElementComposition.GetElementVisual(root)!;
        return visual.Compositor.CreateCompositionVisualSnapshot(visual, this.RenderScaling);
    }

    /// <summary>
    /// A problem report's screenshot: the window anonymized for one frame
    /// (<see cref="Services.ReportCapture"/>), rendered, then given back.
    /// </summary>
    public async Task SaveAnonymizedSnapshotAsync(string path, Core.Diagnostics.Anonymizer anonymizer)
    {
        await File.WriteAllBytesAsync(path, await this.CaptureWindowAsync(anonymizer) ?? throw new InvalidOperationException("No screenshot was taken."));
        Console.WriteLine($"anonymized snapshot {path}");
    }

    public async Task<byte[]?> CaptureWindowAsync(Core.Diagnostics.Anonymizer? anonymizer)
    {
        var root = this.SnapshotRoot;
        if (anonymizer is null) return Png(await this.RenderAsync(root));
        using var capture = Services.ReportCapture.Apply(root, anonymizer);
        for (var attempt = 1; attempt <= 5; attempt++)
        {
            this.UpdateLayout();
            await ElementComposition.GetElementVisual(root)!.Compositor.RequestCommitAsync();
            var bitmap = await this.RenderAsync(root);
            // Whatever showed up while the frame was drawn may be in it as it is: cover it and draw again.
            if (!capture.Extend()) return Png(bitmap);
            bitmap.Dispose();
        }
        return null; // The window kept changing; better no screenshot than a leaking one.
    }

    private static byte[] Png(Avalonia.Media.Imaging.Bitmap bitmap)
    {
        using (bitmap)
        {
            using var stream = new MemoryStream();
            bitmap.Save(stream, PngBitmapEncoderOptions.Default);
            return stream.ToArray();
        }
    }

    // ── Shift = discard mode ──────────────────────────────────────────

    private void OnAnyKey(object? sender, KeyEventArgs e)
    {
        this.SetShift((e.KeyModifiers & KeyModifiers.Shift) != 0 || (e.Key is Key.LeftShift or Key.RightShift && e.RoutedEvent == KeyDownEvent));
        if (e.RoutedEvent != KeyDownEvent || e.Handled) return;
        var ctrl = (e.KeyModifiers & (KeyModifiers.Control | KeyModifiers.Meta)) != 0;
        var blocked = this.ViewModel?.SelectedTab?.IsBlocked == true; // Mid-switch: the repository view takes no input.
        if (blocked && ctrl && e.Key is Key.F or Key.Enter)
        {
            e.Handled = true;
        }
        else if (ctrl && e.Key == Key.F && this.ViewModel?.SelectedTab?.ActiveDiff is { CanSearch: true } diff)
        {
            diff.FocusSearchCommand.Execute(null);
            e.Handled = true;
        }
        else if (ctrl && e.Key == Key.Enter && this.ViewModel?.SelectedTab?.Staging is { } staging && staging.CommitCommand.CanExecute(null))
        {
            staging.CommitCommand.Execute(null);
            e.Handled = true;
        }
        else if (e.Key == Key.Escape && this.ViewModel?.Dialog is { CanClose: true } dialog)
        {
            dialog.Cancel();
            e.Handled = true;
        }
    }

    private void OnAnyPointer(object? sender, PointerEventArgs e) => this.SetShift((e.KeyModifiers & KeyModifiers.Shift) != 0);

    private void SetShift(bool held)
    {
        if (this.ViewModel?.SelectedTab is { } tab && tab.ShiftHeld != held) tab.ShiftHeld = held;
    }

    // ── Title bar ─────────────────────────────────────────────────────

    private void BuildThemeMenu(MainViewModel vm)
    {
        var menu = this.FindControl<MenuItem>("ThemeMenu")!;
        var items = new List<MenuItem>();
        foreach (var choice in ThemeManager.Choices)
        {
            var item = new MenuItem { Header = choice, Command = vm.SelectThemeCommand, CommandParameter = choice };
            // Hovering a theme previews it (old app); leaving the menu goes back.
            item.PointerEntered += (_, _) => vm.PreviewTheme(choice);
            items.Add(item);
        }
        menu.ItemsSource = items;
        menu.SubmenuOpened += (_, _) => Mark();
        menu.PointerExited += (_, _) => vm.PreviewTheme(null);
        if (this.FindControl<Button>("OptionsButton")?.Flyout is { } flyout) flyout.Closed += (_, _) => vm.PreviewTheme(null);
        vm.Themes.Changed += Mark;
        Mark();

        void Mark()
        {
            foreach (var item in items) item.Icon = (string)item.Header! == vm.Themes.Choice ? new TextBlock { Text = "✓", Classes = { "accent" } } : null;
        }
    }

    private void OnTitleBarPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        e.Handled = true;
        // Presses are counted even when the window manager took the release of the first one (the move).
        if (e.ClickCount == 2) this.OnMaximize(sender, e);
        else this.BeginMoveDrag(e);
    }

    private void OnMinimize(object? sender, RoutedEventArgs e) => this.WindowState = WindowState.Minimized;

    private void OnMaximize(object? sender, RoutedEventArgs e) =>
        this.WindowState = this.WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    private void OnClose(object? sender, RoutedEventArgs e) => this.Close();

    // Tabs: click selects; dragging sideways reorders, like the old app's dnd-kit sortable
    // (5 px threshold): the dragged tab follows the pointer, the tabs it passes slide aside,
    // and the collection only changes on release. Moving the item on every pointer move
    // made the tab jump a whole slot at a time with no animation.
    private static readonly TimeSpan TabSlide = TimeSpan.FromMilliseconds(180);

    private void OnTabPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Control { DataContext: RepoTabViewModel tab } control || !e.GetCurrentPoint(control).Properties.IsLeftButtonPressed) return;
        if (e.Source is Visual source && source.FindAncestorOfType<Button>(includeSelf: true) is not null) return;
        this.ViewModel!.SelectedTab = tab;
        this.tabDrag = (tab, e.GetPosition(this), false);
        e.Pointer.Capture(control);
    }

    private void OnTabMoved(object? sender, PointerEventArgs e)
    {
        if (this.tabDrag is not { } drag || this.ViewModel is not { } vm) return;
        var p = e.GetPosition(this);
        if (!drag.Dragging && Math.Abs(p.X - drag.Start.X) < 5) return;
        var strip = this.TabStrip;
        var from = vm.Tabs.IndexOf(drag.Tab);
        if (from < 0) return;
        if (!drag.Dragging)
        {
            this.tabDrag = drag with { Dragging = true };
            for (var i = 0; i < vm.Tabs.Count; i++)
            {
                if (strip.ContainerFromIndex(i) is not Control c) continue;
                c.Transitions = i == from ? null : SlideTransitions();
                c.ZIndex = i == from ? 1 : 0;
            }
            if (strip.ContainerFromIndex(from) is Control dragged) dragged.Opacity = 0.85;
        }

        var (dx, target, width) = this.TabDragSlot(p.X - drag.Start.X, from);
        for (var i = 0; i < vm.Tabs.Count; i++)
        {
            if (strip.ContainerFromIndex(i) is not Control c) continue;
            var shift = i == from ? dx
                : from < i && i <= target ? -width
                : target <= i && i < from ? width
                : 0;
            c.RenderTransform = Translate(shift);
        }
    }

    private void OnTabReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (this.tabDrag is { Dragging: true } drag && this.ViewModel is { } vm && vm.Tabs.IndexOf(drag.Tab) is var from and >= 0)
        {
            var (dx, target, width) = this.TabDragSlot(e.GetPosition(this).X - drag.Start.X, from);
            this.ResetTabTransforms();
            vm.MoveTab(from, target);
            // Settle the dropped tab from where it was let go into its new slot.
            this.TabStrip.UpdateLayout();
            if (this.TabStrip.ContainerFromItem(drag.Tab) is Control dropped)
            {
                dropped.RenderTransform = Translate(dx - (target - from) * width);
                dropped.ZIndex = 1;
                dropped.Transitions = SlideTransitions();
                dropped.RenderTransform = Translate(0);
                DispatcherTimer.RunOnce(() => dropped.ZIndex = 0, TabSlide);
            }
        }
        this.tabDrag = null;
        e.Pointer.Capture(null);
    }

    private void OnTabCaptureLost(object? sender, PointerCaptureLostEventArgs e)
    {
        if (this.tabDrag is not { Dragging: true }) return;
        this.tabDrag = null;
        this.ResetTabTransforms();
    }

    /// <summary>The dragged tab's offset (kept inside the strip), the slot it is over and the tab width.</summary>
    private (double Dx, int Target, double Width) TabDragSlot(double dx, int from)
    {
        var count = this.ViewModel!.Tabs.Count;
        var width = (this.TabStrip.ItemsPanelRoot as Controls.TabStripPanel)?.ActualTabWidth ?? 176;
        dx = Math.Clamp(dx, -from * width, (count - 1 - from) * width);
        var target = Math.Clamp((int)Math.Round(from + dx / width), 0, count - 1);
        return (dx, target, width);
    }

    private void ResetTabTransforms()
    {
        for (var i = 0; i < this.TabStrip.ItemCount; i++)
        {
            if (this.TabStrip.ContainerFromIndex(i) is not Control c) continue;
            c.Transitions = null;
            c.RenderTransform = null;
            c.ZIndex = 0;
            c.Opacity = 1;
        }
    }

    private static Transitions SlideTransitions() =>
    [
        new TransformOperationsTransition
        {
            Property = Visual.RenderTransformProperty,
            Duration = TabSlide,
            Easing = new Avalonia.Animation.Easings.CubicEaseOut(),
        },
    ];

    private static TransformOperations Translate(double x)
    {
        var builder = TransformOperations.CreateBuilder(1);
        builder.AppendTranslate(x, 0);
        return builder.Build();
    }

    private void OnBackdropPressed(object? sender, PointerPressedEventArgs e)
    {
        // Only a click on the backdrop itself, not inside the dialog.
        if (e.Source == sender && this.ViewModel?.Dialog is { CanClose: true } dialog) dialog.Cancel();
    }

    protected override void OnClosed(EventArgs e)
    {
        this.ViewModel?.Dispose();
        base.OnClosed(e);
    }

    // ── IPlatformServices ─────────────────────────────────────────────

    public async Task<string?> PickFolderAsync(string title)
    {
        var folders = await this.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = title, AllowMultiple = false });
        return folders.Count > 0 ? folders[0].TryGetLocalPath() : null;
    }

    public async Task SetClipboardTextAsync(string text)
    {
        if (this.Clipboard is { } clipboard) await clipboard.SetTextAsync(text);
    }

    public async Task OpenUrlAsync(string url)
    {
        if (Uri.TryCreate(url, UriKind.Absolute, out var uri)) await this.Launcher.LaunchUriAsync(uri);
    }

    public async Task<bool> SaveFileAsync(string title, string suggestedName, byte[] content)
    {
        var file = await this.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions { Title = title, SuggestedFileName = suggestedName });
        if (file is null) return false;
        await using var stream = await file.OpenWriteAsync();
        await stream.WriteAsync(content);
        return true;
    }

    public async Task SaveTextFileAsync(string title, string suggestedName, string text)
    {
        var file = await this.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions { Title = title, SuggestedFileName = suggestedName });
        if (file is null) return;
        await using var stream = await file.OpenWriteAsync();
        await using var writer = new StreamWriter(stream);
        await writer.WriteAsync(text);
    }
}
