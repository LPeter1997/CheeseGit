using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using CheeseGit.App.ViewModels;

namespace CheeseGit.App.Views;

public partial class StashListView : UserControl
{
    public StashListView()
    {
        this.InitializeComponent();
        // Tunnel: the ListBox handles Up/Down itself (moving its own selection, which these lists bypass) before a bubbling handler would see them.
        this.List.AddHandler(KeyDownEvent, this.OnListKeyDown, RoutingStrategies.Tunnel);
    }

    private StashViewModel? Stash => this.DataContext as StashViewModel;

    /// <summary>Selection is the view model's (it follows an entry by commit when indices move); the row's buttons are theirs.</summary>
    private void OnRowPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Control { DataContext: StashRow row } control || this.Stash is not { } stash) return;
        if (!e.GetCurrentPoint(control).Properties.IsLeftButtonPressed || e.Handled) return;
        if (e.Source is Avalonia.Visual source && source.FindAncestorOfType<Button>(includeSelf: true) is not null) return;
        stash.Select(row);
        this.List.Focus();
        e.Handled = true;
    }

    private void OnListKeyDown(object? sender, KeyEventArgs e)
    {
        if (this.Stash is not { } stash || e.Key is not (Key.Up or Key.Down)) return;
        if (stash.Move(e.Key == Key.Up ? -1 : 1) is { } row) this.List.ScrollIntoView(row);
        e.Handled = true;
    }
}
