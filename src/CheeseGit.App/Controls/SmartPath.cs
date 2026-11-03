using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Media;

namespace CheeseGit.App.Controls;

/// <summary>
/// A file path that shortens from the middle when space runs out, keeping the
/// file name and as much of the leading path as fits ("src/features/…/FileViewer.tsx").
/// Port of the old app's SmartPath component.
/// </summary>
public sealed class SmartPath : Control
{
    public static readonly StyledProperty<string?> TextProperty = AvaloniaProperty.Register<SmartPath, string?>(nameof(Text));

    public static readonly StyledProperty<IBrush?> ForegroundProperty = TextElement.ForegroundProperty.AddOwner<SmartPath>();

    public static readonly StyledProperty<double> FontSizeProperty = TextElement.FontSizeProperty.AddOwner<SmartPath>();

    public static readonly StyledProperty<FontWeight> FontWeightProperty = TextElement.FontWeightProperty.AddOwner<SmartPath>();

    private FormattedText? shown;
    private double shownFor = -1;

    static SmartPath()
    {
        AffectsMeasure<SmartPath>(TextProperty, FontSizeProperty, FontWeightProperty);
        AffectsRender<SmartPath>(ForegroundProperty);
    }

    public string? Text
    {
        get => this.GetValue(TextProperty);
        set => this.SetValue(TextProperty, value);
    }

    public IBrush? Foreground
    {
        get => this.GetValue(ForegroundProperty);
        set => this.SetValue(ForegroundProperty, value);
    }

    public double FontSize
    {
        get => this.GetValue(FontSizeProperty);
        set => this.SetValue(FontSizeProperty, value);
    }

    public FontWeight FontWeight
    {
        get => this.GetValue(FontWeightProperty);
        set => this.SetValue(FontWeightProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == TextProperty || change.Property == FontSizeProperty || change.Property == FontWeightProperty || change.Property == ForegroundProperty)
        {
            this.shownFor = -1;
        }
    }

    private FormattedText Format(string text) => new(
        text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
        new Typeface(TextElement.GetFontFamily(this), FontStyle.Normal, this.FontWeight), this.FontSize, this.Foreground ?? Brushes.Gray);

    protected override Size MeasureOverride(Size availableSize)
    {
        var full = this.Format(this.Text ?? "");
        return new Size(Math.Min(full.WidthIncludingTrailingWhitespace, availableSize.Width), full.Height);
    }

    public override void Render(DrawingContext context)
    {
        var width = this.Bounds.Width;
        if (this.shown is null || Math.Abs(this.shownFor - width) > 0.5)
        {
            this.shown = this.Format(Shorten(this.Text ?? "", width, s => this.Format(s).WidthIncludingTrailingWhitespace));
            this.shownFor = width;
        }
        else
        {
            // The brush may have been animated (theme switch).
            this.shown.SetForegroundBrush(this.Foreground ?? Brushes.Gray);
        }
        context.DrawText(this.shown, new Point(0, (this.Bounds.Height - this.shown.Height) / 2));
    }

    /// <summary>The longest of "a/b/…/name", "a/…/name", "…/name", then a name shortened in the middle that keeps its extension.</summary>
    public static string Shorten(string path, double width, Func<string, double> measure)
    {
        if (measure(path) <= width) return path;
        var segments = path.Split('/');
        var name = segments[^1];
        for (var keep = segments.Length - 2; keep >= 1; keep--)
        {
            var candidate = string.Join('/', segments[..keep]) + "/…/" + name;
            if (measure(candidate) <= width) return candidate;
        }
        if (segments.Length > 1 && measure("…/" + name) <= width) return "…/" + name;
        // Shorten the middle of the name, keeping its extension and a few letters before it
        // ("ConflictDialo…del.cs"). An ellipsis right before ".cs" read as four dots.
        var dot = name.LastIndexOf('.');
        var ext = dot > 0 && dot < name.Length - 1 ? name[dot..] : "";
        var tail = Math.Min(ext.Length + 3, name.Length - 1);
        while (tail > ext.Length && name[^tail] == '.') tail--;
        for (var head = name.Length - tail - 1; head >= 1; head--)
        {
            if (name[head - 1] == '.') continue;
            var candidate = name[..head] + "…" + name[^tail..];
            if (measure(candidate) <= width) return candidate;
        }
        return "…" + ext;
    }
}
