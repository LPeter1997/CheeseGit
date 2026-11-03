using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CheeseGit.App.ViewModels;

namespace CheeseGit.App.Views;

public partial class HistoryListView : UserControl
{
    private const double RowHeight = 50;

    private HistoryViewModel? history;
    private ScrollViewer? scroller;

    public HistoryListView()
    {
        this.InitializeComponent();
        // Tunnel: the ListBox handles Up/Down itself (moving its own selection, which these lists bypass) before a bubbling handler would see them.
        this.List.AddHandler(KeyDownEvent, this.OnListKeyDown, RoutingStrategies.Tunnel);
        this.List.TemplateApplied += (_, e) =>
        {
            this.scroller = e.NameScope.Find<ScrollViewer>("PART_ScrollViewer");
            if (this.scroller is not null) this.scroller.ScrollChanged += (_, _) => this.ReportVisibleRange();
        };
        // The pills float over the list: room below the last row lets it scroll clear of them.
        this.Floating.SizeChanged += (_, e) => this.List.Padding = new Avalonia.Thickness(0, 0, 0, e.NewSize.Height + 16);
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (this.history is not null) this.history.PropertyChanged -= this.OnHistoryPropertyChanged;
        this.history = this.DataContext as HistoryViewModel;
        if (this.history is not null) this.history.PropertyChanged += this.OnHistoryPropertyChanged;
    }

    private void OnHistoryPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        // New rows: the graph column follows the rows on screen once they are laid out.
        if (e.PropertyName == nameof(HistoryViewModel.DisplayRows)) Dispatcher.UIThread.Post(this.ReportVisibleRange, DispatcherPriority.Background);
    }

    /// <summary>The graph column is as wide as the widest lane among the rows on screen (Phase 0: reserving the maximum wasted half the panel).</summary>
    private void ReportVisibleRange()
    {
        if (this.history is null || this.scroller is null) return;
        var first = (int)(this.scroller.Offset.Y / RowHeight);
        var last = (int)((this.scroller.Offset.Y + this.scroller.Viewport.Height) / RowHeight);
        this.history.SetVisibleRange(first, last);
    }

    /// <summary>
    /// Selection is the view model's, so a reload can keep it by commit; the
    /// ListBox's own selection is bypassed. Clicks on the row's buttons and
    /// the graph dot (checkout) are theirs.
    /// </summary>
    private void OnRowPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Control { DataContext: CommitRow row } control || this.history is null) return;
        if (!e.GetCurrentPoint(control).Properties.IsLeftButtonPressed || e.Handled) return;
        if (e.Source is Avalonia.Visual source && source.FindAncestorOfType<Button>(includeSelf: true) is not null) return;
        // Ctrl on Windows/Linux, ⌘ on macOS.
        var ctrl = (e.KeyModifiers & (KeyModifiers.Control | KeyModifiers.Meta)) != 0;
        this.history.Click(row, ctrl, shift: (e.KeyModifiers & KeyModifiers.Shift) != 0);
        this.List.Focus();
        e.Handled = true;
    }

    private void OnSearchKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && this.history is { IsSearching: true } history)
        {
            history.SearchQuery = "";
            e.Handled = true;
        }
    }

    private void OnListKeyDown(object? sender, KeyEventArgs e)
    {
        if (this.history is null) return;
        var delta = e.Key switch
        {
            Key.Up => -1,
            Key.Down => 1,
            Key.PageUp => -10,
            Key.PageDown => 10,
            _ => 0,
        };
        if (delta == 0) return;
        if (this.history.Move(delta) is { } row) this.List.ScrollIntoView(row);
        e.Handled = true;
    }

    /// <summary>Snapshot runs select rows from code; scrolls the row into view as a click would have.</summary>
    internal void ScrollTo(CommitRow row) => this.List.ScrollIntoView(row);
}
