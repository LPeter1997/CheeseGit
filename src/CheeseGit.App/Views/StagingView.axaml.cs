using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;
using CheeseGit.App.ViewModels;

namespace CheeseGit.App.Views;

public partial class StagingView : UserControl
{
    public StagingView()
    {
        this.InitializeComponent();
        this.CommitSplitter.DragCompleted += (_, _) =>
        {
            if (this.Main is not { } main) return;
            main.Settings.CommitFormHeight = this.Layout.RowDefinitions[4].ActualHeight;
            main.Store.Save();
        };
    }

    private MainViewModel? Main => (this.FindAncestorOfType<RepoView>()?.DataContext as RepoTabViewModel)?.Shell as MainViewModel;

    protected override void OnAttachedToVisualTree(Avalonia.VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        if (this.Main is { } main) this.Layout.RowDefinitions[4].Height = new GridLength(Math.Clamp(main.Settings.CommitFormHeight, 140, 480));
    }

    private void OnFilterKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape || this.DataContext is not StagingViewModel { Filter.Length: > 0 } vm) return;
        vm.Filter = "";
        e.Handled = true;
    }
}
