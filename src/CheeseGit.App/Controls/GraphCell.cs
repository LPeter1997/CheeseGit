using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using CheeseGit.App.ViewModels;
using CheeseGit.Core.Graph;

namespace CheeseGit.App.Controls;

/// <summary>
/// Draws one history row's part of the commit graph from its precomputed
/// segments: lanes passing by, edges into and out of the commit, broken long
/// edges as arrow stubs, and the commit's dot (hollow and dashed when the
/// commit is local-only, ringed when it is HEAD). Clicking the dot checks the
/// commit out, as in the old app.
/// </summary>
public sealed class GraphCell : Control
{
    // Geometry of the old app's BranchGraph.tsx.
    public const double LaneWidth = 16;
    public const double PadLeft = 12;
    private const double NodeRadius = 4;
    private const double HitRadius = NodeRadius + 4;

    public static readonly StyledProperty<CommitRow?> CommitProperty = AvaloniaProperty.Register<GraphCell, CommitRow?>(nameof(Commit));

    /// <summary>Lanes to reserve; the widest lane among the rows on screen.</summary>
    public static readonly StyledProperty<int> LanesProperty = AvaloniaProperty.Register<GraphCell, int>(nameof(Lanes), 1);

    public static readonly StyledProperty<bool> IsHeadProperty = AvaloniaProperty.Register<GraphCell, bool>(nameof(IsHead));

    public static readonly StyledProperty<IBrush?> HeadBrushProperty = AvaloniaProperty.Register<GraphCell, IBrush?>(nameof(HeadBrush));

    /// <summary>Fill of local-only (hollow) dots.</summary>
    public static readonly StyledProperty<IBrush?> HollowBrushProperty = AvaloniaProperty.Register<GraphCell, IBrush?>(nameof(HollowBrush));

    /// <summary>The lane chain (colour) to keep while the rest fades; see <see cref="HistoryViewModel.GraphFade"/>.</summary>
    public static readonly StyledProperty<int> HighlightColorProperty = AvaloniaProperty.Register<GraphCell, int>(nameof(HighlightColor), -1);

    /// <summary>0: nothing fades; 1: everything but <see cref="HighlightColor"/> is at 10%. Animated by a transition.</summary>
    public static readonly StyledProperty<double> FadeProperty = AvaloniaProperty.Register<GraphCell, double>(nameof(Fade));

    public static readonly StyledProperty<ICommand?> CommandProperty = AvaloniaProperty.Register<GraphCell, ICommand?>(nameof(Command));

    // The old app's lane palette (layout.ts), readable on light and dark backgrounds.
    private static readonly ImmutableSolidColorBrush[] Palette = new[]
    {
        "#0ea5e9", "#a855f7", "#f97316", "#22c55e", "#ef4444", "#eab308", "#ec4899", "#14b8a6",
    }.Select(c => new ImmutableSolidColorBrush(Color.Parse(c))).ToArray();

    private static readonly IPen[] Pens = Palette.Select(b => (IPen)new ImmutablePen(b, 2, lineCap: PenLineCap.Round)).ToArray();

    private static readonly IPen[] DashedPens = Palette
        .Select(b => (IPen)new ImmutablePen(b, 1.5, new ImmutableDashStyle([1.5, 1.5], 0)))
        .ToArray();

    private bool overDot;

    static GraphCell()
    {
        AffectsRender<GraphCell>(CommitProperty, IsHeadProperty, HeadBrushProperty, HollowBrushProperty, HighlightColorProperty, FadeProperty);
        AffectsMeasure<GraphCell>(LanesProperty);
    }

    public CommitRow? Commit
    {
        get => this.GetValue(CommitProperty);
        set => this.SetValue(CommitProperty, value);
    }

    public int Lanes
    {
        get => this.GetValue(LanesProperty);
        set => this.SetValue(LanesProperty, value);
    }

    public bool IsHead
    {
        get => this.GetValue(IsHeadProperty);
        set => this.SetValue(IsHeadProperty, value);
    }

    public IBrush? HeadBrush
    {
        get => this.GetValue(HeadBrushProperty);
        set => this.SetValue(HeadBrushProperty, value);
    }

    public IBrush? HollowBrush
    {
        get => this.GetValue(HollowBrushProperty);
        set => this.SetValue(HollowBrushProperty, value);
    }

    public int HighlightColor
    {
        get => this.GetValue(HighlightColorProperty);
        set => this.SetValue(HighlightColorProperty, value);
    }

    public double Fade
    {
        get => this.GetValue(FadeProperty);
        set => this.SetValue(FadeProperty, value);
    }

    /// <summary>Invoked with the row when its dot is clicked.</summary>
    public ICommand? Command
    {
        get => this.GetValue(CommandProperty);
        set => this.SetValue(CommandProperty, value);
    }

    public static double ColumnWidth(int lanes) => PadLeft + Math.Max(1, lanes) * LaneWidth;

    private static double X(int lane) => PadLeft + lane * LaneWidth + LaneWidth / 2;

    protected override Size MeasureOverride(Size availableSize) => new(ColumnWidth(this.Lanes), 0);

