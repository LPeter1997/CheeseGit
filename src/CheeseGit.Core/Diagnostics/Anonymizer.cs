using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace CheeseGit.Core.Diagnostics;

/// <summary>
/// Replaces private text (paths, branch names, people, file contents, commit
/// messages) with gibberish for problem reports, so a screenshot and a command
/// log can be shared. One instance per report: the same word maps to the same
/// fake word everywhere (UI and log correlate), and a fresh random salt per
/// report means two reports cannot be correlated with each other.
/// </summary>
/// <remarks>
/// The output keeps the input's shape: same UTF-16 length, letters stay
/// letters of the same case, digits stay digits, everything else is kept.
/// Same length keeps truncation and layout bugs reproducible, and lets the
/// diff view draw scrambled text over highlighting spans computed on the real text.
/// </remarks>
public sealed partial class Anonymizer
{
    /// <summary>
    /// Kept as they are: git's own vocabulary in refs and URLs, the usual
    /// branch and remote names (useful when debugging, private to nobody).
    /// </summary>
    private static readonly HashSet<string> Allowed = new(StringComparer.Ordinal)
    {
        "main", "master", "develop", "trunk", "origin", "upstream", "HEAD", "FETCH_HEAD", "ORIG_HEAD", "MERGE_HEAD",
        "refs", "heads", "tags", "remotes", "stash", "notes",
        "gitignore", "gitattributes", "gitmodules", "gitkeep",
        "https", "http", "ssh", "git", "github", "gitlab", "bitbucket", "codeberg", "com", "org",
    };

    private const int MaxExtension = 8;

    private readonly byte[] salt;
    private readonly Lock gate = new();
    private readonly Dictionary<string, string> words = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> oids = new(StringComparer.Ordinal);

    /// <summary>Known private strings and how each is scrambled, for <see cref="Scrub"/>.</summary>
    private readonly Dictionary<string, TokenKind> tokens = new(StringComparer.Ordinal);
    private int[] tokenLengths = [];

    public Anonymizer() : this(RandomNumberGenerator.GetBytes(32))
    {
    }

    /// <summary>A fixed salt gives a fixed mapping (tests).</summary>
    public Anonymizer(byte[] salt) => this.salt = salt;

    private enum TokenKind
    {
        Text,
        Path,
        Url,
    }

    /// <summary>Scrambles every word of free text (file contents, commit messages).</summary>
    public string Text(string text) => this.ScrambleWords(text, keepExtension: false);

    /// <summary>
    /// A repository-relative or absolute path: each segment is scrambled, the
    /// file extension is kept (it says what kind of file, and drives highlighting).
    /// </summary>
    public string Path(string path) => this.ScrambleWords(path, keepExtension: true);

    /// <summary>A full or short ref name. The <c>refs/heads/</c> prefix and the usual names are in the allow list.</summary>
    public string Ref(string name) => this.Text(name);

    /// <summary>A remote URL: a password in it is removed, the rest scrambled word by word.</summary>
    public string Url(string url)
    {
        var scheme = url.IndexOf("://", StringComparison.Ordinal);
        var at = url.IndexOf('@', scheme < 0 ? 0 : scheme + 3);
        var slash = scheme < 0 ? -1 : url.IndexOf('/', scheme + 3);
        if (scheme >= 0 && at > 0 && (slash < 0 || at < slash))
        {
            var userInfo = url[(scheme + 3)..at];
            var colon = userInfo.IndexOf(':');
            if (colon >= 0)
            {
                // A password or token: its length would say what kind it is, so it goes entirely.
                return this.Text(url[..(scheme + 3)] + userInfo[..colon]) + ":***" + this.Text(url[at..]);
            }
        }
        return this.Text(url);
    }

    /// <summary>
    /// A commit id (full or abbreviated). Prefix-stable: a short id maps to the
    /// start of its full id's mapping, so they still match up in a report.
    /// </summary>
    public string Oid(string hex)
    {
        lock (this.gate)
        {
            if (this.oids.TryGetValue(hex, out var known)) return known;
            var result = string.Create(hex.Length, (this, hex), static (span, state) =>
            {
                var (self, input) = state;
                Span<byte> mac = stackalloc byte[32];
                for (var i = 0; i < input.Length; i++)
                {
                    self.Hash("oid:" + input[..(i + 1)].ToLowerInvariant(), mac);
                    var c = "0123456789abcdef"[mac[0] & 0xF];
                    span[i] = char.IsUpper(input[i]) ? char.ToUpperInvariant(c) : c;
                }
            });
            this.oids[hex] = result;
            return result;
        }
    }

    // ── The dictionary for label text ────────────────────────────────

    /// <summary>A private string scrambled as free text wherever <see cref="Scrub"/> finds it (a person's name, an email).</summary>
    public void AddText(string token) => this.Add(token, TokenKind.Text);

