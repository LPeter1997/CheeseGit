using Avalonia.Media;

namespace CheeseGit.App.Theming;

public static class Fonts
{
    private static readonly string[] MonoCandidates =
        ["Cascadia Mono", "JetBrains Mono", "Consolas", "Menlo", "SF Mono", "DejaVu Sans Mono", "Liberation Mono", "Noto Sans Mono", "Adwaita Mono"];

    /// <summary>
    /// The first installed monospace family. A comma-separated FontFamily list
    /// drew nothing at all on Linux when its first entry was missing, so the
    /// fallback is resolved here once against the installed fonts.
    /// </summary>
    public static FontFamily Mono { get; } = ResolveMono();

    private static FontFamily ResolveMono()
    {
        var installed = FontManager.Current.SystemFonts.Select(f => f.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return MonoCandidates.FirstOrDefault(installed.Contains) is { } name ? new FontFamily(name) : FontFamily.Default;
    }
}
