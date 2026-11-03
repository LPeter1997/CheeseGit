using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using CheeseGit.App.ViewModels;

namespace CheeseGit.App.Views;

public partial class ImageDiffView : UserControl
{
    public ImageDiffView() => this.InitializeComponent();

    // Upscaled icons and pixel art keep hard pixel edges; scaled-down pictures are smoothed.
    private void OnImageDataContextChanged(object? sender, EventArgs e)
    {
        if (sender is Image { DataContext: ImageSide side } image)
        {
            RenderOptions.SetBitmapInterpolationMode(image, side.Pixelated ? BitmapInterpolationMode.None : BitmapInterpolationMode.HighQuality);
        }
    }
}
