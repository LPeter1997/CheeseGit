using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Data;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CheeseGit.App.Controls;
using CheeseGit.App.Views;
using CheeseGit.Core.Diagnostics;

namespace CheeseGit.App.Services;

/// <summary>
/// Makes the window show anonymized text for one screenshot, then puts it back.
/// Every shown value is overridden at <see cref="BindingPriority.Animation"/>,
/// above bindings and local values, so undoing the override hands each control
/// back to its binding; the view models never see the scrambled text.
/// </summary>
/// <remarks>
/// Only what is in the visual tree is covered: a flyout or tooltip that is open
/// in its own popup window is not in the capture anyway.
/// </remarks>
internal sealed class ReportCapture : IDisposable
{
    private readonly Visual root;
    private readonly Anonymizer anonymizer;
    private readonly List<IDisposable> overrides = [];
    private readonly List<DiffView> diffViews = [];
    private readonly List<TextPresenter> covered = [];
    private readonly HashSet<AvaloniaObject> handled = [];

    private ReportCapture(Visual root, Anonymizer anonymizer)
    {
        this.root = root;
        this.anonymizer = anonymizer;
    }

    /// <summary>Anonymizes everything under <paramref name="root"/> until the result is disposed.</summary>
    public static ReportCapture Apply(Visual root, Anonymizer anonymizer)
    {
        var capture = new ReportCapture(root, anonymizer);
        capture.Extend();
        return capture;
    }

    /// <summary>
    /// Covers what appeared since the last walk (a command log row, a refreshed
    /// list) and says whether there was any. Rendering awaits, and the dispatcher
    /// keeps delivering repository events meanwhile, so a frame is only good if
    /// nothing new turned up after it was drawn.
    /// </summary>
    public bool Extend()
    {
        Dispatcher.UIThread.VerifyAccess();
        var found = false;
        // A list first: covering a text box adds to the adorner layer, which is part of the tree.
        foreach (var visual in this.root.GetSelfAndVisualDescendants().ToList()) found |= this.Override(visual);
        return found;
    }

    private bool Override(Visual visual)
    {
        // In a text box only the presenter holds what the user wrote; its placeholder is the app's ("Summary (required)").
        string Shown(AvaloniaObject owner, string text) =>
            Anonymize.GetMode(owner) == AnonymizeMode.Scramble && !(owner is TextBlock { TemplatedParent: TextBox })
                ? this.anonymizer.Text(text)
                : this.anonymizer.Scrub(text);

        switch (visual)
        {
            case TextBlock { Inlines.Count: > 0 } block:
                var found = false;
                foreach (var inline in block.Inlines) found |= this.OverrideInline(inline, block, Shown);
                return found;
            case TextBlock { Text: { Length: > 0 } text } block when this.handled.Add(block):
                this.Set(block, TextBlock.TextProperty, Shown(block, text));
                return true;
            case TextPresenter { Text: { Length: > 0 } text } presenter when this.handled.Add(presenter):
                this.Cover(presenter, Shown(presenter, text));
                return true;
            case SmartPath { Text: { Length: > 0 } text } path when this.handled.Add(path):
                this.Set(path, SmartPath.TextProperty, Shown(path, text));
                return true;
            case DiffView diff when this.handled.Add(diff):
                diff.Anonymizer = this.anonymizer;
                this.diffViews.Add(diff);
                return true;
            case Image image when image.FindAncestorOfType<ImageDiffView>() is not null && this.handled.Add(image):
                // Pictures cannot be scrambled; hidden, the checkerboard and the sizes stay.
                this.Set(image, Visual.OpacityProperty, 0.0);
                return true;
            default:
                return false;
        }
    }

    /// <summary>
    /// A text box's text cannot be overridden: the template binds the presenter's
    /// text back to the box, and the box's to its view model, so the scrambled
    /// text would replace what the user typed. The presenter is hidden instead
    /// and a text block with the same font is laid over it.
    /// </summary>
    private void Cover(TextPresenter presenter, string shown)
    {
        var cover = new TextBlock
        {
            Text = shown,
            FontFamily = presenter.FontFamily,
            FontSize = presenter.FontSize,
            FontWeight = presenter.FontWeight,
            FontStyle = presenter.FontStyle,
            Foreground = presenter.Foreground,
            TextWrapping = presenter.TextWrapping,
            TextAlignment = presenter.TextAlignment,
            LineHeight = presenter.LineHeight,
            LetterSpacing = presenter.LetterSpacing,
            IsHitTestVisible = false,
        };
        this.handled.Add(cover);
        this.Set(presenter, Visual.OpacityProperty, 0.0);
        presenter.SetValue(AdornerLayer.AdornerProperty, cover);
        this.covered.Add(presenter);
    }

    private bool OverrideInline(Inline inline, TextBlock owner, Func<AvaloniaObject, string, string> shown)
    {
        switch (inline)
        {
            case Run { Text: { Length: > 0 } text } run when this.handled.Add(run):
                this.Set(run, Run.TextProperty, shown(owner, text));
                return true;
            case Span span:
                var found = false;
                foreach (var child in span.Inlines) found |= this.OverrideInline(child, owner, shown);
                return found;
            default:
                return false;
        }
    }

    private void Set<T>(AvaloniaObject target, StyledProperty<T> property, T value)
    {
        if (target.SetValue(property, value, BindingPriority.Animation) is { } undo) this.overrides.Add(undo);
    }

    public void Dispose()
    {
        foreach (var undo in this.overrides) undo.Dispose();
        this.overrides.Clear();
        foreach (var diff in this.diffViews) diff.Anonymizer = null;
        this.diffViews.Clear();
        foreach (var presenter in this.covered) presenter.ClearValue(AdornerLayer.AdornerProperty);
        this.covered.Clear();
        this.handled.Clear();
    }
}
