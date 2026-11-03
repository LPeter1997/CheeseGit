using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using CheeseGit.App.Controls;
using CheeseGit.App.ViewModels;

namespace CheeseGit.App.Views;

public partial class RepoView : UserControl
{
    private const double MinPanelWidth = 240;
    private const double MaxPanelWidth = 900;

    /// <summary>Room the history keeps for commit text beside the graph (old app: MIN_TEXT_WIDTH).</summary>
    private const double MinHistoryTextWidth = 320;

    /// <summary>The old app widened the panel for the graph up to this, no further.</summary>
    private const double MaxAutoWidth = 600;

    private static readonly TimeSpan WidthAnimation = TimeSpan.FromMilliseconds(200);

    private DiffViewModel? diff;
    private RepoTabViewModel? tab;
    private HistoryViewModel? history;

    /// <summary>The width the user chose with the splitter; the history may show the panel wider.</summary>
    private double userWidth = 340;

    private int animation;

    public RepoView()
    {
        this.InitializeComponent();
        this.PanelSplitter.DragCompleted += (_, _) => this.SaveLayout();
        // The overlay stops the pointer; keys still reach whatever had focus (the history list, the commit message).
        this.AddHandler(KeyDownEvent, this.OnKeyWhileBlocked, RoutingStrategies.Tunnel);
        this.AddHandler(TextInputEvent, this.OnKeyWhileBlocked, RoutingStrategies.Tunnel);
    }

    private void OnKeyWhileBlocked(object? sender, RoutedEventArgs e)
    {
        if (this.Tab?.IsBlocked == true) e.Handled = true;
    }

    private RepoTabViewModel? Tab => this.DataContext as RepoTabViewModel;

    private MainViewModel? Main => this.Tab?.Shell as MainViewModel;

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (this.diff is not null) this.diff.PropertyChanged -= this.OnDiffPropertyChanged;
        this.diff = this.Tab?.Diff;
        if (this.diff is not null) this.diff.PropertyChanged += this.OnDiffPropertyChanged;

        if (this.tab is not null) this.tab.PropertyChanged -= this.OnTabPropertyChanged;
        if (this.history is not null) this.history.PropertyChanged -= this.OnHistoryPropertyChanged;
        this.tab = this.Tab;
        this.history = this.tab?.History;
        if (this.tab is not null) this.tab.PropertyChanged += this.OnTabPropertyChanged;
        if (this.history is not null) this.history.PropertyChanged += this.OnHistoryPropertyChanged;

