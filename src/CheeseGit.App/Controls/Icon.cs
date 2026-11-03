using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Media;

namespace CheeseGit.App.Controls;

/// <summary>
/// An outline icon: a stroked geometry in a 16×16 box (the old app's inline
/// SVGs), scaled to the control's size. Without an explicit
/// <see cref="Stroke"/> it uses the inherited foreground, so inside a button
/// it follows the hover colour Fluent sets on the presenter. (A Path with
/// <c>Stroke="{Binding $parent[ContentPresenter].Foreground}"</c> walks the
/// logical tree and found no presenter for buttons outside data templates,
/// leaving those icons invisible.)
/// </summary>
public sealed class Icon : Control
{
    public static readonly StyledProperty<Geometry?> DataProperty = AvaloniaProperty.Register<Icon, Geometry?>(nameof(Data));

    public static readonly StyledProperty<IBrush?> StrokeProperty = AvaloniaProperty.Register<Icon, IBrush?>(nameof(Stroke));

    public static readonly StyledProperty<double> StrokeThicknessProperty = AvaloniaProperty.Register<Icon, double>(nameof(StrokeThickness), 1.5);

    public static readonly StyledProperty<IBrush?> ForegroundProperty = TextElement.ForegroundProperty.AddOwner<Icon>();

    private const double ViewBox = 16;

    static Icon()
    {
        AffectsRender<Icon>(DataProperty, StrokeProperty, StrokeThicknessProperty, ForegroundProperty);
    }

    public Geometry? Data
    {
        get => this.GetValue(DataProperty);
        set => this.SetValue(DataProperty, value);
    }

    public IBrush? Stroke
    {
        get => this.GetValue(StrokeProperty);
        set => this.SetValue(StrokeProperty, value);
    }

    public double StrokeThickness
    {
        get => this.GetValue(StrokeThicknessProperty);
        set => this.SetValue(StrokeThicknessProperty, value);
    }

    public IBrush? Foreground
    {
        get => this.GetValue(ForegroundProperty);
        set => this.SetValue(ForegroundProperty, value);
    }

    protected override Size MeasureOverride(Size availableSize) => new(14, 14);

    public override void Render(DrawingContext context)
    {
        if (this.Data is not { } data || (this.Stroke ?? this.Foreground) is not { } brush) return;
        var size = Math.Min(this.Bounds.Width, this.Bounds.Height);
        var scale = size / ViewBox;
        var dx = (this.Bounds.Width - size) / 2;
        var dy = (this.Bounds.Height - size) / 2;
        using (context.PushTransform(Matrix.CreateScale(scale, scale) * Matrix.CreateTranslation(dx, dy)))
        {
            context.DrawGeometry(null, new Pen(brush, this.StrokeThickness, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round), data);
        }
    }
}
