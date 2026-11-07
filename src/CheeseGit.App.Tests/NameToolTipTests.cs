using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace CheeseGit.App.Tests;

/// <summary>Tooltips that show a name (ToolTip.name in App.axaml) keep it on one line.</summary>
public class NameToolTipTests
{
    [AvaloniaFact]
    public void A_long_branch_name_is_one_line_and_not_cut_at_320_px()
    {
        const string name = "feature/SBOF-10863-10star-adapter-serializer-with-a-much-longer-description";
        var tip = new ToolTip { Content = name };
        tip.Classes.Add("name");
        var button = new Button { Content = "x" };
        ToolTip.SetTip(button, tip);
        var window = new Window { Content = button, Width = 1200, Height = 300 };
        window.Show();
        try
        {
            ToolTip.SetIsOpen(button, true);
            Dispatcher.UIThread.RunJobs();

            var text = Assert.Single(tip.GetVisualDescendants().OfType<TextBlock>());
            Assert.Equal(name, text.Text);
            var presenter = Assert.Single(tip.GetVisualDescendants().OfType<ContentPresenter>());
            // One line (a plain tooltip wraps this at a hyphen), as wide as the text needs.
            Assert.True(text.Bounds.Height < 2 * text.FontSize, $"height {text.Bounds.Height}");
            Assert.True(tip.Bounds.Width > 320, $"width {tip.Bounds.Width}");
            Assert.True(presenter.Bounds.Width >= text.DesiredSize.Width, $"{presenter.Bounds.Width} < {text.DesiredSize.Width}");
        }
        finally
        {
            ToolTip.SetIsOpen(button, false);
            window.Close();
        }
    }
}
