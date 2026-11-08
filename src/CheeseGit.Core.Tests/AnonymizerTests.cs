using CheeseGit.Core.Diagnostics;

namespace CheeseGit.Core.Tests;

/// <summary>Problem-report anonymization: consistent within a report, shape-preserving, and leaving git's vocabulary readable.</summary>
public class AnonymizerTests
{
    private static Anonymizer Fixed(byte seed = 1) => new([.. Enumerable.Repeat(seed, 32)]);

    private static void SameShape(string original, string scrambled)
    {
        Assert.Equal(original.Length, scrambled.Length);
        for (var i = 0; i < original.Length; i++)
        {
            var (o, s) = (original[i], scrambled[i]);
            var surrogateLetter = char.IsSurrogate(o) && System.Text.Rune.TryGetRuneAt(original, char.IsLowSurrogate(o) ? i - 1 : i, out var rune) && System.Text.Rune.IsLetter(rune);
            if (char.IsDigit(o)) Assert.True(char.IsAsciiDigit(s), $"{i}: {o} -> {s}");
            else if (char.IsUpper(o)) Assert.True(char.IsAsciiLetterUpper(s), $"{i}: {o} -> {s}");
            else if (char.IsLetter(o) || surrogateLetter) Assert.True(char.IsAsciiLetterLower(s), $"{i}: {o} -> {s}");
            else Assert.Equal(o, s);
        }
    }

    [Theory]
    [InlineData("Hello, World! 42 times\n\tindented_line();")]
    [InlineData("Ünïcødé façade ΑΒΓ δεζ 日本語")]
    [InlineData("emoji 🧀 stays, 𝒳 letters keep their length")]
    public void Text_keeps_length_case_and_punctuation(string text) => SameShape(text, Fixed().Text(text));

    [Fact]
    public void Same_word_maps_the_same_and_case_follows_the_input()
    {
        var a = Fixed();
        var once = a.Text("secret");
        Assert.NotEqual("secret", once);
        Assert.Equal(once, a.Text("secret"));
        Assert.Equal(once.ToUpperInvariant(), a.Text("SECRET"));
        Assert.Equal(char.ToUpperInvariant(once[0]) + once[1..], a.Text("Secret"));
        Assert.Equal($"{once}_{once}.{once}", a.Text("secret_secret.secret"));
    }

    [Fact]
    public void Another_salt_gives_another_mapping()
    {
        Assert.NotEqual(Fixed(1).Text("confidential project"), Fixed(2).Text("confidential project"));
        Assert.NotEqual(new Anonymizer().Text("confidential project"), new Anonymizer().Text("confidential project"));
    }

    [Fact]
    public void Long_words_are_scrambled_past_one_hash_block()
    {
        var word = new string('a', 100);
        var scrambled = Fixed().Text(word);
        SameShape(word, scrambled);
        Assert.NotEqual(scrambled[..32], scrambled[32..64]);
    }

    [Fact]
    public void Paths_keep_their_extension_and_separators()
    {
        var a = Fixed();
        var path = a.Path("src/Secret.Project/Plans.cs");
        SameShape("src/Secret.Project/Plans.cs", path);
        Assert.EndsWith(".cs", path);
        Assert.Equal(a.Text("src/Secret.Project/Plans"), path[..^3]);
        Assert.Equal(a.Text("notes.verysecretext"), a.Path("notes.verysecretext")); // not an extension
        Assert.Equal(a.Text(@"C:\Users\peter\repo") , a.Path(@"C:\Users\peter\repo"));
    }

    [Fact]
    public void Refs_keep_git_vocabulary_and_common_names()
    {
        var a = Fixed();
        Assert.Equal("refs/heads/main", a.Ref("refs/heads/main"));
        Assert.Equal("refs/remotes/origin/HEAD", a.Ref("refs/remotes/origin/HEAD"));
        var feature = a.Ref("refs/heads/feature/login");
        Assert.StartsWith("refs/heads/", feature);
        Assert.Equal("refs/heads/" + a.Ref("feature/login"), feature);
        Assert.NotEqual("refs/heads/feature/login", feature);
    }

    [Fact]
    public void Urls_lose_their_password_and_keep_the_host_kind()
    {
        var a = Fixed();
        var url = a.Url("https://peter:ghp_token123@github.com/acme/rocket.git");
        Assert.StartsWith("https://", url);
        Assert.Contains(":***@github.com/", url);
        Assert.DoesNotContain("ghp_token123", url);
        Assert.DoesNotContain("acme", url);
        Assert.EndsWith(".git", url);
        var scp = a.Url("git@github.com:acme/rocket.git");
        Assert.StartsWith("git@github.com:", scp);
        Assert.EndsWith(a.Text("acme/rocket") + ".git", scp);
    }

