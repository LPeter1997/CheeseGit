using Avalonia;

namespace CheeseGit.App.Services;

/// <summary>The main window's last normal (not maximized) bounds. Size in DIPs, position in screen pixels.</summary>
public sealed class WindowPlacement
{
    public double Width { get; set; }

    public double Height { get; set; }

    /// <summary>Unset where the platform does not report positions (Wayland).</summary>
    public int? X { get; set; }

    public int? Y { get; set; }

    public bool Maximized { get; set; }

    /// <summary>
    /// Where a saved window may reopen: the screen that holds its title bar, with the size shrunk to that
    /// screen's working area. A position whose title bar is on no screen (a monitor was unplugged) gives
    /// no position, so the window is centred instead of opening where it cannot be grabbed.
    /// </summary>
    public static (Size Size, PixelPoint? Position) Fit(WindowPlacement saved, IReadOnlyList<(PixelRect WorkingArea, double Scaling)> screens)
    {
        var size = new Size(saved.Width, saved.Height);
        if (saved.X is not { } x || saved.Y is not { } y) return (FitTo(size, Primary(screens)), null);

        foreach (var screen in screens)
        {
            var titleBar = new PixelRect(x, y, Math.Max(1, (int)(size.Width * screen.Scaling)), (int)(32 * screen.Scaling));
            var visible = titleBar.Intersect(screen.WorkingArea);
            if (visible.Width < 120 * screen.Scaling || visible.Height < 16 * screen.Scaling) continue;
            return (FitTo(size, screen), new PixelPoint(x, y));
        }
        return (FitTo(size, Primary(screens)), null);
    }

    private static (PixelRect WorkingArea, double Scaling)? Primary(IReadOnlyList<(PixelRect WorkingArea, double Scaling)> screens) =>
        screens.Count > 0 ? screens[0] : null;

    private static Size FitTo(Size size, (PixelRect WorkingArea, double Scaling)? screen)
    {
        if (screen is not { } s || s.WorkingArea.Width <= 0) return size;
        return new Size(Math.Min(size.Width, s.WorkingArea.Width / s.Scaling), Math.Min(size.Height, s.WorkingArea.Height / s.Scaling));
    }
}
