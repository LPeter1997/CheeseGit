using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Layout;
using Avalonia.Media;
using CheeseGit.App.Theming;

namespace CheeseGit.App.Controls;

/// <summary>
/// Release notes as Markdown (the old app used react-markdown in What's new). Covers what release notes use:
/// headings, bullet and numbered lists, paragraphs, fenced code, <c>**bold**</c>, <c>*italic*</c>, <c>`code`</c> and
/// links (shown as their text). Anything else shows as plain text, never as an error.
/// </summary>
public sealed class MarkdownView : StackPanel
{
    public static readonly StyledProperty<string?> MarkdownProperty = AvaloniaProperty.Register<MarkdownView, string?>(nameof(Markdown));

    public MarkdownView()
    {
        this.Spacing = 8;
    }

    public string? Markdown
    {
        get => this.GetValue(MarkdownProperty);
        set => this.SetValue(MarkdownProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == MarkdownProperty) this.Rebuild();
    }

    private void Rebuild()
    {
        this.Children.Clear();
        foreach (var block in Parse(this.Markdown ?? "")) this.Children.Add(Render(block));
    }

    internal enum BlockKind
    {
        Heading,
        Paragraph,
        Bullet,
        Numbered,
        Code,
    }

    /// <summary>One rendered block. <see cref="Level"/>: heading level, or list nesting depth.</summary>
    internal sealed record Block(BlockKind Kind, string Text, int Level = 0, string? Marker = null);

    internal static List<Block> Parse(string markdown)
    {
        var blocks = new List<Block>();
        var paragraph = new StringBuilder();
        var lines = markdown.Replace("\r\n", "\n").Split('\n');

        void Flush()
        {
            if (paragraph.Length > 0) blocks.Add(new Block(BlockKind.Paragraph, paragraph.ToString()));
            paragraph.Clear();
        }

        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            var trimmed = line.TrimStart();
            var indent = line.Length - trimmed.Length;
            if (trimmed.StartsWith("```"))
            {
                Flush();
                var code = new StringBuilder();
                for (i++; i < lines.Length && !lines[i].TrimStart().StartsWith("```"); i++) code.Append(code.Length > 0 ? "\n" : "").Append(lines[i]);
                blocks.Add(new Block(BlockKind.Code, code.ToString()));
                continue;
            }
            if (trimmed.Length == 0)
            {
                Flush();
                continue;
            }
            var hashes = trimmed.TakeWhile(c => c == '#').Count();
            if (hashes is > 0 and <= 6 && trimmed.Length > hashes && trimmed[hashes] == ' ')
            {
                Flush();
                blocks.Add(new Block(BlockKind.Heading, trimmed[(hashes + 1)..].Trim().TrimEnd('#').TrimEnd(), hashes));
                continue;
            }
            if (trimmed.Length > 2 && trimmed[0] is '-' or '*' or '+' && trimmed[1] == ' ')
            {
                Flush();
                blocks.Add(new Block(BlockKind.Bullet, trimmed[2..].Trim(), indent / 2));
                continue;
            }
            var digits = trimmed.TakeWhile(char.IsAsciiDigit).Count();
            if (digits > 0 && trimmed.Length > digits + 1 && trimmed[digits] is '.' or ')' && trimmed[digits + 1] == ' ')
            {
                Flush();
                blocks.Add(new Block(BlockKind.Numbered, trimmed[(digits + 2)..].Trim(), indent / 2, trimmed[..(digits + 1)]));
                continue;
            }
            // A continuation line of a list item belongs to it, not to a new paragraph.
            if (paragraph.Length == 0 && indent > 0 && blocks.Count > 0 && blocks[^1].Kind is BlockKind.Bullet or BlockKind.Numbered)
            {
                blocks[^1] = blocks[^1] with { Text = blocks[^1].Text + " " + trimmed.Trim() };
                continue;
            }
            paragraph.Append(paragraph.Length > 0 ? " " : "").Append(trimmed.TrimEnd());
        }
        Flush();
        return blocks;
    }

    private static Control Render(Block block)
    {
        switch (block.Kind)
        {
            case BlockKind.Heading:
                var heading = Text(block.Text);
                heading.FontWeight = FontWeight.SemiBold;
                heading.FontSize = block.Level switch { 1 => 16, 2 => 14.5, _ => 13 };
                heading.Margin = new Thickness(0, 4, 0, 0);
                return heading;
            case BlockKind.Bullet or BlockKind.Numbered:
                var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), Margin = new Thickness(block.Level * 16, 0, 0, 0) };
                var marker = new TextBlock { Text = block.Marker ?? "•", Margin = new Thickness(2, 0, 8, 0), MinWidth = 10 };
                var body = Text(block.Text);
                Grid.SetColumn(body, 1);
                grid.Children.Add(marker);
                grid.Children.Add(body);
                return grid;
            case BlockKind.Code:
                var code = new SelectableTextBlock { Text = block.Text, FontFamily = Fonts.Mono, FontSize = 12, TextWrapping = TextWrapping.Wrap };
                var border = new Border { Child = code, Padding = new Thickness(8, 6), CornerRadius = new CornerRadius(4) };
                border.Bind(Border.BackgroundProperty, border.GetResourceObservable("BgSurface"));
                return border;
            default:
                return Text(block.Text);
        }
    }

    private static SelectableTextBlock Text(string markdown)
    {
        var block = new SelectableTextBlock { TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Top };
        block.Inlines ??= [];
        foreach (var run in ParseInline(markdown))
        {
            var r = new Run(run.Text);
            if (run.Bold) r.FontWeight = FontWeight.SemiBold;
            if (run.Italic) r.FontStyle = FontStyle.Italic;
            if (run.Code) r.FontFamily = Fonts.Mono;
            if (run.Link) r.TextDecorations = TextDecorations.Underline;
            block.Inlines.Add(r);
        }
        return block;
    }

    internal sealed record Span(string Text, bool Bold = false, bool Italic = false, bool Code = false, bool Link = false);

    internal static List<Span> ParseInline(string text)
    {
        var spans = new List<Span>();
        var current = new StringBuilder();
        bool bold = false, italic = false;

        void Emit(bool code = false, bool link = false, string? literal = null)
        {
            var value = literal ?? current.ToString();
            if (value.Length > 0) spans.Add(new Span(value, bold, italic, code, link));
            if (literal is null) current.Clear();
        }

        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (c == '\\' && i + 1 < text.Length && !char.IsLetterOrDigit(text[i + 1]))
            {
                current.Append(text[++i]);
            }
            else if (c == '`' && text.IndexOf('`', i + 1) is var end and > 0)
            {
                Emit();
                Emit(code: true, literal: text[(i + 1)..end]);
                i = end;
            }
            else if (c == '[' && text.IndexOf("](", i, StringComparison.Ordinal) is var close and > 0 && text.IndexOf(')', close) is var stop and > 0)
            {
                Emit();
                Emit(link: true, literal: text[(i + 1)..close]);
                i = stop;
            }
            else if ((c == '*' || c == '_') && i + 1 < text.Length && text[i + 1] == c)
            {
                Emit();
                bold = !bold;
                i++;
            }
            // A lone * or _ only opens emphasis before a word, so "a * b" and snake_case stay as written.
            else if (c is '*' or '_' && (italic || (i + 1 < text.Length && !char.IsWhiteSpace(text[i + 1]) && (i == 0 || !char.IsLetterOrDigit(text[i - 1])))))
            {
                Emit();
                italic = !italic;
            }
            else
            {
                current.Append(c);
            }
        }
        Emit();
        return spans;
    }
}
