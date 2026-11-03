using Avalonia;
using Avalonia.Controls;

namespace CheeseGit.App.Controls;

/// <summary>
/// Lays repository tabs out side by side at <see cref="TabWidth"/>, shrinking them evenly when
/// the title bar is too narrow (the old app's tabs were <c>w-44 shrink</c> in a flex row).
/// A StackPanel in an Auto column gets infinite width, so its tabs never shrank and pushed
/// the add button and the drag area out of the window.
/// </summary>
public sealed class TabStripPanel : Panel
{
    public static readonly StyledProperty<double> TabWidthProperty =
        AvaloniaProperty.Register<TabStripPanel, double>(nameof(TabWidth), 176);

    public static readonly StyledProperty<double> MinTabWidthProperty =
        AvaloniaProperty.Register<TabStripPanel, double>(nameof(MinTabWidth), 64);

    /// <summary>Width left over for what follows the tabs (the add button and a minimum drag area).</summary>
    public static readonly StyledProperty<double> ReservedWidthProperty =
        AvaloniaProperty.Register<TabStripPanel, double>(nameof(ReservedWidth));

    static TabStripPanel()
    {
        AffectsMeasure<TabStripPanel>(TabWidthProperty, MinTabWidthProperty, ReservedWidthProperty);
    }

    public double TabWidth
    {
        get => this.GetValue(TabWidthProperty);
        set => this.SetValue(TabWidthProperty, value);
    }

    public double MinTabWidth
    {
        get => this.GetValue(MinTabWidthProperty);
        set => this.SetValue(MinTabWidthProperty, value);
    }

    public double ReservedWidth
    {
        get => this.GetValue(ReservedWidthProperty);
        set => this.SetValue(ReservedWidthProperty, value);
    }

    /// <summary>The width each tab got in the last layout pass (dragging maps the pointer to a slot with it).</summary>
    public double ActualTabWidth { get; private set; }

    protected override Size MeasureOverride(Size availableSize)
    {
        var count = this.Children.Count(c => c.IsVisible);
        var width = this.TabWidth;
        if (count > 0 && double.IsFinite(availableSize.Width))
            width = Math.Clamp((availableSize.Width - this.ReservedWidth) / count, this.MinTabWidth, this.TabWidth);
        this.ActualTabWidth = width;

        var height = 0.0;
        foreach (var child in this.Children)
        {
            child.Measure(new Size(width, availableSize.Height));
            if (child.IsVisible) height = Math.Max(height, child.DesiredSize.Height);
        }
        return new Size(width * count, height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var x = 0.0;
        foreach (var child in this.Children)
        {
            if (!child.IsVisible) continue;
            child.Arrange(new Rect(x, 0, this.ActualTabWidth, finalSize.Height));
            x += this.ActualTabWidth;
        }
        return finalSize;
    }
}