    public override void Render(DrawingContext context)
    {
        if (this.Commit is not { Owner.View: { } view } commit || commit.Row >= view.Count) return;
        var ring = this.IsHead && this.HeadBrush is { } head ? new Pen(head, 2)
            : this.overDot && this.HeadBrush is { } hover ? new Pen(hover, 1.5)
            : null;
        using (context.PushClip(new Rect(this.Bounds.Size)))
        {
            DrawRow(context, view.Layout, commit.Row, this.Bounds.Height, commit.IsLocalOnly, this.HollowBrush, ring, this.HighlightColor, this.Fade);
        }
    }

    /// <summary>
    /// Draws one row of <paramref name="layout"/> at the origin: lanes passing by, edges into and
    /// out of the commit, stubs, then the dot with an optional ring. The history list and the
    /// graph corpus images (App.Tests) both draw through here.
    /// </summary>
    public static void DrawRow(DrawingContext context, GraphLayout layout, int row, double height, bool localOnly, IBrush? hollowBrush, IPen? ring,
        int highlightColor = -1, double fade = 0)
    {
        var h = height;
        var mid = h / 2;
        var faded = 1 - (0.9 * Math.Clamp(fade, 0, 1));
        foreach (var s in layout.SegmentsOf(row))
        {
            using var _ = faded < 1 && s.Color != highlightColor ? context.PushOpacity(faded) : default(DrawingContext.PushedState?);
            var pen = Pens[s.Color % Pens.Length];
            double x0 = X(s.FromLane), x1 = X(s.ToLane);
            switch (s.Kind)
            {
                case SegmentKind.Through when s.FromLane == s.ToLane:
                    context.DrawLine(pen, new Point(x0, 0), new Point(x0, h));
                    break;
                case SegmentKind.Through:
                    // A lane sliding left: straight to the middle, then curving over.
                    context.DrawLine(pen, new Point(x0, 0), new Point(x0, mid));
                    Curve(context, pen, new Point(x0, mid), new Point(x1, h));
                    break;
                case SegmentKind.Top:
                    Curve(context, pen, new Point(x0, 0), new Point(x1, mid));
                    break;
                case SegmentKind.Bottom:
                    Curve(context, pen, new Point(x0, mid), new Point(x1, h));
                    break;
                case SegmentKind.StubDown:
                    Arrow(context, pen, new Point(x0, mid + NodeRadius + 2), new Point(x0, h - 4));
                    break;
                case SegmentKind.StubUp:
                    Arrow(context, pen, new Point(x0, 4), new Point(x0, mid - NodeRadius - 3));
                    break;
            }
        }

        var center = new Point(X(layout.NodeLane(row)), mid);
        using var dim = faded < 1 && layout.NodeColor(row) != highlightColor ? context.PushOpacity(faded) : default(DrawingContext.PushedState?);
        var color = layout.NodeColor(row) % Palette.Length;
        if (ring is not null) context.DrawEllipse(null, ring, center, NodeRadius + 3, NodeRadius + 3);
        if (localOnly)
        {
            context.DrawEllipse(hollowBrush, DashedPens[color], center, NodeRadius, NodeRadius);
        }
        else
        {
            context.DrawEllipse(Palette[color], null, center, NodeRadius + 0.75, NodeRadius + 0.75);
        }
    }

    // Vertical tangents at both ends make lane changes read as smooth S-curves.
    private static void Curve(DrawingContext context, IPen pen, Point from, Point to)
    {
        if (Math.Abs(from.X - to.X) < 0.5)
        {
            context.DrawLine(pen, from, to);
            return;
        }
        var geometry = new StreamGeometry();
        using (var g = geometry.Open())
        {
            var dy = (to.Y - from.Y) * 0.6;
            g.BeginFigure(from, false);
            g.CubicBezierTo(new Point(from.X, from.Y + dy), new Point(to.X, to.Y - dy), to);
            g.EndFigure(false);
        }
        context.DrawGeometry(null, pen, geometry);
    }

    private static void Arrow(DrawingContext context, IPen pen, Point from, Point to)
    {
        context.DrawLine(pen, from, to);
        var dir = Math.Sign(to.Y - from.Y);
        context.DrawLine(pen, to, new Point(to.X - 3, to.Y - 3 * dir));
        context.DrawLine(pen, to, new Point(to.X + 3, to.Y - 3 * dir));
    }

    private bool HitsDot(Point p)
    {
        if (this.Commit is not { Owner.View: { } view } commit || commit.Row >= view.Count) return false;
        var center = new Point(X(view.Layout.NodeLane(commit.Row)), this.Bounds.Height / 2);
        return Point.Distance(p, center) <= HitRadius;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        this.SetOverDot(this.HitsDot(e.GetPosition(this)));
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        this.SetOverDot(false);
    }

    private void SetOverDot(bool over)
    {
        if (over == this.overDot) return;
        this.overDot = over;
        this.Commit?.Owner.HoverDot(over ? this.Commit : null);
        this.Cursor = over && this.Command is not null ? new Cursor(StandardCursorType.Hand) : null;
        ToolTip.SetTip(this, over && !this.IsHead ? "Check out this commit" : null);
        this.InvalidateVisual();
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed || !this.HitsDot(e.GetPosition(this))) return;
        if (this.Command is { } command && command.CanExecute(this.Commit))
        {
            command.Execute(this.Commit);
            e.Handled = true;
        }
    }
}
