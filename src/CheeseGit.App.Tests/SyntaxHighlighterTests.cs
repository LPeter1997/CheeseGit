using CheeseGit.App.Syntax;
using CheeseGit.Core.Diff;
using CheeseGit.Core.Git;

namespace CheeseGit.App.Tests;

/// <summary>The old app's src-tauri/tests/syntax.rs, plus the diff-specific parts.</summary>
public class SyntaxHighlighterTests
{
    private static readonly SyntaxHighlighter Highlighter = new();

    // Each line gets a 50 ms budget. The first lines through a new grammar
    // (JIT, regex compilation) can exceed it when the test matrix runs at full
    // load and then come back plain, so the grammar is warmed up first.
    private static SyntaxSpan[][] Tokenize(string fileName, string content)
    {
        var lines = content.Split('\n');
        Highlighter.Tokenize(fileName, lines);
        return Highlighter.Tokenize(fileName, lines);
    }

    private static SyntaxCategory[] Categories(SyntaxSpan[] line) => [.. line.Select(s => s.Category)];

    [Fact]
    public void Rust_keywords_and_numbers()
    {
        var result = Tokenize("main.rs", "fn main() {\n    let x = 42;\n}\n");

        Assert.Equal(4, result.Length);
        Assert.Contains(SyntaxCategory.Keyword, Categories(result[0]));
        Assert.Contains(SyntaxCategory.Keyword, Categories(result[1]));
        Assert.Contains(SyntaxCategory.Number, Categories(result[1]));
    }

    [Fact]
    public void JavaScript_strings_and_comments()
    {
        var result = Tokenize("app.js", "const x = \"hello\";\n// comment\n");

        Assert.Contains(SyntaxCategory.Keyword, Categories(result[0]));
        Assert.Contains(SyntaxCategory.String, Categories(result[0]));
        Assert.Contains(SyntaxCategory.Comment, Categories(result[1]));
    }

    /// <summary>
    /// A line that runs out of time must not leave the next lines in its unfinished state. On a loaded CI runner
    /// 'const x = "hello";' stopped inside the string, and "// comment" after it came out as a string.
    /// </summary>
    [Fact]
    public void A_line_out_of_time_does_not_colour_the_next_lines()
    {
        // With a one-tick budget the first line stops right after the string opens.
        var lines = new[] { "\"hello world, a longer string\" + y;", "// comment" };
        new SyntaxHighlighter().Tokenize("app.js", lines); // JIT and grammar loading outside the budget below.
        var result = new SyntaxHighlighter(TimeSpan.FromTicks(1)).Tokenize("app.js", lines);
        Assert.DoesNotContain(SyntaxCategory.String, Categories(result[1]));
    }

    [Fact]
    public void Tsx_python_and_html_have_grammars()
    {
        Assert.Contains(SyntaxCategory.Keyword, Categories(Tokenize("Component.tsx", "const x: number = 42;")[0]));
        Assert.Contains(SyntaxCategory.Keyword, Categories(Tokenize("app.py", "def greet(name):")[0]));
        Assert.Contains(SyntaxCategory.Tag, Categories(Tokenize("page.html", "<div class=\"foo\">Hello</div>")[0]));
    }

    [Theory]
    [InlineData("CheeseGit.slnx")] // #46: project files are XML.
    [InlineData("App.csproj")]
    [InlineData("Directory.Build.props")]
    public void Project_files_are_highlighted_as_xml(string fileName)
    {
        Assert.Contains(SyntaxCategory.Tag, Categories(Tokenize(fileName, "<Project Sdk=\"Microsoft.NET.Sdk\">")[0]));
    }