    /// <summary>
    /// A path. Its file name and every parent directory are added too, since the
    /// UI shows them apart (name column, folder rows).
    /// </summary>
    public void AddPath(string path)
    {
        this.Add(path, TokenKind.Path);
        var trimmed = path.TrimEnd('/', '\\');
        for (var i = trimmed.Length - 1; i > 0; i--)
        {
            if (trimmed[i] is not ('/' or '\\')) continue;
            this.Add(trimmed[..i], TokenKind.Path);
            if (i == trimmed.LastIndexOfAny(['/', '\\'])) this.Add(trimmed[(i + 1)..], TokenKind.Path);
        }
    }

    /// <summary>A ref, with its short forms (<c>feature/x</c>, <c>origin/feature/x</c>) and each segment.</summary>
    public void AddRef(string fullName)
    {
        foreach (var prefix in (ReadOnlySpan<string>)["refs/heads/", "refs/tags/", "refs/remotes/", "refs/"])
        {
            if (!fullName.StartsWith(prefix, StringComparison.Ordinal)) continue;
            var shortName = fullName[prefix.Length..];
            this.Add(shortName, TokenKind.Text);
            foreach (var segment in shortName.Split('/')) this.Add(segment, TokenKind.Text);
            break;
        }
        this.Add(fullName, TokenKind.Text);
    }

    /// <summary>A remote URL, also in its scp form's parts.</summary>
    public void AddUrl(string url)
    {
        this.Add(url, TokenKind.Url);
        if (url.EndsWith(".git", StringComparison.Ordinal)) this.Add(url[..^4], TokenKind.Url);
    }

    private void Add(string token, TokenKind kind)
    {
        if (token.Length == 0 || this.IsPublic(token)) return;
        lock (this.gate)
        {
            if (!this.tokens.TryAdd(token, kind)) return;
            if (Array.IndexOf(this.tokenLengths, token.Length) < 0)
            {
                // Longest first: "feature/login" wins over "feature".
                this.tokenLengths = [.. this.tokenLengths.Append(token.Length).OrderDescending()];
            }
        }
    }

    /// <summary>A token that scrambles to itself (all words allowed, or no words) needs no entry.</summary>
    private bool IsPublic(string token) => this.Text(token) == token;

    /// <summary>
    /// For text that is mostly the app's or git's own (labels, command lines,
    /// stderr): replaces the known private strings, emails, URLs and commit ids,
    /// and leaves every other word readable.
    /// </summary>
    public string Scrub(string text)
    {
        if (text.Length == 0) return text;
        var matches = new List<(int Start, int Length, string Replacement)>();
        lock (this.gate)
        {
            var lookup = this.tokens.GetAlternateLookup<ReadOnlySpan<char>>();
            for (var i = 0; i < text.Length; i++)
            {
                if (i > 0 && IsWordChar(text[i - 1]) && IsWordChar(text[i])) continue;
                foreach (var length in this.tokenLengths)
                {
                    var end = i + length;
                    if (end > text.Length || (end < text.Length && IsWordChar(text[end - 1]) && IsWordChar(text[end]))) continue;
                    var candidate = text.AsSpan(i, length);
                    if (!lookup.TryGetValue(candidate, out var kind)) continue;
                    var token = candidate.ToString();
                    matches.Add((i, length, kind switch
                    {
                        TokenKind.Path => this.Path(token),
                        TokenKind.Url => this.Url(token),
                        _ => this.Text(token),
                    }));
                    break;
                }
            }
        }
        foreach (Match m in QuotedPattern().Matches(text))
        {
            // The quotes stay; what is inside is a name or a message nobody told us about.
            var inner = m.Groups["inner"];
            matches.Add((inner.Index, inner.Length, this.Path(inner.Value)));
        }
        foreach (Match m in UrlPattern().Matches(text)) matches.Add((m.Index, m.Length, this.Url(m.Value)));
        foreach (Match m in EmailPattern().Matches(text)) matches.Add((m.Index, m.Length, this.Text(m.Value)));
        foreach (Match m in HexPattern().Matches(text))
        {
            if (LooksLikeOid(m.ValueSpan)) matches.Add((m.Index, m.Length, this.Oid(m.Value)));
        }
        if (matches.Count == 0) return text;

        // Earliest first, then longest: a URL that contains a known path is replaced as a URL.
        matches.Sort((a, b) => a.Start != b.Start ? a.Start.CompareTo(b.Start) : b.Length.CompareTo(a.Length));
        var sb = new StringBuilder(text.Length);
        var at = 0;
        foreach (var (start, length, replacement) in matches)
        {
            if (start < at) continue;
            sb.Append(text, at, start - at).Append(replacement);
            at = start + length;
        }
        return sb.Append(text, at, text.Length - at).ToString();
    }

