using System.Globalization;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Media;
using Avalonia.Threading;
using CheeseGit.App.Syntax;
using CheeseGit.App.Theming;
using CheeseGit.Core.Diff;

namespace CheeseGit.App.Controls;

/// <summary>Raised when a gutter action is clicked.</summary>
public sealed class LinesActivatedEventArgs(IReadOnlyList<LineSelection> selections, bool discard) : EventArgs
{
    public IReadOnlyList<LineSelection> Selections { get; } = selections;

    public bool Discard { get; } = discard;
}

/// <summary>
/// A custom-drawn, virtualized diff (docs/architecture.md, Diff view): only the visible rows are
/// drawn, so a 50k-line diff scrolls like a 50-line one. Unified and split
/// layouts, word highlights, and the old app's staging gutters: hunk, block
/// and line actions, which become discard actions while Shift is held.
/// Hosted in a ScrollViewer as an <see cref="ILogicalScrollable"/>.
/// </summary>
public sealed class DiffView : Control, ILogicalScrollable
{
    public static readonly StyledProperty<FileDiff?> DiffProperty = AvaloniaProperty.Register<DiffView, FileDiff?>(nameof(Diff));

    public static readonly StyledProperty<bool> SplitProperty = AvaloniaProperty.Register<DiffView, bool>(nameof(Split));

    /// <summary>Show the staging gutters.</summary>
    public static readonly StyledProperty<bool> InteractiveProperty = AvaloniaProperty.Register<DiffView, bool>(nameof(Interactive));

    /// <summary>The diff shows staged changes: actions unstage instead of stage.</summary>
    public static readonly StyledProperty<bool> StagedProperty = AvaloniaProperty.Register<DiffView, bool>(nameof(Staged));

    public static readonly StyledProperty<bool> DiscardModeProperty = AvaloniaProperty.Register<DiffView, bool>(nameof(DiscardMode));

    public static readonly StyledProperty<string?> SearchQueryProperty = AvaloniaProperty.Register<DiffView, string?>(nameof(SearchQuery));

    public static readonly StyledProperty<int> CurrentMatchProperty = AvaloniaProperty.Register<DiffView, int>(nameof(CurrentMatch));

    public static readonly DirectProperty<DiffView, int> MatchCountProperty =
        AvaloniaProperty.RegisterDirect<DiffView, int>(nameof(MatchCount), o => o.MatchCount, (o, v) => o.MatchCount = v);

    public const double RowHeight = 20;
    private const double ActionCellWidth = 16;
    private const double FontSize = 12.5;

    private static readonly Typeface Mono = new(Fonts.Mono);
    private static readonly Typeface UiFont = new("Inter");

    private readonly Dictionary<(int Row, int Side), FormattedText> textCache = [];
    private List<DiffRow> rows = [];
    private List<(int Row, int Side, int Start, int Length)> matches = [];
    private double charWidth = 7.5;
    private int maxChars;
    private int maxLineNumber;
    private Size extent;
    private Size viewport;
    private Vector offset;
    private Hover hover;
    private int matchCount;
    private (int Side, int Anchor, int End)? selection;
    private bool selecting;
    private ThemeManager? themes;
    private SyntaxSpan[][][]? syntax;
    private CancellationTokenSource? syntaxCancel;

    static DiffView()
    {
        AffectsRender<DiffView>(InteractiveProperty, StagedProperty, DiscardModeProperty, CurrentMatchProperty);
        FocusableProperty.OverrideDefaultValue<DiffView>(true);
        ClipToBoundsProperty.OverrideDefaultValue<DiffView>(true);
    }

    public event EventHandler<LinesActivatedEventArgs>? LinesActivated;

    public event EventHandler? ScrollInvalidated;

    public FileDiff? Diff
    {
        get => this.GetValue(DiffProperty);
        set => this.SetValue(DiffProperty, value);
    }

    public bool Split
    {
        get => this.GetValue(SplitProperty);
        set => this.SetValue(SplitProperty, value);
    }

    public bool Interactive
    {
        get => this.GetValue(InteractiveProperty);
        set => this.SetValue(InteractiveProperty, value);
    }

