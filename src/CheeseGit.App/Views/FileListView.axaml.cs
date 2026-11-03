using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using CheeseGit.App.ViewModels;

namespace CheeseGit.App.Views;

public partial class FileListView : UserControl
{
    private FileListViewModel? list;

    public FileListView()
    {
        this.InitializeComponent();
        // Tunnel: the ListBox handles Up/Down itself (moving its own selection, which these lists bypass) before a bubbling handler would see them.
        this.List.AddHandler(KeyDownEvent, this.OnListKeyDown, RoutingStrategies.Tunnel);
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (this.list is not null) this.list.PropertyChanged -= this.OnListPropertyChanged;
        this.list = this.DataContext as FileListViewModel;
        if (this.list is not null) this.list.PropertyChanged += this.OnListPropertyChanged;
        this.UpdateClasses();
    }

    private void OnListPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(FileListViewModel.ShiftHeld)) this.UpdateClasses();
    }

    // The row icons switch with the list and with Shift through these classes, so rows need no bindings for it.
    private void UpdateClasses()
    {
        this.Classes.Set("staged", this.list?.IsStaged == true);
        this.Classes.Set("unstaged", this.list?.IsStaged == false);
        this.Classes.Set("shift", this.list?.ShiftHeld == true);
    }

    /// <summary>
    /// Selection is the view model's (old app semantics: click shows the diff,
    /// Ctrl toggles, Shift selects a range, and only one list has a selection),
    /// so the ListBox's own selection is bypassed.
    /// </summary>
    private void OnRowPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Control { DataContext: FileRow row } control || this.list is null) return;
        if (!e.GetCurrentPoint(control).Properties.IsLeftButtonPressed) return;
        if (e.Source is Avalonia.Visual source && source.FindAncestorOfType<Button>(includeSelf: true) is not null) return;
        var ctrl = (e.KeyModifiers & (KeyModifiers.Control | KeyModifiers.Meta)) != 0;
        var shift = (e.KeyModifiers & KeyModifiers.Shift) != 0;
        this.list.Click(row, ctrl, shift);
        this.List.Focus();
        e.Handled = true;
    }

    private void OnListKeyDown(object? sender, KeyEventArgs e)
    {
        if (this.list is null) return;
        if (e.Key is Key.Up or Key.Down)
        {
            this.list.Move(e.Key == Key.Up ? -1 : 1);
            if (this.list.Rows.FirstOrDefault(r => r.IsCurrent) is { } current) this.List.ScrollIntoView(current);
            e.Handled = true;
        }
    }

    // The menu's items inherit the row's DataContext from the row the menu opened on.
    private void OnCopyPath(object? sender, RoutedEventArgs e)
    {
        if (sender is MenuItem { DataContext: FileRow row }) this.list?.CopyPathCommand.Execute(row);
    }

    private void OnCopyName(object? sender, RoutedEventArgs e)
    {
        if (sender is MenuItem { DataContext: FileRow row }) this.list?.CopyNameCommand.Execute(row);
    }
}
