using Avalonia;
using CheeseGit.App.Services;

namespace CheeseGit.App.Tests;

public sealed class WindowPlacementTests
{
    private static readonly List<(PixelRect, double)> TwoScreens =
    [
        (new PixelRect(0, 0, 2560, 1400), 1.0),
        (new PixelRect(2560, 0, 3840, 2100), 2.0),
    ];

    [Fact]
    public void Position_on_a_screen_is_kept()
    {
        var (size, position) = WindowPlacement.Fit(new WindowPlacement { Width = 1400, Height = 900, X = 3000, Y = 200 }, TwoScreens);
        Assert.Equal(new PixelPoint(3000, 200), position);
        Assert.Equal(new Size(1400, 900), size);
    }

    [Fact]
    public void Position_on_an_unplugged_screen_is_dropped()
    {
        var (_, position) = WindowPlacement.Fit(new WindowPlacement { Width = 1400, Height = 900, X = 7000, Y = 200 }, TwoScreens);
        Assert.Null(position);
    }

    [Fact]
    public void Title_bar_above_the_screen_is_dropped()
    {
        var (_, position) = WindowPlacement.Fit(new WindowPlacement { Width = 1400, Height = 900, X = 100, Y = -500 }, TwoScreens);
        Assert.Null(position);
    }

    [Fact]
    public void Size_shrinks_to_the_screen()
    {
        // The second screen is 1920x1050 DIPs at 200%.
        var (size, _) = WindowPlacement.Fit(new WindowPlacement { Width = 2400, Height = 1300, X = 2600, Y = 0 }, TwoScreens);
        Assert.Equal(new Size(1920, 1050), size);
    }

    [Fact]
    public void Unknown_position_keeps_the_size()
    {
        var (size, position) = WindowPlacement.Fit(new WindowPlacement { Width = 1000, Height = 700 }, TwoScreens);
        Assert.Null(position);
        Assert.Equal(new Size(1000, 700), size);
    }
}
