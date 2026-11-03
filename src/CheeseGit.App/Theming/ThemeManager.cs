using Avalonia;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Styling;
using Avalonia.Threading;

namespace CheeseGit.App.Theming;

/// <summary>
/// Owns one mutable brush per palette key in the application resources. Every
/// control references these through DynamicResource, so a theme switch only
/// has to animate the brushes' colours and the whole UI cross-fades smoothly.
/// Palettes are taken from the old app (src/styles/globals.css); translucent
/// keys reproduce its Tailwind opacity modifiers (<c>bg-accent/10</c> and so on).
/// </summary>
public sealed class ThemeManager
{
    public const string System = "System";

    /// <summary>What the user can pick, in menu order.</summary>
    public static readonly IReadOnlyList<string> Choices = [System, "Light", "Dark", "High contrast"];

    /// <summary>Syntax highlighting categories, in the order of the palettes' syntax colours. Declared before <see cref="Palettes"/>, which reads it during static initialization.</summary>
    public static readonly IReadOnlyList<string> SyntaxKeys =
    [
        "SyntaxKeyword", "SyntaxString", "SyntaxComment", "SyntaxNumber", "SyntaxOperator", "SyntaxFunction",
        "SyntaxType", "SyntaxVariable", "SyntaxPunctuation", "SyntaxTag", "SyntaxAttribute", "SyntaxMeta",
    ];

    public static readonly IReadOnlyDictionary<string, IReadOnlyDictionary<string, Color>> Palettes =
        new Dictionary<string, IReadOnlyDictionary<string, Color>>
        {
            ["Light"] = Palette(
                bg: "#ffffff", surface: "#f4f4f5", hover: "#e4e4e7", fg: "#18181b", fgMuted: "#71717a", border: "#d4d4d8",
                accent: "#f59e0b", accentFg: "#ffffff", danger: "#ef4444", warning: "#d97706", success: "#22c55e",
                syntax: ["#d73a49", "#032f62", "#6a737d", "#005cc5", "#d73a49", "#6f42c1", "#6f42c1", "#24292e", "#24292e", "#22863a", "#6f42c1", "#d73a49"]),
            ["Dark"] = Palette(
                bg: "#18181b", surface: "#27272a", hover: "#3f3f46", fg: "#fafafa", fgMuted: "#a1a1aa", border: "#3f3f46",
                accent: "#f59e0b", accentFg: "#18181b", danger: "#ef4444", warning: "#f59e0b", success: "#22c55e",
                syntax: ["#ff7b72", "#a5d6ff", "#8b949e", "#79c0ff", "#ff7b72", "#d2a8ff", "#ffa657", "#c9d1d9", "#c9d1d9", "#7ee787", "#d2a8ff", "#ff7b72"]),
            ["High contrast"] = Palette(
                bg: "#000000", surface: "#1a1a1a", hover: "#333333", fg: "#ffffff", fgMuted: "#cccccc", border: "#ffffff",
                accent: "#fbbf24", accentFg: "#000000", danger: "#ff6b6b", warning: "#fbbf24", success: "#4ade80",
                syntax: ["#ff6b6b", "#6db3f2", "#999999", "#6db3f2", "#ff6b6b", "#c586c0", "#fbbf24", "#ffffff", "#cccccc", "#4ade80", "#c586c0", "#ff6b6b"]),
        };

    private static readonly TimeSpan TransitionDuration = TimeSpan.FromMilliseconds(250);

