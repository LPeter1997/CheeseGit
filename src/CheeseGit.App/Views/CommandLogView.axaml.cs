using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.VisualTree;
using CheeseGit.App.ViewModels;

namespace CheeseGit.App.Views;

public partial class CommandLogView : UserControl
{
    public CommandLogView() => this.InitializeComponent();

    private MainViewModel? Main => this.FindAncestorOfType<Window>()?.DataContext as MainViewModel;

    protected override void OnAttachedToVisualTree(Avalonia.VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        if (this.Main is { } main) this.Root.Height = Math.Clamp(main.Settings.CommandLogHeight, 120, 800);
    }

    private void OnResize(object? sender, VectorEventArgs e) =>
        this.Root.Height = Math.Clamp(this.Root.Height - e.Vector.Y, 120, Math.Max(120, (this.Parent as Control)?.Bounds.Height * 0.8 ?? 800));

    private void OnResizeDone(object? sender, VectorEventArgs e)
    {
        if (this.Main is not { } main) return;
        main.Settings.CommandLogHeight = this.Root.Height;
        main.Store.Save();
    }

    private void OnEntryPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Control { DataContext: OperationEntry entry }) return;
        if (e.Source is Avalonia.Visual source && source.FindAncestorOfType<Button>(includeSelf: true) is not null) return;
        entry.IsExpanded = !entry.IsExpanded;
        e.Handled = true;
    }
}
