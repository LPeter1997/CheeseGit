using CheeseGit.App.Controls;
using CheeseGit.App.ViewModels;
using CheeseGit.Core.Diff;
using CheeseGit.Core.Git;
using TextMateSharp.Grammars;
using TextMateSharp.Internal.Types;
using TextMateSharp.Registry;
using TextMateSharp.Themes;

namespace CheeseGit.App.Syntax;

/// <summary>Token colours, in the order of <see cref="Theming.ThemeManager.SyntaxKeys"/> (after <see cref="Plain"/>).</summary>
public enum SyntaxCategory : byte
{
    Plain,
    Keyword,
    String,
    Comment,
    Number,
    Operator,
    Function,
    Type,
    Variable,
    Punctuation,
    Tag,
    Attribute,
    Meta,
}

/// <summary>A run of one category, as UTF-16 indices into the line it was tokenized from.</summary>
public readonly record struct SyntaxSpan(int Start, int Length, SyntaxCategory Category);

/// <summary>
/// TextMate grammars (TextMateSharp, the VS Code grammar set) mapped to the old
/// app's semantic categories (src-tauri/src/syntax), so colours come from the
/// theme palette and follow theme switches. Thread-safe; meant for a background
/// thread, since a line costs ~0.2 ms (Phase 0).
/// </summary>
public sealed class SyntaxHighlighter
{
    /// <summary>The old app's limit: past this many lines a diff is shown without colours.</summary>
    public const int MaxLines = 10_000;

    // A pathological regex on one line must not stall the whole diff; the rest of that line stays plain.
    private static readonly TimeSpan LineTimeout = TimeSpan.FromMilliseconds(50);

    // Ordered most specific first, matched against a token's scopes from innermost out (the old app's table).
    // Null skips to the enclosing scope: quotes and comment markers take their string's or comment's colour.
    private static readonly (string Prefix, SyntaxCategory? Category)[] ScopeMap =
    [
        ("punctuation.definition.string", null),
        ("punctuation.definition.comment", null),
        ("comment", SyntaxCategory.Comment),
        ("string", SyntaxCategory.String),
        ("constant.numeric", SyntaxCategory.Number),
        ("constant.character", SyntaxCategory.String),
        ("constant.language", SyntaxCategory.Keyword),
        ("keyword.operator", SyntaxCategory.Operator),
        ("keyword", SyntaxCategory.Keyword),
        ("storage.type", SyntaxCategory.Keyword),
        ("storage.modifier", SyntaxCategory.Keyword),
        ("storage", SyntaxCategory.Keyword),
        ("entity.name.function", SyntaxCategory.Function),
        ("entity.name.type", SyntaxCategory.Type),
        ("entity.name.tag", SyntaxCategory.Tag),
        ("entity.other.attribute-name", SyntaxCategory.Attribute),
        ("entity.other.inherited-class", SyntaxCategory.Type),
        ("entity.name", SyntaxCategory.Function),
        ("variable.function", SyntaxCategory.Function),
        ("variable", SyntaxCategory.Variable),
        ("support.function", SyntaxCategory.Function),
        ("support.type", SyntaxCategory.Type),
        ("support.class", SyntaxCategory.Type),
        ("punctuation", SyntaxCategory.Punctuation),
        ("meta.tag", SyntaxCategory.Tag),
    ];

    // Extensions the grammar set does not claim (#46: project files are XML).
    private static readonly Dictionary<string, string> ExtensionAliases = new(StringComparer.OrdinalIgnoreCase)
    {
        [".slnx"] = ".xml", [".resx"] = ".xml", [".config"] = ".xml", [".manifest"] = ".xml", [".plist"] = ".xml",
        [".ui"] = ".xml", [".xsl"] = ".xml", [".xslt"] = ".xml", [".rdl"] = ".xml", [".rdlc"] = ".xml", [".vsixmanifest"] = ".xml",
        [".mts"] = ".ts", [".cts"] = ".ts", [".json5"] = ".jsonc", [".fish"] = ".sh",
        [".svelte"] = ".html", [".vue"] = ".html",
    };

