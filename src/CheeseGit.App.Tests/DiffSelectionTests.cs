using System.Runtime.InteropServices;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using CheeseGit.App.Controls;
using CheeseGit.Core.Git;
using CheeseGit.Core.Diff;

namespace CheeseGit.App.Tests;

/// <summary>Selecting text in the diff by pointer, down to the character, and what gets copied.</summary>
public class DiffSelectionTests
{
    private static DiffLine Line(DiffLineKind kind, string text, int old, int @new) => new(kind, Encoding.UTF8.GetBytes(text), old, @new);

    // Rows: 0 hunk, 1 context, 2 deletion, 3 addition, 4 context, 5 hunk, 6 context.
    private static FileDiff Sample() => new(GitPath.FromString("a.txt"), GitPath.FromString("a.txt"), DiffChange.Modified, false, null, null,
    [
        new DiffHunk(1, 3, 1, 3, "", [
            Line(DiffLineKind.Context, "first line", 1, 1),
            Line(DiffLineKind.Deletion, "old second", 2, 0),
            Line(DiffLineKind.Addition, "new second", 0, 2),
            Line(DiffLineKind.Context, "foo_bar(baz);", 3, 3),
        ]),
        new DiffHunk(20, 1, 20, 1, "", [Line(DiffLineKind.Context, "far away", 20, 20)]),
    ]);

    private static FileDiff Long(int lines)
    {
        var body = Enumerable.Range(1, lines).Select(i => Line(DiffLineKind.Context, $"line {i}", i, i)).ToList();
        return new FileDiff(GitPath.FromString("a.txt"), GitPath.FromString("a.txt"), DiffChange.Modified, false, null, null, [new DiffHunk(1, lines, 1, lines, "", body)]);
    }

    private static (Window Window, DiffView View) Show(FileDiff diff, bool split = false, bool interactive = false)
    {
        var view = new DiffView { Diff = diff, Split = split, Interactive = interactive };
        var window = new Window { Width = 900, Height = 300, Content = view };
        window.Show();
        window.UpdateLayout();
        return (window, view);
    }

    private static void Drag(Window window, Point from, Point to)
    {
        window.MouseMove(from);
        window.MouseDown(from, MouseButton.Left);
        window.MouseMove(to);
        window.MouseUp(to, MouseButton.Left);
    }

    [AvaloniaFact]
    public void Dragging_within_a_line_selects_just_those_characters()
    {
        var (window, view) = Show(Sample());
        Drag(window, view.PointAt(1, 2), view.PointAt(1, 7));
        Assert.Equal("rst l", view.SelectedText());
        window.Close();
    }

    [AvaloniaFact]
    public void Dragging_backwards_selects_the_same_text()
    {
        var (window, view) = Show(Sample());
        Drag(window, view.PointAt(1, 7), view.PointAt(1, 2));
        Assert.Equal("rst l", view.SelectedText());
        window.Close();
    }

    [AvaloniaFact]
    public void Dragging_across_lines_takes_the_ends_partly_and_skips_hunk_headers()
    {
        var (window, view) = Show(Sample());
        Drag(window, view.PointAt(3, 4), view.PointAt(6, 3));
        Assert.Equal("second\nfoo_bar(baz);\nfar", view.SelectedText());
        window.Close();
    }

    [AvaloniaFact]
    public void A_click_selects_nothing()
    {
        var (window, view) = Show(Sample());
        var at = view.PointAt(1, 3);
        Drag(window, at, at);
        Assert.Null(view.SelectedText());
        window.Close();
    }

    [AvaloniaFact]
    public void Past_the_end_of_a_line_is_the_end_of_the_line()
    {
        var (window, view) = Show(Sample());
        Drag(window, view.PointAt(1, 6), new Point(view.PointAt(1, 6).X + 500, view.PointAt(1, 6).Y));
        Assert.Equal("line", view.SelectedText());
        window.Close();
    }

    [AvaloniaFact]
    public void A_split_view_selects_one_side()
    {
        var (window, view) = Show(Sample(), split: true);
        // Split rows: 0 hunk, 1 context, 2 deletion | addition, 3 context.
        Drag(window, view.PointAt(2, 4, side: 1), view.PointAt(3, 3, side: 1));
        Assert.Equal("second\nfoo", view.SelectedText());
        Drag(window, view.PointAt(2, 0, side: 0), view.PointAt(2, 3, side: 0));
        Assert.Equal("old", view.SelectedText());
        window.Close();
    }

