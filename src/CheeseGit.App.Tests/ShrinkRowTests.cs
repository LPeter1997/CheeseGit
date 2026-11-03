using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Headless.XUnit;
using CheeseGit.App.Controls;

namespace CheeseGit.App.Tests;

public class ShrinkRowTests
{
    /// <summary>
    /// The layout manager remeasures a changed child with its last constraint. ShrinkRow used to give a shrinking
    /// child exactly its desired width, so after selecting a short path, a longer one stayed squeezed into the
    /// short one's width ("…/manifest.yaml" with the rest of the header empty).
    /// </summary>
    [AvaloniaFact]
    public void A_shrinking_child_grows_when_its_text_gets_longer()
    {
        var path = new SmartPath { Text = "a.txt", FontSize = 12 };
        var label = new TextBlock { Text = "short", TextTrimming = Avalonia.Media.TextTrimming.CharacterEllipsis };
        var button = new Button { Content = path, Padding = new Avalonia.Thickness(4, 1), Margin = new Avalonia.Thickness(-4, 0, 0, 0) };
        ShrinkRow.SetShrink(button, true);
        ShrinkRow.SetShrink(label, true);
        var row = new ShrinkRow { Children = { label, button } };
        var window = new Window { Width = 1000, Height = 100, Content = row };
        window.Show();
        try
        {
            window.UpdateLayout();
            path.Text = "SB.EventPlayer/manifest.yaml";
            label.Text = "a much longer label than before";
            window.UpdateLayout();

            Assert.True(path.Bounds.Width >= path.DesiredSize.Width && path.Bounds.Width > 100, $"path got {path.Bounds.Width}");
            Assert.Equal("SB.EventPlayer/manifest.yaml", SmartPath.Shorten(path.Text, path.Bounds.Width, s => MeasureText(path, s)));
            Assert.True(label.Bounds.Width >= label.DesiredSize.Width && label.Bounds.Width > 100, $"label got {label.Bounds.Width}");
        }
        finally
        {
            window.Close();
        }
    }

    private static double MeasureText(SmartPath path, string text) => Unconstrained(path, text).DesiredSize.Width;

    /// <summary>
    /// A detached copy measured without a constraint. It takes the font from <paramref name="like"/>: detached, it
    /// would inherit none and fall back to the system font, which is narrower than the theme's on some machines and
    /// wider on others (DejaVu Sans on the CI runner failed both tests here).
    /// </summary>
    private static SmartPath Unconstrained(SmartPath like, string text)
    {
        var probe = new SmartPath { Text = text, FontSize = like.FontSize, FontWeight = like.FontWeight };
        TextElement.SetFontFamily(probe, TextElement.GetFontFamily(like));
        probe.Measure(Avalonia.Size.Infinity);
        return probe;
    }

    /// <summary>Same for SharedRowPanel: a side that grew into the other's slack kept its old width when its text got longer.</summary>
    [AvaloniaFact]
    public void A_shared_row_side_grows_when_its_text_gets_longer()
    {
        // A SmartPath desires exactly its constraint when it does not fit, so its desired size cannot change on its own.
        var first = new SmartPath { Text = "feature/a-medium-name-past-half", FontSize = 12 };
        var last = new TextBlock { Text = "Fetch" };
        // Buttons, as in the app: a direct child invalidates the panel itself.
        var row = new SharedRowPanel { Children = { new Button { Content = first, Padding = default }, new Button { Content = last, Padding = default } } };
        var window = new Window { Width = 300, Height = 100, Content = row };
        window.Show();
        try
        {
            window.UpdateLayout();
            Assert.True(first.Bounds.Width > 150, $"the first side should start past half the row, got {first.Bounds.Width}");
            first.Text = "feature/SBOF-1234-a-longer-name-yet";
            window.UpdateLayout();

            var full = Unconstrained(first, first.Text);
            Assert.True(first.Bounds.Width >= full.DesiredSize.Width - 0.5, $"got {first.Bounds.Width} of {full.DesiredSize.Width}");
        }
        finally
        {
            window.Close();
        }
    }
}