    private static readonly Dictionary<string, string> FileNameAliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Dockerfile"] = ".dockerfile", ["Containerfile"] = ".dockerfile", ["Makefile"] = ".mk", ["GNUmakefile"] = ".mk",
        [".gitignore"] = ".gitignore", [".gitattributes"] = ".gitattributes", [".gitmodules"] = ".gitmodules",
        [".gitconfig"] = ".gitconfig", [".editorconfig"] = ".editorconfig", [".bashrc"] = ".bashrc", [".zshrc"] = ".zshrc",
    };

    private readonly RegistryOptions options;
    private readonly Registry registry;
    private readonly Dictionary<string, IGrammar?> grammars = [];
    private readonly Lock gate = new();

    public SyntaxHighlighter()
    {
        this.options = new RegistryOptions(ThemeName.DarkPlus);
        this.registry = new Registry(new PatchedGrammars(this.options));
    }

    public static SyntaxHighlighter Shared => field ??= new SyntaxHighlighter();

    /// <summary>
    /// Tokenizes consecutive lines of one file (the state carries from line to
    /// line). Unknown file types give one plain span per non-empty line.
    /// </summary>
    public SyntaxSpan[][] Tokenize(string fileName, IReadOnlyList<string> lines)
    {
        var result = new SyntaxSpan[lines.Count][];
        var grammar = lines.Count <= MaxLines ? this.GrammarFor(fileName) : null;
        IStateStack? state = null;
        for (var i = 0; i < lines.Count; i++)
        {
            result[i] = grammar is null ? Plain(lines[i]) : Line(grammar, lines[i], ref state);
        }
        return result;
    }

    /// <summary>Tokens for every line of a diff, indexed [hunk][line]; see <see cref="TokenizeDiff(FileDiff, SyntaxSpan[][][], CancellationToken)"/>.</summary>
    public SyntaxSpan[][][] TokenizeDiff(FileDiff diff, CancellationToken ct = default)
    {
        var result = AllocateDiff(diff);
        if (!this.TokenizeDiff(diff, result, ct))
        {
            for (var h = 0; h < diff.Hunks.Count; h++)
            {
                for (var i = 0; i < result[h].Length; i++) result[h][i] = Plain(DiffRows.ToDisplay(diff.Hunks[h].Lines[i].Content).Text);
            }
        }
        return result;
    }

    /// <summary>One slot per diff line, [hunk][line], null until tokenized.</summary>
    public static SyntaxSpan[][][] AllocateDiff(FileDiff diff)
    {
        var result = new SyntaxSpan[diff.Hunks.Count][][];
        for (var h = 0; h < result.Length; h++) result[h] = new SyntaxSpan[diff.Hunks[h].Lines.Count][];
        return result;
    }

    /// <summary>
    /// Fills <paramref name="into"/> line by line, so a reader on another
    /// thread can draw what is done so far (a slot is null until then). Tokens
    /// index the display text <see cref="DiffRows"/> draws. Each hunk is
    /// tokenized twice from a fresh state, once per side, so a deletion never
    /// inherits state from an addition; context lines take the new side's
    /// tokens. Returns false, leaving every slot null, when the file type has
    /// no grammar or the diff is too large.
    /// </summary>
    public bool TokenizeDiff(FileDiff diff, SyntaxSpan[][][] into, CancellationToken ct)
    {
        var total = diff.Hunks.Sum(h => h.Lines.Count);
        var grammar = total <= MaxLines && !diff.IsBinary ? this.GrammarFor(DisplayText.Name(diff.Path)) : null;
        if (grammar is null) return false;
        for (var h = 0; h < diff.Hunks.Count; h++)
        {
            var lines = diff.Hunks[h].Lines;
            var tokens = into[h];
            IStateStack? oldState = null;
            IStateStack? newState = null;
            for (var i = 0; i < lines.Count; i++)
            {
                ct.ThrowIfCancellationRequested();
                var text = DiffRows.ToDisplay(lines[i].Content).Text;
                switch (lines[i].Kind)
                {
                    case DiffLineKind.Deletion:
                        tokens[i] = Line(grammar, text, ref oldState);
                        break;
                    case DiffLineKind.Addition:
                        tokens[i] = Line(grammar, text, ref newState);
                        break;
                    default:
                        Line(grammar, text, ref oldState);
                        tokens[i] = Line(grammar, text, ref newState);
                        break;
                }
            }
        }
        return true;
    }

    private IGrammar? GrammarFor(string fileName)
    {
        var key = FileNameAliases.TryGetValue(fileName, out var alias) ? alias : Path.GetExtension(fileName);
        if (ExtensionAliases.TryGetValue(key, out var ext)) key = ext;
        if (key.Length == 0) return null;
        lock (this.gate)
        {
            if (this.grammars.TryGetValue(key, out var cached)) return cached;
            IGrammar? grammar = null;
            try
            {
                var scope = this.options.GetScopeByExtension(key);
                if (scope is not null) grammar = this.registry.LoadGrammar(scope);
            }
            catch (Exception)
            {
                // A grammar that fails to load means no colours for that type, not a broken diff.
            }
            this.grammars[key] = grammar;
            return grammar;
        }
    }

    private static SyntaxSpan[] Line(IGrammar grammar, string text, ref IStateStack? state)
    {
        ITokenizeLineResult result;
        try
        {
            result = grammar.TokenizeLine(text, state, LineTimeout);
        }
        catch (Exception)
        {
            return Plain(text);
        }
        state = result.RuleStack;
        var spans = new List<SyntaxSpan>(result.Tokens.Length);
        foreach (var token in result.Tokens)
        {
            var start = Math.Min(token.StartIndex, text.Length);
            var end = Math.Min(token.EndIndex, text.Length);
            if (end <= start) continue;
            Add(spans, start, end - start, Classify(token.Scopes));
        }
        var covered = spans.Count == 0 ? 0 : spans[^1].Start + spans[^1].Length;
        if (covered < text.Length) Add(spans, covered, text.Length - covered, SyntaxCategory.Plain); // Stopped early.
        return [.. spans];

        static void Add(List<SyntaxSpan> spans, int start, int length, SyntaxCategory category)
        {
            if (spans.Count > 0 && spans[^1].Category == category && spans[^1].Start + spans[^1].Length == start)
            {
                spans[^1] = spans[^1] with { Length = spans[^1].Length + length };
            }
            else
            {
                spans.Add(new SyntaxSpan(start, length, category));
            }
        }
    }

    private static SyntaxCategory Classify(List<string> scopes)
    {
        for (var i = scopes.Count - 1; i >= 0; i--)
        {
            var scope = scopes[i];
            foreach (var (prefix, category) in ScopeMap)
            {
                // Scope prefixes match whole dot-separated atoms: "string" matches "string.quoted", not "stringify".
                if (!scope.StartsWith(prefix, StringComparison.Ordinal) || (scope.Length != prefix.Length && scope[prefix.Length] != '.')) continue;
                if (category is { } c) return c;
                break;
            }
        }
        return SyntaxCategory.Plain;
    }

    private static SyntaxSpan[] Plain(string text) => text.Length == 0 ? [] : [new SyntaxSpan(0, text.Length, SyntaxCategory.Plain)];
}

/// <summary>
/// Fixes for bundled grammars. The XML grammar (TextMateSharp.Grammars 2.0.4) names only the closing
/// <c>--&gt;</c> of a comment <c>comment.block.xml</c>, so comment text in XML, XAML and project files
/// was coloured as plain text. Giving the rule that name scopes the whole comment, as HTML's does.
/// </summary>
internal sealed class PatchedGrammars(IRegistryOptions inner) : IRegistryOptions
{
    public IRawTheme GetTheme(string scopeName) => inner.GetTheme(scopeName);

    public ICollection<string> GetInjections(string scopeName) => inner.GetInjections(scopeName);

    public IRawTheme GetDefaultTheme() => inner.GetDefaultTheme();

    public IRawGrammar GetGrammar(string scopeName)
    {
        var grammar = inner.GetGrammar(scopeName);
        if (scopeName == "text.xml" && grammar?.GetRepository()?.GetProp("comments")?.GetPatterns() is { } rules)
        {
            foreach (var rule in rules)
            {
                if (rule.GetName() is null && rule.GetBegin() is "<!--" or "<%--") rule.SetName("comment.block.xml");
            }
        }
        return grammar!;
    }
}