    [AvaloniaFact]
    public void Double_click_selects_a_word_and_triple_click_the_line()
    {
        var (window, view) = Show(Sample());
        var at = view.PointAt(4, 2);
        window.MouseMove(at);
        window.MouseDown(at, MouseButton.Left);
        window.MouseUp(at, MouseButton.Left);
        window.MouseDown(at, MouseButton.Left);
        window.MouseUp(at, MouseButton.Left);
        Assert.Equal("foo_bar", view.SelectedText());
        window.MouseDown(at, MouseButton.Left);
        window.MouseUp(at, MouseButton.Left);
        Assert.Equal("foo_bar(baz);", view.SelectedText());
        window.Close();
    }

    [AvaloniaFact]
    public void Shift_click_extends_the_selection()
    {
        var (window, view) = Show(Sample());
        Drag(window, view.PointAt(1, 6), view.PointAt(1, 8));
        var to = view.PointAt(2, 3);
        window.MouseMove(to);
        window.MouseDown(to, MouseButton.Left, RawInputModifiers.Shift);
        window.MouseUp(to, MouseButton.Left, RawInputModifiers.Shift);
        Assert.Equal("line\nold", view.SelectedText());
        window.Close();
    }

    [AvaloniaFact]
    public async Task Dragging_past_the_bottom_scrolls_and_keeps_selecting()
    {
        var (window, view) = Show(Long(500));
        var from = view.PointAt(1, 0);
        window.MouseMove(from);
        window.MouseDown(from, MouseButton.Left);
        window.MouseMove(new Point(50, window.Height + 40));
        for (var i = 0; i < 200 && view.Offset.Y < 200; i++) await Task.Delay(20);
        window.MouseUp(new Point(50, window.Height + 40), MouseButton.Left);

        var scrolled = view.Offset.Y;
        Assert.True(scrolled >= 200, $"scrolled to {scrolled}");
        Assert.StartsWith("line 1\nline 2\n", view.SelectedText());
        Assert.True(view.SelectedText()!.Split('\n').Length >= 15, view.SelectedText());

        // Released: no more scrolling.
        await Task.Delay(100);
        Assert.Equal(scrolled, view.Offset.Y);
        window.Close();
    }

    [AvaloniaFact]
    public void A_refresh_with_the_same_lines_keeps_the_selection_and_a_changed_line_drops_it()
    {
        var (window, view) = Show(Sample());
        Drag(window, view.PointAt(1, 0), view.PointAt(2, 3));

        view.Diff = Sample();
        Assert.Equal("first line\nold", view.SelectedText());

        view.Diff = Sample() with
        {
            Hunks = [new DiffHunk(1, 1, 1, 1, "", [Line(DiffLineKind.Context, "edited", 1, 1), Line(DiffLineKind.Deletion, "old second", 2, 0)])],
        };
        Assert.Null(view.SelectedText());
        window.Close();
    }

    [AvaloniaFact]
    public void The_staging_gutter_still_acts_instead_of_selecting()
    {
        var (window, view) = Show(Sample(), interactive: true);
        IReadOnlyList<LineSelection>? activated = null;
        view.LinesActivated += (_, e) => activated = e.Selections;
        // The second action cell of the deleted line.
        var at = new Point(24, view.PointAt(2, 0).Y);
        window.MouseMove(at);
        window.MouseDown(at, MouseButton.Left);
        window.MouseUp(at, MouseButton.Left);
        Assert.Equal([new LineSelection(0, 1)], activated);
        Assert.Null(view.SelectedText());
        window.Close();
    }

    [AvaloniaFact]
    public void Only_the_selected_characters_are_painted()
    {
        var (window, view) = Show(Sample());
        var from = view.PointAt(1, 2);
        var to = view.PointAt(1, 7);
        var bare = window.CaptureRenderedFrame()!;
        Drag(window, from, to);
        var selected = window.CaptureRenderedFrame()!;

        // The row's top edge, above the glyphs: only the background shows.
        var y = (int)(from.Y - DiffView.RowHeight / 2 + 1);
        Assert.Equal(Pixel(bare, (int)from.X - 3, y), Pixel(selected, (int)from.X - 3, y));
        Assert.NotEqual(Pixel(bare, (int)from.X + 3, y), Pixel(selected, (int)from.X + 3, y));
        Assert.NotEqual(Pixel(bare, (int)to.X - 3, y), Pixel(selected, (int)to.X - 3, y));
        Assert.Equal(Pixel(bare, (int)to.X + 3, y), Pixel(selected, (int)to.X + 3, y));
        window.Close();
    }

    private static int Pixel(WriteableBitmap bitmap, int x, int y)
    {
        using var buffer = bitmap.Lock();
        return Marshal.ReadInt32(buffer.Address, y * buffer.RowBytes + x * 4);
    }
}
