using Avalonia;
using Avalonia.Controls;

namespace CheeseGit.App.Controls;

/// <summary>
/// Lays its children out in a row: the first and the last share what the ones between them leave, the ones between
/// get their desired width. The two halves are equal by default (the old app's <c>flex-1</c> each, so both grow and
/// trim with the column), but when one side needs more than half and the other needs less, the bigger one takes the
/// other's slack: a long branch name next to a short "Fetch", or "Pull 12345 changes" next to a short branch name.
/// A <c>*,Auto,*</c> grid cannot do that: its star columns are equal whatever their content wants.
/// </summary>
public sealed class SharedRowPanel : Panel
{
    private double desiredFirst;
    private double desiredLast;

    /// <summary>
    /// Splits <paramref name="available"/> between two sides that want <paramref name="first"/> and
    /// <paramref name="last"/>. Equal when both fit in half or neither does; otherwise the side that needs more than
    /// half grows into the other's slack, but never past what it wants and never below half.
    /// </summary>
    public static (double First, double Last) Split(double available, double first, double last)
    {
        available = Math.Max(0, available);
        var half = available / 2;
        if (first > half && last <= half)
        {
            var grown = Math.Max(half, Math.Min(first, available - last));
            return (grown, available - grown);
        }
        if (last > half && first <= half)
        {
            var grown = Math.Max(half, Math.Min(last, available - first));
            return (available - grown, grown);
        }
        return (half, half);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var children = this.Children.Where(c => c.IsVisible).ToList();
        var height = 0.0;
        var fixedWidth = 0.0;
        var unbounded = new Size(double.PositiveInfinity, availableSize.Height);
        for (var i = 0; i < children.Count; i++)
        {
            children[i].Measure(unbounded);
            height = Math.Max(height, children[i].DesiredSize.Height);
            if (i > 0 && i < children.Count - 1)
                fixedWidth += children[i].DesiredSize.Width;
        }
        if (children.Count == 0)
            return default;

        this.desiredFirst = children[0].DesiredSize.Width;
        this.desiredLast = children.Count > 1 ? children[^1].DesiredSize.Width : 0;
        if (!double.IsFinite(availableSize.Width))
            return new Size(this.desiredFirst + fixedWidth + this.desiredLast, height);

        // Measure each side at the most it could get beside the other, not at the width it gets now (text trims
        // at arrange anyway). The layout manager remeasures a changed child with its last constraint: a side
        // measured at its current width kept that width when its text got longer ("feature/SBOF-…" with room
        // to spare), because its desired size could not grow to tell us.
        var room = availableSize.Width - fixedWidth;
        var first = children.Count == 1 ? Math.Max(0, availableSize.Width) : Split(room, double.PositiveInfinity, this.desiredLast).First;
        children[0].Measure(new Size(first, availableSize.Height));
        if (children.Count > 1)
            children[^1].Measure(new Size(Split(room, this.desiredFirst, double.PositiveInfinity).Last, availableSize.Height));
        return new Size(availableSize.Width, height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var children = this.Children.Where(c => c.IsVisible).ToList();
        if (children.Count == 0)
            return finalSize;
        if (children.Count == 1)
        {
            children[0].Arrange(new Rect(finalSize));
            return finalSize;
        }

        var fixedWidth = 0.0;
        for (var i = 1; i < children.Count - 1; i++)
            fixedWidth += children[i].DesiredSize.Width;
        var (first, last) = Split(finalSize.Width - fixedWidth, this.desiredFirst, this.desiredLast);

        var x = 0.0;
        for (var i = 0; i < children.Count; i++)
        {
            var width = i == 0 ? first : i == children.Count - 1 ? last : children[i].DesiredSize.Width;
            children[i].Arrange(new Rect(x, 0, width, finalSize.Height));
            x += width;
        }
        return finalSize;
    }
}