    [Theory]
    [InlineData("App.axaml")]
    [InlineData("App.csproj")]
    public void Xml_comments_are_comments_throughout(string fileName)
    {
        // The bundled XML grammar scoped only the closing "-->"; the text before it came out plain.
        var result = Tokenize(fileName, "    <!-- one line -->\n<!-- starts\n  middle\n ends -->\n<a/>");

        Assert.Equal([SyntaxCategory.Plain, SyntaxCategory.Comment], Categories(result[0]));
        Assert.Equal([SyntaxCategory.Comment], Categories(result[1]));
        Assert.Equal([SyntaxCategory.Comment], Categories(result[2]));
        Assert.Equal([SyntaxCategory.Comment], Categories(result[3]));
        Assert.Contains(SyntaxCategory.Tag, Categories(result[4]));
    }

    [Fact]
    public void Dockerfile_is_recognised_by_name()
    {
        Assert.Contains(SyntaxCategory.Keyword, Categories(Tokenize("Dockerfile", "FROM alpine:3")[0]));
    }

    [Fact]
    public void Unknown_types_are_one_plain_span_per_line()
    {
        var result = Tokenize("data.xyz", "hello world foo bar baz\n\n");

        Assert.Equal([new SyntaxSpan(0, 23, SyntaxCategory.Plain)], result[0]);
        Assert.Empty(result[1]);
    }

    [Fact]
    public void Spans_cover_each_line_exactly()
    {
        var content = "pub struct Foo {\n    bar: String, // ünïcödé 🧀\n    baz: u32,\n}\n";
        var lines = content.Split('\n');
        var result = Highlighter.Tokenize("types.rs", lines);

        for (var i = 0; i < lines.Length; i++)
        {
            var at = 0;
            foreach (var span in result[i])
            {
                Assert.Equal(at, span.Start);
                Assert.True(span.Length > 0);
                at += span.Length;
            }
            Assert.Equal(lines[i].Length, at);
        }
    }

    [Fact]
    public void Too_many_lines_skip_highlighting()
    {
        var lines = Enumerable.Repeat("let x = 1;", SyntaxHighlighter.MaxLines + 1).ToArray();

        var result = Highlighter.Tokenize("big.rs", lines);

        Assert.All(result, line => Assert.Equal([new SyntaxSpan(0, 10, SyntaxCategory.Plain)], line));
    }

    [Fact]
    public void Diff_sides_are_tokenized_separately()
    {
        // The deletion opens a block comment that the new side never has: the addition after it must not be a comment.
        var diff = Diff("a.c",
            Line(DiffLineKind.Context, "int a;"),
            Line(DiffLineKind.Deletion, "/* old"),
            Line(DiffLineKind.Deletion, "still old */"),
            Line(DiffLineKind.Addition, "int b;"),
            Line(DiffLineKind.Context, "int c;"));

        var tokens = Highlighter.TokenizeDiff(diff, TestContext.Current.CancellationToken);

        Assert.Equal([SyntaxCategory.Comment], Categories(tokens[0][1]));
        Assert.Equal([SyntaxCategory.Comment], Categories(tokens[0][2]));
        Assert.Contains(SyntaxCategory.Keyword, Categories(tokens[0][3]));
        Assert.Contains(SyntaxCategory.Keyword, Categories(tokens[0][4]));
    }

    [Fact]
    public void Diff_tokens_index_the_displayed_text()
    {
        // Tabs are expanded for display; spans must line up with the expanded text, not the raw bytes.
        var diff = Diff("a.py", Line(DiffLineKind.Addition, "\tx = \"s\""));

        var span = Assert.Single(Highlighter.TokenizeDiff(diff, TestContext.Current.CancellationToken)[0][0], s => s.Category == SyntaxCategory.String);

        Assert.Equal("\"s\"", "    x = \"s\"".Substring(span.Start, span.Length));
    }

    private static DiffLine Line(DiffLineKind kind, string text) => new(kind, System.Text.Encoding.UTF8.GetBytes(text), 0, 0);

    private static FileDiff Diff(string path, params DiffLine[] lines) =>
        FileDiff.Empty(GitPath.FromString(path)) with { Hunks = [new DiffHunk(1, 1, 1, 1, "", lines)] };
}