        if (this.Main is { } main) this.userWidth = Math.Clamp(main.Settings.LeftPanelWidth, MinPanelWidth, MaxPanelWidth);
        this.UpdatePanelWidth(animate: false);
    }

    private void OnTabPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(RepoTabViewModel.IsHistoryTab)) this.UpdatePanelWidth(animate: true);
    }

    private void OnHistoryPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(HistoryViewModel.View)) this.UpdatePanelWidth(animate: true);
    }

    /// <summary>
    /// The history needs the graph plus room for the commit text, so on that tab the panel grows
    /// (animated, like the old app's width transition) and the splitter cannot make it narrower.
    /// Other tabs go back to the width the user chose. The graph's full lane count is used, not
    /// the lanes on screen, so scrolling never resizes the panel.
    /// </summary>
    private void UpdatePanelWidth(bool animate)
    {
        var column = this.Layout.ColumnDefinitions[0];
        var min = MinPanelWidth;
        if (this.tab is { IsHistoryTab: true } && this.history?.View is { } view)
            min = Math.Clamp(GraphCell.ColumnWidth(view.Layout.LaneCount) + MinHistoryTextWidth, MinPanelWidth, MaxAutoWidth);
        var target = Math.Max(this.userWidth, min);
        var from = column.ActualWidth;

        column.MinWidth = MinPanelWidth;
        var id = ++this.animation;
        if (!animate || from <= 0 || Math.Abs(from - target) < 1 || TopLevel.GetTopLevel(this) is not { } top)
        {
            column.Width = new GridLength(target);
            column.MinWidth = min;
            return;
        }

        var start = TimeSpan.MinValue;
        top.RequestAnimationFrame(Step);

        void Step(TimeSpan now)
        {
            if (id != this.animation) return;
            if (start == TimeSpan.MinValue) start = now;
            var t = Math.Clamp((now - start) / WidthAnimation, 0, 1);
            var eased = 1 - Math.Pow(1 - t, 3);
            column.Width = new GridLength(from + (target - from) * eased);
            if (t < 1) top.RequestAnimationFrame(Step);
            else column.MinWidth = min;
        }
    }

    private void SaveLayout()
    {
        ++this.animation;
        this.userWidth = this.Layout.ColumnDefinitions[0].ActualWidth;
        if (this.Main is not { } main) return;
        main.Settings.LeftPanelWidth = this.userWidth;
        main.Store.Save();
    }

    private void OnDiffPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(DiffViewModel.FocusSearchRequest))
        {
            this.SearchBox.Focus();
            this.SearchBox.SelectAll();
        }
    }

    private async void OnLinesActivated(object? sender, LinesActivatedEventArgs e)
    {
        if (this.diff is { } diff) await diff.ApplyLinesAsync(e.Selections, e.Discard);
    }

    /// <summary>Opens the branch dropdown (snapshots). The eye toggles need the graph, so the history loads too.</summary>
    public void OpenRemotePicker() => this.RemotePickerButton.Flyout?.ShowAt(this.RemotePickerButton);

    public void OpenBranchDropdown()
    {
        this.Tab?.History.EnsureLoaded();
        this.BranchButton.Flyout?.ShowAt(this.BranchButton);
    }

    /// <summary>The merge button's tooltip predicts conflicts once it is about to show (not for every row listed).</summary>
    private void OnMergeToolTipOpening(object? sender, CancelRoutedEventArgs e)
    {
        if (sender is Control { DataContext: MergeableBranchRow row }) _ = row.PredictAsync();
    }

    private void OnBranchButtonClick(object? sender, RoutedEventArgs e) => this.Tab?.History.EnsureLoaded();

    private BranchListViewModel? branchList;

    private void OnBranchFlyoutOpened(object? sender, EventArgs e)
    {
        if (this.Tab?.BranchList is not { } list) return;
        this.branchList = list;
        list.CloseRequested += this.CloseBranchDropdown;
        list.IsOpen = true;
        Avalonia.Threading.Dispatcher.UIThread.Post(() => this.BranchSearch.Focus(), Avalonia.Threading.DispatcherPriority.Input);
    }

    private void OnBranchFlyoutClosed(object? sender, EventArgs e)
    {
        if (this.branchList is not { } list) return;
        list.CloseRequested -= this.CloseBranchDropdown;
        list.IsOpen = false;
        this.branchList = null;
    }

    private void CloseBranchDropdown() => this.BranchButton.Flyout?.Hide();

    private RemoteViewModel? remote;

    private void OnRemoteFlyoutOpened(object? sender, EventArgs e)
    {
        if (this.Tab?.Remote is not { } r) return;
        this.remote = r;
        r.PickerCloseRequested += this.CloseRemotePicker;
    }

    private void OnRemoteFlyoutClosed(object? sender, EventArgs e)
    {
        if (this.remote is { } r) r.PickerCloseRequested -= this.CloseRemotePicker;
        this.remote = null;
    }

    private void CloseRemotePicker() => this.RemotePickerButton.Flyout?.Hide();

    private void OnBranchSearchKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && this.branchList is { } list)
        {
            list.SubmitCommand.Execute(null);
            e.Handled = true;
        }
    }

    private void OnSearchKeyDown(object? sender, KeyEventArgs e)
    {
        if (this.diff is not { } diff) return;
        switch (e.Key)
        {
            case Key.Enter when (e.KeyModifiers & KeyModifiers.Shift) != 0:
                diff.PreviousMatchCommand.Execute(null);
                e.Handled = true;
                break;
            case Key.Enter:
                diff.NextMatchCommand.Execute(null);
                e.Handled = true;
                break;
            case Key.Escape:
                diff.SearchQuery = "";
                this.DiffView.Focus();
                e.Handled = true;
                break;
        }
    }
}