    private static readonly (string Fluent, string Palette)[] FluentAliases =
    [
        ("TextControlBackground", "Bg"), ("TextControlBackgroundPointerOver", "Bg"), ("TextControlBackgroundFocused", "Bg"),
        ("TextControlBackgroundDisabled", "BgSurface"),
        ("TextControlBorderBrush", "Border"), ("TextControlBorderBrushPointerOver", "FgMuted"), ("TextControlBorderBrushFocused", "Accent"),
        ("TextControlForeground", "Fg"), ("TextControlForegroundPointerOver", "Fg"), ("TextControlForegroundFocused", "Fg"),
        ("TextControlPlaceholderForeground", "FgMuted"), ("TextControlPlaceholderForegroundPointerOver", "FgMuted"),
        ("TextControlPlaceholderForegroundFocused", "FgMuted"), ("TextControlSelectionHighlightColor", "AccentStrong"),
        ("MenuFlyoutPresenterBackground", "BgSurface"), ("MenuFlyoutPresenterBorderBrush", "Border"),
        ("MenuFlyoutItemBackgroundPointerOver", "BgHover"), ("MenuFlyoutItemBackgroundPressed", "BgHover"),
        ("MenuFlyoutItemForeground", "Fg"), ("MenuFlyoutItemForegroundPointerOver", "Fg"), ("MenuFlyoutItemForegroundPressed", "Fg"),
        ("MenuFlyoutSubItemChevron", "FgMuted"),
        ("FlyoutPresenterBackground", "BgSurface"), ("FlyoutBorderThemeBrush", "Border"),
        ("ToolTipBackground", "BgSurface"), ("ToolTipForeground", "Fg"), ("ToolTipBorderBrush", "Border"),
        ("ComboBoxBackground", "Bg"), ("ComboBoxBackgroundPointerOver", "BgHover"), ("ComboBoxBorderBrush", "Border"),
        ("ComboBoxForeground", "Fg"), ("ComboBoxDropDownBackground", "BgSurface"), ("ComboBoxDropDownBorderBrush", "Border"),
        ("ComboBoxItemBackgroundPointerOver", "BgHover"), ("ComboBoxItemBackgroundSelected", "AccentMedium"),
        ("ComboBoxItemBackgroundSelectedPointerOver", "AccentMedium"), ("ComboBoxItemForeground", "Fg"),
        ("ComboBoxItemForegroundSelected", "Fg"), ("ComboBoxItemForegroundPointerOver", "Fg"),
        ("CheckBoxForegroundUnchecked", "Fg"), ("CheckBoxForegroundChecked", "Fg"), ("CheckBoxForegroundUncheckedPointerOver", "Fg"),
        ("CheckBoxForegroundCheckedPointerOver", "Fg"), ("CheckBoxCheckBackgroundFillChecked", "Accent"),
        ("CheckBoxCheckBackgroundFillCheckedPointerOver", "Accent"), ("CheckBoxCheckBackgroundStrokeUnchecked", "FgMuted"),
        ("CheckBoxCheckGlyphForegroundChecked", "AccentFg"), ("CheckBoxCheckGlyphForegroundCheckedPointerOver", "AccentFg"),
        ("ScrollBarThumbFill", "FgFaint"), ("ScrollBarThumbFillPointerOver", "FgMuted"),
    ];

    private readonly Application app;
    private readonly Dictionary<string, SolidColorBrush> brushes = new();
    private DispatcherTimer? timer;

    public ThemeManager(Application app, string choice)
    {
        this.app = app;
        this.Choice = Choices.Contains(choice) ? choice : System;
        this.Applied = this.Resolve(this.Choice);
        foreach (var (key, color) in Palettes[this.Applied])
        {
            var brush = new SolidColorBrush(color);
            this.brushes[key] = brush;
            app.Resources[key] = brush;
        }
        // Fluent's controls read their own keys; application resources win
        // over the theme's, so pointing them at our brushes makes them follow
        // (and animate with) the palette.
        foreach (var (fluentKey, key) in FluentAliases) app.Resources[fluentKey] = this.brushes[key];
        app.Resources["SystemAccentColor"] = Palettes["Dark"]["Accent"];
        app.RequestedThemeVariant = this.Applied == "Light" ? ThemeVariant.Light : ThemeVariant.Dark;
        if (app.PlatformSettings is { } platform)
        {
            platform.ColorValuesChanged += (_, _) =>
            {
                if (this.Choice == System) this.Animate(this.Resolve(System));
            };
        }
    }

    /// <summary>The user's choice, possibly <see cref="System"/>.</summary>
    public string Choice { get; private set; }

    /// <summary>The palette shown right now (never <see cref="System"/>; can be a hover preview).</summary>
    public string Applied { get; private set; }

    /// <summary>Raised when <see cref="Choice"/> changes.</summary>
    public event Action? Changed;

    /// <summary>
    /// Raised on every animation frame. Custom-drawn controls render with
    /// immutable copies of the brushes, so they re-render on this.
    /// </summary>
    public event Action? Tick;

    public IBrush Brush(string key) => this.brushes[key];

    public Color Color(string key) => this.brushes[key].Color;

