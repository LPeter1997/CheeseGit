using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace CheeseGit.App.Controls;

/// <summary>
/// The backdrop behind an image preview, so transparent pixels read as transparent rather than as
/// the panel's colour. The image is its child.
/// </summary>
public sealed class Checkerboard : Decorator
{
    public static readonly StyledProperty<IBrush?> LightProperty = AvaloniaProperty.Register<Checkerboard, IBrush?>(nameof(Light));

    public static readonly StyledProperty<IBrush?> DarkProperty = AvaloniaProperty.Register<Checkerboard, IBrush?>(nameof(Dark));

    private const double Cell = 8;

    private StreamGeometry? squares;
    private Size squaresSize;

    static Checkerboard()
    {
        AffectsRender<Checkerboard>(LightProperty, DarkProperty);
    }

    public IBrush? Light
    {
        get => this.GetValue(LightProperty);
        set => this.SetValue(LightProperty, value);
    }

    public IBrush? Dark
    {
        get => this.GetValue(DarkProperty);
        set => this.SetValue(DarkProperty, value);
    }

    public override void Render(DrawingContext context)
    {
        var size = this.Bounds.Size;
        if (size.Width <= 0 || size.Height <= 0) return;
        context.FillRectangle(this.Light ?? Brushes.White, new Rect(size));
        if (this.Dark is not { } dark) return;
        // One geometry for all dark cells, rebuilt only when the size changes.
        if (this.squares is null || this.squaresSize != size)
        {
            this.squares = new StreamGeometry();
            using (var g = this.squares.Open())
            {
                for (var row = 0; row * Cell < size.Height; row++)
                {
                    for (var col = row % 2; col * Cell < size.Width; col += 2)
                    {
                        var x = col * Cell;
                        var y = row * Cell;
                        var w = Math.Min(Cell, size.Width - x);
                        var h = Math.Min(Cell, size.Height - y);
                        g.BeginFigure(new Point(x, y), true);
                        g.LineTo(new Point(x + w, y));
                        g.LineTo(new Point(x + w, y + h));
                        g.LineTo(new Point(x, y + h));
                        g.EndFigure(true);
                    }
                }
            }
            this.squaresSize = size;
        }
        context.DrawGeometry(dark, null, this.squares);
    }
}