    public bool Staged
    {
        get => this.GetValue(StagedProperty);
        set => this.SetValue(StagedProperty, value);
    }

    public bool DiscardMode
    {
        get => this.GetValue(DiscardModeProperty);
        set => this.SetValue(DiscardModeProperty, value);
    }

    public string? SearchQuery
    {
        get => this.GetValue(SearchQueryProperty);
        set => this.SetValue(SearchQueryProperty, value);
    }

    public int CurrentMatch
    {
        get => this.GetValue(CurrentMatchProperty);
        set => this.SetValue(CurrentMatchProperty, value);
    }

    public int MatchCount
    {
        get => this.matchCount;
        private set => this.SetAndRaise(MatchCountProperty, ref this.matchCount, value);
    }

    /// <summary>Rows as laid out now (for tests).</summary>
    public IReadOnlyList<DiffRow> Rows => this.rows;

    // ── ILogicalScrollable ────────────────────────────────────────────

    public Size Extent => this.extent;

    public Size Viewport => this.viewport;

    public Vector Offset
    {
        get => this.offset;
        set
        {
            var clamped = this.Clamp(value);
            if (clamped == this.offset) return;
            this.offset = clamped;
            this.InvalidateVisual();
        }
    }

    public bool CanHorizontallyScroll { get; set; }

    public bool CanVerticallyScroll { get; set; }

    public bool IsLogicalScrollEnabled => true;

    public Size ScrollSize => new(this.charWidth * 4, RowHeight);

    public Size PageScrollSize => new(this.viewport.Width, Math.Max(RowHeight, this.viewport.Height - RowHeight));

    public bool BringIntoView(Control target, Rect targetRect) => false;

    public Control? GetControlInDirection(NavigationDirection direction, Control? from) => null;

    public void RaiseScrollInvalidated(EventArgs e) => this.ScrollInvalidated?.Invoke(this, e);

    private Vector Clamp(Vector v) => new(
        Math.Clamp(v.X, 0, Math.Max(0, this.extent.Width - this.viewport.Width)),
        Math.Clamp(v.Y, 0, Math.Max(0, this.extent.Height - this.viewport.Height)));