    public void Select(string choice)
    {
        if (!Choices.Contains(choice)) return;
        this.Choice = choice;
        this.Animate(this.Resolve(choice));
        this.Changed?.Invoke();
    }

    /// <summary>Shows <paramref name="choice"/> while hovering a menu entry; null goes back to <see cref="Choice"/>.</summary>
    public void Preview(string? choice) => this.Animate(this.Resolve(choice ?? this.Choice));

    private string Resolve(string choice) =>
        choice != System ? choice
        : this.app.PlatformSettings?.GetColorValues().ThemeVariant == PlatformThemeVariant.Light ? "Light"
        : "Dark";

    private void Animate(string name)
    {
        if (name == this.Applied && this.timer is null) return;
        this.Applied = name;
        var target = Palettes[name];
        this.app.RequestedThemeVariant = name == "Light" ? ThemeVariant.Light : ThemeVariant.Dark;

        var from = this.brushes.ToDictionary(kv => kv.Key, kv => kv.Value.Color);
        var started = DateTime.UtcNow;
        this.timer?.Stop();
        this.timer = new DispatcherTimer(TimeSpan.FromMilliseconds(1000.0 / 120), DispatcherPriority.Render, (_, _) =>
        {
            var t = Math.Clamp((DateTime.UtcNow - started) / TransitionDuration, 0, 1);
            var eased = 1 - Math.Pow(1 - t, 3); // ease-out cubic
            foreach (var (key, brush) in this.brushes)
            {
                brush.Color = Lerp(from[key], target[key], eased);
            }
            this.Tick?.Invoke();
            if (t >= 1)
            {
                this.timer?.Stop();
                this.timer = null;
            }
        });
        this.timer.Start();
    }

    private static Color Lerp(Color a, Color b, double t) => Avalonia.Media.Color.FromArgb(
        (byte)(a.A + (b.A - a.A) * t),
        (byte)(a.R + (b.R - a.R) * t),
        (byte)(a.G + (b.G - a.G) * t),
        (byte)(a.B + (b.B - a.B) * t));

    private static Color WithAlpha(Color c, double alpha) => Avalonia.Media.Color.FromArgb((byte)(alpha * 255), c.R, c.G, c.B);

    private static Dictionary<string, Color> Palette(
        string bg, string surface, string hover, string fg, string fgMuted, string border,
        string accent, string accentFg, string danger, string warning, string success, string[] syntax)
    {
        var p = new Dictionary<string, Color>
        {
            ["Bg"] = Avalonia.Media.Color.Parse(bg),
            ["BgSurface"] = Avalonia.Media.Color.Parse(surface),
            ["BgHover"] = Avalonia.Media.Color.Parse(hover),
            ["Fg"] = Avalonia.Media.Color.Parse(fg),
            ["FgMuted"] = Avalonia.Media.Color.Parse(fgMuted),
            ["Border"] = Avalonia.Media.Color.Parse(border),
            ["Accent"] = Avalonia.Media.Color.Parse(accent),
            ["AccentFg"] = Avalonia.Media.Color.Parse(accentFg),
            ["Danger"] = Avalonia.Media.Color.Parse(danger),
            ["Warning"] = Avalonia.Media.Color.Parse(warning),
            ["Success"] = Avalonia.Media.Color.Parse(success),
        };
        p["BgSelected"] = WithAlpha(p["Accent"], 0.12);
        p["AccentSoft"] = WithAlpha(p["Accent"], 0.10);
        p["AccentMedium"] = WithAlpha(p["Accent"], 0.20);
        p["AccentStrong"] = WithAlpha(p["Accent"], 0.40);
        p["SuccessSoft"] = WithAlpha(p["Success"], 0.15);
        p["SuccessStrong"] = WithAlpha(p["Success"], 0.35);
        p["DangerSoft"] = WithAlpha(p["Danger"], 0.15);
        p["DangerStrong"] = WithAlpha(p["Danger"], 0.35);
        p["WarningSoft"] = WithAlpha(p["Warning"], 0.12);
        p["FgFaint"] = WithAlpha(p["FgMuted"], 0.5);
        p["Backdrop"] = Avalonia.Media.Color.FromArgb(0x66, 0, 0, 0);
        for (var i = 0; i < SyntaxKeys.Count; i++) p[SyntaxKeys[i]] = Avalonia.Media.Color.Parse(syntax[i]);
        return p;
    }
}
