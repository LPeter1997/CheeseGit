using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using CheeseGit.App.ViewModels;

namespace CheeseGit.App.Views;

public partial class ChangeDetailsView : UserControl
{
    public ChangeDetailsView()
    {
        this.InitializeComponent();
        // Tunnel: the ListBox handles Up/Down itself (moving its own selection, which these lists bypass) before a bubbling handler would see them.
        this.Files.AddHandler(KeyDownEvent, this.OnFilesKeyDown, RoutingStrategies.Tunnel);
    }

    private ChangeDetailsViewModel? Details => this.DataContext as ChangeDetailsViewModel;

    private void OnFilePressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Control { DataContext: FileRow file } control || this.Details is not { } details) return;
        if (!e.GetCurrentPoint(control).Properties.IsLeftButtonPressed) return;
        details.SelectFile(file);
        this.Files.Focus();
        e.Handled = true;
    }

    private void OnFilesKeyDown(object? sender, KeyEventArgs e)
    {
        if (this.Details is not { } details || e.Key is not (Key.Up or Key.Down)) return;
        if (details.MoveFile(e.Key == Key.Up ? -1 : 1) is { } file) this.Files.ScrollIntoView(file);
        e.Handled = true;
    }

    // The menu's items inherit the row's DataContext from the row the menu opened on.
    private void OnCopyPath(object? sender, RoutedEventArgs e)
    {
        if (sender is MenuItem { DataContext: FileRow row }) this.Details?.CopyPathCommand.Execute(row);
    }

    private void OnCopyName(object? sender, RoutedEventArgs e)
    {
        if (sender is MenuItem { DataContext: FileRow row }) this.Details?.CopyNameCommand.Execute(row);
    }
}