    // ── Layout ────────────────────────────────────────────────────────

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        this.themes = (Application.Current as App)?.Themes;
        if (this.themes is not null) this.themes.Tick += this.OnThemeTick;
        var probe = this.Format(new string('M', 20), Brushes.Black, Mono, FontSize);
        this.charWidth = probe.WidthIncludingTrailingWhitespace / 20;
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        if (this.themes is not null) this.themes.Tick -= this.OnThemeTick;
    }

    private void OnThemeTick()
    {
        this.textCache.Clear();
        this.InvalidateVisual();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == DiffProperty || change.Property == SplitProperty)
        {
            var oldPath = (change.Property == DiffProperty ? change.OldValue as FileDiff : this.Diff)?.Path;
            if (change.Property == DiffProperty) this.Highlight();
            this.Rebuild(keepScroll: change.Property == DiffProperty && oldPath is { } p && this.Diff is { } d && p == d.Path);
        }
        else if (change.Property == SearchQueryProperty)
        {
            this.FindMatches();
            this.ScrollToMatch();
        }
        else if (change.Property == CurrentMatchProperty)
        {
            this.ScrollToMatch();
        }
    }

    private void Rebuild(bool keepScroll)
    {
        this.textCache.Clear();
        this.hover = default;
        this.selection = null;
        var diff = this.Diff;
        this.rows = diff is null ? [] : this.Split ? DiffRows.Split(diff) : DiffRows.Unified(diff);
        this.maxChars = 0;
        this.maxLineNumber = 0;
        foreach (var row in this.rows)
        {
            Measure(row.Left);
            if (row.Right is { } right) Measure(right);
        }
        if (!keepScroll) this.offset = default;
        this.UpdateExtent();
        this.FindMatches();
        this.InvalidateVisual();

        void Measure(DiffCell cell)
        {
            if (cell.Kind != CellKind.Hunk) this.maxChars = Math.Max(this.maxChars, cell.Text.Length + (cell.NoNewlineAtEnd ? 2 : 0) + (cell.LineEnding is { } eol ? eol.Length + 1 : 0));
            this.maxLineNumber = Math.Max(this.maxLineNumber, Math.Max(cell.OldNumber, cell.NewNumber));
        }
    }

    // Tokenizing costs ~0.2-0.4 ms a line, so it runs in the background and
    // fills the token slots line by line; the view repaints what has landed
    // every 50 ms, so a 10k-line diff colours from the top instead of all at
    // once seconds later. Tokens are per (hunk, line), so switching between
    // unified and split keeps them.
    private async void Highlight()
    {
        this.syntaxCancel?.Cancel();
        this.syntax = null;
        if (this.Diff is not { Hunks.Count: > 0 } diff) return;
        var cancel = this.syntaxCancel = new CancellationTokenSource();
        var tokens = this.syntax = SyntaxHighlighter.AllocateDiff(diff);
        var timer = new DispatcherTimer(TimeSpan.FromMilliseconds(50), DispatcherPriority.Background, (_, _) => this.SyntaxProgress(tokens));
        timer.Start();
        try
        {
            await Task.Run(() => SyntaxHighlighter.Shared.TokenizeDiff(diff, tokens, cancel.Token), cancel.Token);
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            timer.Stop();
        }
        this.SyntaxProgress(tokens);
    }

    private void SyntaxProgress(SyntaxSpan[][][] tokens)
    {
        if (!ReferenceEquals(tokens, this.syntax)) return;
        this.textCache.Clear();
        this.InvalidateVisual();
    }

    protected override Size MeasureOverride(Size availableSize) =>
        new(double.IsInfinity(availableSize.Width) ? 400 : availableSize.Width, double.IsInfinity(availableSize.Height) ? 400 : availableSize.Height);

    protected override Size ArrangeOverride(Size finalSize)
    {
        this.viewport = finalSize;
        this.UpdateExtent();
        return finalSize;
    }

    private void UpdateExtent()
    {
        var height = this.rows.Count * RowHeight;
        var contentWidth = this.maxChars * this.charWidth + 24;
        var available = this.Split ? this.viewport.Width / 2 - this.GutterWidth : this.viewport.Width - this.GutterWidth;
        var width = this.viewport.Width + Math.Max(0, contentWidth - available);
        var newExtent = new Size(width, height);
        var newOffset = this.Clamp(this.offset);
        if (newExtent == this.extent && newOffset == this.offset) return;
        this.extent = newExtent;
        this.offset = this.Clamp(this.offset);
        this.RaiseScrollInvalidated(EventArgs.Empty);
    }

    private double ActionsWidth => this.Interactive ? ActionCellWidth * 2 : 0;

    private double NumberWidth => Math.Max(3, this.maxLineNumber.ToString(CultureInfo.InvariantCulture).Length) * this.charWidth + 12;

    /// <summary>Width before the text on one side: actions, then one (split) or two (unified) line number columns.</summary>
    private double GutterWidth => this.ActionsWidth + this.NumberWidth * (this.Split ? 1 : 2);

    // ── Search ────────────────────────────────────────────────────────

    private void FindMatches()
    {
        this.matches = [];
        var query = this.SearchQuery;
        if (!string.IsNullOrEmpty(query))
        {
            for (var r = 0; r < this.rows.Count; r++)
            {
                var row = this.rows[r];
                Find(r, 0, row.Left);
                if (row.Right is { } right && !ReferenceEquals(right, row.Left)) Find(r, 1, right);
            }
        }
        this.MatchCount = this.matches.Count;
        this.InvalidateVisual();

        void Find(int row, int side, DiffCell cell)
        {
            if (cell.Kind is CellKind.Hunk or CellKind.Empty) return;
            var at = 0;
            while ((at = cell.Text.IndexOf(query, at, StringComparison.OrdinalIgnoreCase)) >= 0)
            {
                this.matches.Add((row, side, at, query.Length));
                at += Math.Max(1, query.Length);
            }
        }
    }

    private void ScrollToMatch()
    {
        if (this.matches.Count == 0) return;
        var m = this.matches[Math.Clamp(this.CurrentMatch, 0, this.matches.Count - 1)];
        var y = m.Row * RowHeight;
        var x = this.offset.X;
        var textX = m.Start * this.charWidth;
        var visibleText = (this.Split ? this.viewport.Width / 2 : this.viewport.Width) - this.GutterWidth - 16;
        if (textX < x || textX > x + visibleText) x = Math.Max(0, textX - visibleText / 3);
        if (y < this.offset.Y || y + RowHeight > this.offset.Y + this.viewport.Height) this.Offset = new Vector(x, y - this.viewport.Height / 2);
        else this.Offset = new Vector(x, this.offset.Y);
        this.RaiseScrollInvalidated(EventArgs.Empty);
        this.InvalidateVisual();
    }

    // ── Rendering ─────────────────────────────────────────────────────

    private IBrush Res(string key) =>
        this.themes is not null ? this.themes.Brush(key) : this.TryFindResource(key, out var value) && value is IBrush b ? b : Brushes.Gray;

    public override void Render(DrawingContext context)
    {
        var bounds = new Rect(this.Bounds.Size);
        context.FillRectangle(this.Res("Bg"), bounds);
        if (this.rows.Count == 0) return;

        var first = Math.Max(0, (int)(this.offset.Y / RowHeight));
        var last = Math.Min(this.rows.Count - 1, (int)((this.offset.Y + bounds.Height) / RowHeight));
        if (this.Split)
        {
            var half = Math.Floor(bounds.Width / 2);
            this.RenderSide(context, first, last, 0, 0, half);
            this.RenderSide(context, first, last, 1, half + 1, bounds.Width - half - 1);
            context.FillRectangle(this.Res("Border"), new Rect(half, 0, 1, bounds.Height));
        }
        else
        {
            this.RenderSide(context, first, last, 0, 0, bounds.Width);
        }
    }

    // One clip per side for the scrolled text column. Pushing a clip per row
    // (two per row, popped with using) left every row after the first without
    // text in Avalonia 12.1.4, and it was also more work per frame.
    private void RenderSide(DrawingContext context, int first, int last, int side, double x0, double width)
    {
        for (var r = first; r <= last; r++) this.RenderRow(context, r, side, x0, width);
        var contentX = x0 + this.GutterWidth;
        using (context.PushClip(new Rect(contentX, 0, Math.Max(0, x0 + width - contentX), this.Bounds.Height)))
        {
            for (var r = first; r <= last; r++) this.RenderText(context, r, side, contentX);
        }
    }

    private void RenderRow(DrawingContext context, int r, int side, double x0, double width)
    {
        var row = this.rows[r];
        var cell = side == 0 ? row.Left : row.Right!;
        var y = r * RowHeight - this.offset.Y;
        var rowRect = new Rect(x0, y, width, RowHeight);
        var discard = this.DiscardMode;

        // Background: hunk header, addition, deletion, or the hovered action target.
        IBrush? background = cell.Kind switch
        {
            CellKind.Hunk => this.Res("AccentSoft"),
            CellKind.Addition => this.Res("SuccessSoft"),
            CellKind.Deletion => this.Res("DangerSoft"),
            CellKind.Empty => this.Res("BgSurface"),
            _ => null,
        };
        if (cell.Kind == CellKind.Hunk && this.hover.Target == HoverTarget.Hunk && this.hover.Row >= 0 && this.rows[this.hover.Row].Left.Hunk == cell.Hunk)
        {
            background = this.Res("AccentMedium");
        }
        else if (this.IsHighlighted(r, side, cell))
        {
            background = discard ? this.Res("DangerStrong") : this.Res("AccentMedium");
        }
        if (background is not null) context.FillRectangle(background, rowRect);
        if (this.selection is { } sel && sel.Side == side && r >= Math.Min(sel.Anchor, sel.End) && r <= Math.Max(sel.Anchor, sel.End) && cell.Kind != CellKind.Hunk)
        {
            context.FillRectangle(this.Res("AccentStrong"), rowRect);
        }

        var textY = y + (RowHeight - FontSize * 1.3) / 2;

        if (cell.Kind == CellKind.Hunk)
        {
            if (side == 1) return; // The header spans both sides; drawn once.
            var headerX = x0 + this.ActionsWidth + 8;
            if (this.Interactive) this.DrawGlyph(context, discard ? "✕" : this.Staged ? "↑" : "↓", x0, y, ActionCellWidth * 2, this.hover.Target == HoverTarget.Hunk && this.hover.Row == r, discard);
            context.DrawText(this.Format(cell.Text, this.Res("FgMuted"), UiFont, 11.5), new Point(headerX, y + (RowHeight - 11.5 * 1.3) / 2));
            return;
        }

        // Gutter actions (only on the side where the change lives).
        if (this.Interactive && cell.IsChange)
        {
            var groupSize = cell.GroupEnd - cell.GroupStart + 1;
            var groupHovered = this.hover.Target == HoverTarget.Group && this.hover.Side == side && this.hover.Row >= cell.GroupStart && this.hover.Row <= cell.GroupEnd;
            if (groupSize > 1 && groupHovered && r == (cell.GroupStart + cell.GroupEnd) / 2)
            {
                this.DrawGlyph(context, discard ? "✕" : this.Staged ? "‹" : "›", x0, y, ActionCellWidth, true, discard);
            }
            if (this.hover.Target == HoverTarget.Line && this.hover.Row == r && this.hover.Side == side)
            {
                this.DrawGlyph(context, discard ? "✕" : this.Staged ? "−" : "+", x0 + ActionCellWidth, y, ActionCellWidth, true, discard);
            }
        }

        // Line numbers.
        var numberBrush = this.Res("FgFaint");
        var nx = x0 + this.ActionsWidth;
        if (this.Split)
        {
            var n = side == 0 ? cell.OldNumber : cell.NewNumber;
            if (cell.Kind != CellKind.Empty) this.DrawNumber(context, n, nx, textY, numberBrush);
        }
        else
        {
            if (cell.Kind != CellKind.Addition) this.DrawNumber(context, cell.OldNumber, nx, textY, numberBrush);
            if (cell.Kind != CellKind.Deletion) this.DrawNumber(context, cell.NewNumber, nx + this.NumberWidth, textY, numberBrush);
        }
    }

    private void RenderText(DrawingContext context, int r, int side, double contentX)
    {
        var row = this.rows[r];
        var cell = side == 0 ? row.Left : row.Right!;
        if (cell.Kind is CellKind.Empty or CellKind.Hunk) return;
        var textY = r * RowHeight - this.offset.Y + (RowHeight - FontSize * 1.3) / 2;
        var origin = new Point(contentX + 8 - this.offset.X, textY);
        var text = this.CachedText(r, side, cell);
        if (cell.Highlights.Length > 0)
        {
            var hlBrush = cell.Kind == CellKind.Addition ? this.Res("SuccessStrong") : this.Res("DangerStrong");
            foreach (var (start, length) in cell.Highlights)
            {
                if (length <= 0) continue;
                context.DrawGeometry(hlBrush, null, text.BuildHighlightGeometry(origin, start, length)!);
            }
        }
        for (var i = 0; i < this.matches.Count; i++)
        {
            var m = this.matches[i];
            if (m.Row != r || m.Side != side) continue;
            var current = i == Math.Clamp(this.CurrentMatch, 0, this.matches.Count - 1);
            context.DrawGeometry(current ? this.Res("Warning") : this.Res("AccentStrong"), null, text.BuildHighlightGeometry(origin, m.Start, m.Length)!);
        }
        context.DrawText(text, origin);
        if (cell.NoNewlineAtEnd)
        {
            context.DrawText(this.Format("⊘", this.Res("Danger"), Mono, FontSize - 1), new Point(origin.X + text.WidthIncludingTrailingWhitespace + this.charWidth, origin.Y));
        }
        else if (cell.LineEnding is { } eol)
        {
            context.DrawText(this.Format(eol, this.Res("FgMuted"), Mono, FontSize - 2), new Point(origin.X + text.WidthIncludingTrailingWhitespace + this.charWidth, origin.Y + 1));
        }
    }

    private bool IsHighlighted(int r, int side, DiffCell cell)
    {
        if (!this.Interactive || !cell.IsChange || this.hover.Row < 0) return false;
        return this.hover.Target switch
        {
            HoverTarget.Hunk => this.rows[this.hover.Row].Left.Hunk == cell.Hunk,
            HoverTarget.Group => this.hover.Side == side && this.hover.Row >= cell.GroupStart && this.hover.Row <= cell.GroupEnd,
            HoverTarget.Line => this.hover.Side == side && this.hover.Row == r,
            _ => false,
        };
    }

    private void DrawGlyph(DrawingContext context, string glyph, double x, double y, double width, bool hovered, bool discard)
    {
        var brush = discard ? this.Res("Danger") : hovered ? this.Res("Fg") : this.Res("FgMuted");
        var text = this.Format(glyph, brush, UiFont, 12);
        context.DrawText(text, new Point(x + (width - text.WidthIncludingTrailingWhitespace) / 2, y + (RowHeight - text.Height) / 2));
    }

    private void DrawNumber(DrawingContext context, int n, double x, double y, IBrush brush)
    {
        if (n <= 0) return;
        var text = this.Format(n.ToString(CultureInfo.InvariantCulture), brush, Mono, FontSize);
        context.DrawText(text, new Point(x + this.NumberWidth - 6 - text.WidthIncludingTrailingWhitespace, y));
    }

    private FormattedText CachedText(int row, int side, DiffCell cell)
    {
        if (this.textCache.TryGetValue((row, side), out var text)) return text;
        // Bounded: the visible rows of a few screens.
        if (this.textCache.Count > 4000) this.textCache.Clear();
        text = this.Format(cell.Text, this.Res("Fg"), Mono, FontSize);
        if (this.syntax is { } syntax && cell.Hunk < syntax.Length && cell.Line >= 0 && cell.Line < syntax[cell.Hunk].Length)
        {
            foreach (var span in syntax[cell.Hunk][cell.Line] ?? [])
            {
                if (span.Category == SyntaxCategory.Plain || span.Start + span.Length > cell.Text.Length) continue;
                text.SetForegroundBrush(this.Res(ThemeManager.SyntaxKeys[(int)span.Category - 1]), span.Start, span.Length);
            }
        }
        this.textCache[(row, side)] = text;
        return text;
    }

    private FormattedText Format(string text, IBrush brush, Typeface typeface, double size) =>
        new(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, typeface, size, brush);

    // ── Input ─────────────────────────────────────────────────────────

    private enum HoverTarget
    {
        None,
        Hunk,
        Group,
        Line,
        Text,
    }

    private readonly record struct Hover(HoverTarget Target, int Row, int Side)
    {
        public static readonly Hover Nothing = new(HoverTarget.None, -1, 0);
    }

    private Hover HitTest(Point p)
    {
        var r = (int)((p.Y + this.offset.Y) / RowHeight);
        if (r < 0 || r >= this.rows.Count) return Hover.Nothing;
        var half = Math.Floor(this.Bounds.Width / 2);
        var side = this.Split && p.X > half ? 1 : 0;
        var x = side == 1 ? p.X - half - 1 : p.X;
        var row = this.rows[r];
        var cell = side == 0 ? row.Left : row.Right!;
        if (this.Interactive && x < this.ActionsWidth)
        {
            if (cell.Kind == CellKind.Hunk) return new Hover(HoverTarget.Hunk, r, 0);
            if (!cell.IsChange) return new Hover(HoverTarget.None, r, side);
            var groupSize = cell.GroupEnd - cell.GroupStart + 1;
            if (x < ActionCellWidth && groupSize > 1) return new Hover(HoverTarget.Group, r, side);
            return new Hover(HoverTarget.Line, r, side);
        }
        return new Hover(HoverTarget.Text, r, side);
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        var p = e.GetPosition(this);
        if (this.selecting && this.selection is { } sel)
        {
            var r = Math.Clamp((int)((p.Y + this.offset.Y) / RowHeight), 0, Math.Max(0, this.rows.Count - 1));
            this.selection = sel with { End = r };
            this.InvalidateVisual();
            return;
        }
        this.SetHover(this.HitTest(p));
        this.DiscardModeFromModifiers(e.KeyModifiers);
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        this.SetHover(Hover.Nothing);
    }

    private void SetHover(Hover value)
    {
        if (value == this.hover) return;
        this.hover = value;
        this.Cursor = value.Target is HoverTarget.Hunk or HoverTarget.Group or HoverTarget.Line ? new Cursor(StandardCursorType.Hand)
            : value.Target == HoverTarget.Text ? new Cursor(StandardCursorType.Ibeam) : Cursor.Default;
        this.InvalidateVisual();
    }

    private void DiscardModeFromModifiers(KeyModifiers modifiers)
    {
        // The window keeps DiscardMode in sync with Shift; pointer events also carry it, which covers a missed key event.
        var shift = (modifiers & KeyModifiers.Shift) != 0;
        if (shift != this.DiscardMode) this.SetCurrentValue(DiscardModeProperty, shift);
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        var point = e.GetCurrentPoint(this);
        if (!point.Properties.IsLeftButtonPressed) return;
        this.Focus();
        var hit = this.HitTest(point.Position);
        var discard = (e.KeyModifiers & KeyModifiers.Shift) != 0;
        if (hit.Target is HoverTarget.Hunk or HoverTarget.Group or HoverTarget.Line && this.Diff is { } diff)
        {
            var selections = this.SelectionsFor(hit, diff);
            if (selections.Count > 0) this.LinesActivated?.Invoke(this, new LinesActivatedEventArgs(selections, discard));
            e.Handled = true;
            return;
        }
        if (hit.Target == HoverTarget.Text)
        {
            // Line-granular selection for copying; one side at a time in split view.
            this.selection = e.KeyModifiers.HasFlag(KeyModifiers.Shift) && this.selection is { } s && s.Side == hit.Side
                ? s with { End = hit.Row }
                : (hit.Side, hit.Row, hit.Row);
            this.selecting = true;
            e.Pointer.Capture(this);
            this.InvalidateVisual();
            e.Handled = true;
        }
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (!this.selecting) return;
        this.selecting = false;
        e.Pointer.Capture(null);
    }

    protected override async void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        var copy = e.Key == Key.C && (e.KeyModifiers & (KeyModifiers.Control | KeyModifiers.Meta)) != 0;
        if (copy && this.SelectedText() is { } text && TopLevel.GetTopLevel(this)?.Clipboard is { } clipboard)
        {
            e.Handled = true;
            await clipboard.SetTextAsync(text);
        }
        else if (e.Key == Key.Escape && this.selection is not null)
        {
            this.selection = null;
            this.InvalidateVisual();
            e.Handled = true;
        }
    }

    /// <summary>The selected lines' text (without +/- markers), or null.</summary>
    public string? SelectedText()
    {
        if (this.selection is not { } sel) return null;
        var sb = new StringBuilder();
        for (var r = Math.Min(sel.Anchor, sel.End); r <= Math.Max(sel.Anchor, sel.End) && r < this.rows.Count; r++)
        {
            var cell = sel.Side == 0 ? this.rows[r].Left : this.rows[r].Right!;
            if (cell.Kind is CellKind.Hunk or CellKind.Empty) continue;
            sb.Append(cell.Text).Append('\n');
        }
        return sb.Length == 0 ? null : sb.ToString();
    }

    private List<LineSelection> SelectionsFor(Hover hit, FileDiff diff)
    {
        var row = this.rows[hit.Row];
        var cell = hit.Side == 0 ? row.Left : row.Right!;
        switch (hit.Target)
        {
            case HoverTarget.Hunk:
                return DiffRows.HunkSelections(diff, cell.Hunk);
            case HoverTarget.Group:
                var result = new List<LineSelection>();
                for (var r = cell.GroupStart; r <= cell.GroupEnd; r++)
                {
                    // In unified rows the block holds both sides' changes; in split rows only this side's.
                    var c = hit.Side == 0 ? this.rows[r].Left : this.rows[r].Right!;
                    if (c.IsChange) result.Add(new LineSelection(c.Hunk, c.Line));
                }
                return result;
            case HoverTarget.Line:
                return cell.IsChange ? [new LineSelection(cell.Hunk, cell.Line)] : [];
            default:
                return [];
        }
    }
}