    [Fact]
    public void Commit_ids_are_prefix_stable_hex()
    {
        var a = Fixed();
        const string full = "3f2a9c1d8e7b6a5f4e3d2c1b0a9f8e7d6c5b4a39";
        var mapped = a.Oid(full);
        Assert.Equal(full.Length, mapped.Length);
        Assert.True(mapped.All(char.IsAsciiHexDigitLower));
        Assert.NotEqual(full, mapped);
        Assert.StartsWith(a.Oid(full[..7]), mapped);
        Assert.Equal(mapped[..7], a.Text($"reverts {full[..7]}")[8..]); // ids in free text map as ids
    }

    [Fact]
    public void Scrub_replaces_known_tokens_and_leaves_the_rest()
    {
        var a = Fixed();
        a.AddRef("refs/heads/feature/login");
        a.AddPath("src/secret/plan.txt");
        a.AddText("Peter Lenkefi");
        a.AddUrl("https://example.com/acme/rocket.git");

        var line = "Merged feature/login into main by Peter Lenkefi (src/secret/plan.txt, plan.txt)";
        var scrubbed = a.Scrub(line);
        Assert.Equal(
            $"Merged {a.Ref("feature/login")} into main by {a.Text("Peter Lenkefi")} ({a.Path("src/secret/plan.txt")}, {a.Path("plan.txt")})",
            scrubbed);

        Assert.Equal("git fetch --prune " + a.Url("https://example.com/acme/rocket.git"), a.Scrub("git fetch --prune https://example.com/acme/rocket.git"));
        Assert.Equal("Stage all", a.Scrub("Stage all"));
    }

    [Fact]
    public void Scrub_matches_whole_words_only()
    {
        var a = Fixed();
        a.AddText("fix");
        Assert.Equal("prefix fixes " + a.Text("fix") + "!", a.Scrub("prefix fixes fix!"));
    }

    [Fact]
    public void Scrub_finds_emails_urls_and_ids_it_was_not_told_about()
    {
        var a = Fixed();
        var text = "error: hook by jane@corp.example at https://ci.corp.example/job/7 rejected 3f2a9c1d (deadbeef 1234567)";
        var scrubbed = a.Scrub(text);
        Assert.DoesNotContain("jane", scrubbed);
        Assert.DoesNotContain("corp", scrubbed);
        Assert.Contains(a.Oid("3f2a9c1d"), scrubbed);
        Assert.Contains("deadbeef 1234567", scrubbed); // not ids: no digit, no letter
        Assert.StartsWith("error: hook by ", scrubbed);
    }

    [Fact]
    public void Scrub_scrambles_quoted_names_and_messages_it_was_not_told_about()
    {
        var a = Fixed();
        Assert.Equal($"error: pathspec '{a.Path("secret/file.txt")}' did not match", a.Scrub("error: pathspec 'secret/file.txt' did not match"));
        Assert.Equal($"Committed “{a.Text("Fix the launch codes")}” to main", a.Scrub("Committed “Fix the launch codes” to main"));
        Assert.Equal("fatal: 'origin' does not appear to be a git repository", a.Scrub("fatal: 'origin' does not appear to be a git repository"));
        Assert.Equal("don't stage it, it's fine", a.Scrub("don't stage it, it's fine"));
    }

    [Fact]
    public void Paths_added_once_are_found_in_every_form_the_UI_shows()
    {
        var a = Fixed();
        a.AddPath("docs/secret/plan.md");
        Assert.Equal(a.Path("docs/secret"), a.Scrub("docs/secret"));
        Assert.Equal(a.Path("plan.md"), a.Scrub("plan.md"));
        Assert.Equal(a.Path("docs/secret/plan.md"), a.Scrub("docs/secret/plan.md"));
        Assert.EndsWith("/" + a.Path("plan.md"), a.Path("docs/secret/plan.md")); // per segment, so the parts agree
    }

    [Fact]
    public void Public_tokens_are_not_added()
    {
        var a = Fixed();
        a.AddRef("refs/heads/main");
        a.AddRef("refs/remotes/origin/main");
        Assert.Equal("origin/main and main", a.Scrub("origin/main and main"));
    }
}
