using Avalonia;
using Avalonia.Controls;

namespace CheeseGit.App.Controls;

/// <summary>
/// A horizontal row in which only the children marked <see cref="ShrinkProperty"/> give way when the row is too
/// narrow: the others keep their width, the shrinking ones (in order, the first keeps the most) get what is left
/// and trim their text. An <c>Auto</c> grid column or a horizontal StackPanel measures its text at full width,
/// so a long stash message or commit context pushed the buttons after it out of view.
/// </summary>
public sealed class ShrinkRow : Panel
{
    public static readonly AttachedProperty<bool> ShrinkProperty =
        AvaloniaProperty.RegisterAttached<ShrinkRow, Control, bool>("Shrink");

    /// <summary>
    /// The width a shrinking child wants when there is room, instead of its desired width (a text box
    /// with a fixed width would otherwise stay that wide, and arranged narrower, overflow).
    /// </summary>
    public static readonly AttachedProperty<double> PreferredWidthProperty =
        AvaloniaProperty.RegisterAttached<ShrinkRow, Control, double>("PreferredWidth", double.NaN);

    static ShrinkRow()
    {
        AffectsParentMeasure<ShrinkRow>(ShrinkProperty, PreferredWidthProperty);
    }

    public static bool GetShrink(Control control) => control.GetValue(ShrinkProperty);

    public static void SetShrink(Control control, bool value) => control.SetValue(ShrinkProperty, value);

    public static double GetPreferredWidth(Control control) => control.GetValue(PreferredWidthProperty);

    public static void SetPreferredWidth(Control control, double value) => control.SetValue(PreferredWidthProperty, value);

    /// <summary>The width each child got in the last measure pass, by index in <see cref="Panel.Children"/>.</summary>
    private double[] widths = [];

    protected override Size MeasureOverride(Size availableSize)
    {
        var children = this.Children;
        if (this.widths.Length != children.Count) this.widths = new double[children.Count];
        var unbounded = new Size(double.PositiveInfinity, availableSize.Height);
        var used = 0.0;
        var height = 0.0;
        for (var i = 0; i < children.Count; i++)
        {
            var child = children[i];
            this.widths[i] = 0;
            if (!child.IsVisible || GetShrink(child)) continue;
            child.Measure(unbounded);
            this.widths[i] = child.DesiredSize.Width;
            used += child.DesiredSize.Width;
            height = Math.Max(height, child.DesiredSize.Height);
        }

        var left = Math.Max(0, availableSize.Width - used);
        for (var i = 0; i < children.Count; i++)
        {
            var child = children[i];
            if (!child.IsVisible || !GetShrink(child)) continue;
            var wanted = GetPreferredWidth(child);
            if (double.IsNaN(wanted))
            {
                child.Measure(unbounded);
                wanted = child.DesiredSize.Width;
            }
            var width = Math.Min(wanted, left);
            // Measure against all the room left, not just the width given: the layout manager remeasures a
            // changed child with its last constraint, so a path that got longer stayed squeezed into the old
            // path's width ("…/manifest.yaml" beside an empty header) and its desired size never grew to tell us.
            child.Measure(new Size(left, availableSize.Height));
            this.widths[i] = width;
            left -= width;
            used += width;
            height = Math.Max(height, child.DesiredSize.Height);
        }
        return new Size(double.IsFinite(availableSize.Width) ? Math.Min(used, availableSize.Width) : used, height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var x = 0.0;
        for (var i = 0; i < this.Children.Count && i < this.widths.Length; i++)
        {
            var child = this.Children[i];
            if (!child.IsVisible) continue;
            child.Arrange(new Rect(x, 0, this.widths[i], finalSize.Height));
            x += this.widths[i];
        }
        return finalSize;
    }
}