    /// <summary>
    /// A quoted span: git quotes names in its messages ("pathspec 'x' did not
    /// match"), the app quotes the user's text (“summary”). An opening quote
    /// follows no letter, so "don't … it's" is not a span.
    /// </summary>
    [GeneratedRegex(@"(?<![\p{L}\p{N}])(?:'(?<inner>[^'\n]{1,200})'|“(?<inner>[^”\n]{1,200})”|""(?<inner>[^""\n]{1,200})"")(?![\p{L}\p{N}])")]
    private static partial Regex QuotedPattern();

    [GeneratedRegex(@"[A-Za-z][A-Za-z0-9+.\-]*://[^\s'""<>]+")]
    private static partial Regex UrlPattern();

    [GeneratedRegex(@"[A-Za-z0-9._%+\-]+@[A-Za-z0-9.\-]+\.[A-Za-z]{2,}")]
    private static partial Regex EmailPattern();

    [GeneratedRegex(@"\b[0-9a-fA-F]{7,64}\b")]
    private static partial Regex HexPattern();

    /// <summary>
    /// A hex word with both a digit and a letter: words like "defaced" and
    /// numbers like "1234567" are not taken for ids. An all-digit short id is
    /// missed, which reveals little.
    /// </summary>
    private static bool LooksLikeOid(ReadOnlySpan<char> word)
    {
        if (word.Length is < 7 or > 64) return false;
        bool digit = false, letter = false;
        foreach (var c in word)
        {
            if (char.IsAsciiDigit(c)) digit = true;
            else if (char.IsAsciiHexDigit(c)) letter = true;
            else return false;
        }
        return digit && letter;
    }

    // ── The scrambler ────────────────────────────────────────────────

    private static bool IsWordChar(char c) => char.IsLetterOrDigit(c) || char.IsSurrogate(c);

    private string ScrambleWords(string text, bool keepExtension)
    {
        if (text.Length == 0) return text;
        var extensionStart = keepExtension ? ExtensionStart(text) : text.Length;
        var sb = new StringBuilder(text.Length);
        var i = 0;
        while (i < text.Length)
        {
            if (i >= extensionStart)
            {
                sb.Append(text, i, text.Length - i);
                break;
            }
            if (!IsWordStart(text, i))
            {
                sb.Append(text[i++]);
                continue;
            }
            var start = i;
            while (i < text.Length && i < extensionStart && IsWordStart(text, i)) i += char.IsHighSurrogate(text[i]) && i + 1 < text.Length ? 2 : 1;
            var word = text[start..i];
            sb.Append(this.Word(word));
        }
        return sb.ToString();
    }

    /// <summary>A letter or digit, including one written as a surrogate pair.</summary>
    private static bool IsWordStart(string text, int i) =>
        Rune.TryGetRuneAt(text, i, out var rune) ? Rune.IsLetterOrDigit(rune) : false;

    /// <summary>Where the last segment's extension starts (at its dot), or the end if there is none worth keeping.</summary>
    private static int ExtensionStart(string path)
    {
        var segment = path.LastIndexOfAny(['/', '\\']) + 1;
        var dot = path.LastIndexOf('.');
        if (dot < segment || path.Length - dot - 1 is 0 or > MaxExtension) return path.Length;
        for (var i = dot + 1; i < path.Length; i++)
        {
            if (!char.IsAsciiLetterOrDigit(path[i])) return path.Length;
        }
        return dot;
    }

    /// <summary>One word: allowed words and drive letters stay, commit ids map as ids, the rest letter by letter.</summary>
    private string Word(string word)
    {
        if (Allowed.Contains(word)) return word;
        if (LooksLikeOid(word)) return this.Oid(word);
        lock (this.gate)
        {
            if (this.words.TryGetValue(word, out var known)) return known;

            // Keyed on the lower-case form, so Foo, foo and FOO keep their case and share their letters.
            Span<byte> mac = stackalloc byte[32];
            var key = word.ToLowerInvariant();
            this.Hash("word:" + key, mac);
            var result = new StringBuilder(word.Length);
            var block = 0;
            for (var i = 0; i < word.Length; i++)
            {
                if (i > 0 && i % 32 == 0) this.Hash($"word:{++block}:{key}", mac);
                var b = mac[i % 32];
                var c = word[i];
                result.Append(
                    char.IsDigit(c) ? (char)('0' + b % 10)
                    : char.IsUpper(c) ? (char)('A' + b % 26)
                    : (char)('a' + b % 26)); // lower-case, caseless scripts, either half of a surrogate pair
            }
            var scrambled = result.ToString();
            this.words[word] = scrambled;
            return scrambled;
        }
    }

    private void Hash(string input, Span<byte> destination) =>
        HMACSHA256.HashData(this.salt, Encoding.UTF8.GetBytes(input), destination);
}
