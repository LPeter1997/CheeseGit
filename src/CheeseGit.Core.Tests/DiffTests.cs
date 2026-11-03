using System.Text;
using CheeseGit.Core.Diff;
using CheeseGit.Core.Git;

namespace CheeseGit.Core.Tests;

/// <summary>Diff parsing, inline highlights and partial patches (ports of build_partial_patch.rs and the provider's unit tests).</summary>
public class DiffTests
{
    private static DiffLine Ctx(string s, int o, int n) => new(DiffLineKind.Context, Encoding.UTF8.GetBytes(s), o, n);

    private static DiffLine Add(string s, int n) => new(DiffLineKind.Addition, Encoding.UTF8.GetBytes(s), 0, n);

    private static DiffLine Del(string s, int o) => new(DiffLineKind.Deletion, Encoding.UTF8.GetBytes(s), o, 0);

    private static FileDiff Diff(string path, params DiffHunk[] hunks) => FileDiff.Empty(path) with { Hunks = hunks };

    private static DiffHunk Hunk(int oldStart, int oldCount, int newStart, int newCount, params DiffLine[] lines) =>
        new(oldStart, oldCount, newStart, newCount, "", lines);

    private static string Build(FileDiff diff, PatchBase @base, params (int H, int L)[] sel) =>
        PartialPatch.Build(diff, sel.Select(s => new LineSelection(s.H, s.L)), @base) is { } p ? Encoding.UTF8.GetString(p) : "";

    private static List<DiffHunk> ParseHunks(string raw) =>
        DiffParser.Parse(Encoding.UTF8.GetBytes("diff --git a/f b/f\n--- a/f\n+++ b/f\n" + raw)).Single().Hunks.ToList();

    // ── Partial patches (build_partial_patch.rs) ─────────────────────

    [Fact]
    public void Stage_single_addition()
    {
        var diff = Diff("file.txt", Hunk(1, 3, 1, 4, Ctx("line1", 1, 1), Add("new line", 2), Ctx("line2", 2, 3), Ctx("line3", 3, 4)));
        var patch = Build(diff, PatchBase.OldSide, (0, 1));
        Assert.Contains("--- a/file.txt", patch);
        Assert.Contains("+++ b/file.txt", patch);
        Assert.Contains("@@ -1,3 +1,4 @@", patch);
        Assert.Contains("+new line\n", patch);
        Assert.Contains(" line1\n", patch);
    }

    [Fact]
    public void Stage_single_deletion()
    {
        var diff = Diff("file.txt", Hunk(1, 3, 1, 2, Ctx("line1", 1, 1), Del("removed", 2), Ctx("line3", 3, 2)));
        var patch = Build(diff, PatchBase.OldSide, (0, 1));
        Assert.Contains("@@ -1,3 +1,2 @@", patch);
        Assert.Contains("-removed\n", patch);
    }

    [Fact]
    public void Unselected_addition_is_omitted()
    {
        var diff = Diff("file.txt", Hunk(1, 2, 1, 4, Ctx("line1", 1, 1), Add("selected", 2), Add("not selected", 3), Ctx("line2", 2, 4)));
        var patch = Build(diff, PatchBase.OldSide, (0, 1));
        Assert.Contains("+selected\n", patch);
        Assert.DoesNotContain("not selected", patch);
        Assert.Contains("@@ -1,2 +1,3 @@", patch);
    }

    [Fact]
    public void Unselected_deletion_becomes_context()
    {
        var diff = Diff("file.txt", Hunk(1, 3, 1, 1, Del("keep as context", 1), Del("actually remove", 2), Ctx("line3", 3, 1)));
        var patch = Build(diff, PatchBase.OldSide, (0, 1));
        Assert.Contains(" keep as context\n", patch);
        Assert.Contains("-actually remove\n", patch);
        Assert.Contains("@@ -1,3 +1,2 @@", patch);
    }

    [Fact]
    public void Empty_selection_produces_no_patch()
    {
        var diff = Diff("file.txt", Hunk(1, 2, 1, 3, Ctx("a", 1, 1), Add("b", 2), Ctx("c", 2, 3)));
        Assert.Null(PartialPatch.Build(diff, [], PatchBase.OldSide));
        // Selecting a context line is not a selection either.
        Assert.Null(PartialPatch.Build(diff, [new LineSelection(0, 0)], PatchBase.OldSide));
    }

    [Fact]
    public void Multiple_hunks_only_selected_emitted()
    {
        var diff = Diff(
            "file.txt",
            Hunk(1, 2, 1, 3, Ctx("a", 1, 1), Add("b", 2), Ctx("c", 2, 3)),
            Hunk(10, 2, 11, 3, Ctx("x", 10, 11), Add("y", 12), Ctx("z", 11, 13)));
        var patch = Build(diff, PatchBase.OldSide, (1, 1));
        Assert.DoesNotContain("@@ -1,", patch);
        Assert.Contains("@@ -10,", patch);
        Assert.Contains("+y\n", patch);
    }

    [Fact]
    public void Unstage_single_addition()
    {
        var diff = Diff("file.txt", Hunk(1, 2, 1, 3, Ctx("line1", 1, 1), Add("staged line", 2), Ctx("line2", 2, 3)));
        var patch = Build(diff, PatchBase.NewSide, (0, 1));
        Assert.Contains("+staged line\n", patch);
        Assert.Contains("@@ -1,2 +1,3 @@", patch);
    }

    [Fact]
    public void Unstage_one_addition_among_multiple()
    {
        var diff = Diff("file.txt", Hunk(1, 1, 1, 3, Ctx("line1", 1, 1), Add("keep staged", 2), Add("unstage this", 3)));
        var patch = Build(diff, PatchBase.NewSide, (0, 2));
        Assert.Contains(" keep staged\n", patch);
        Assert.Contains("+unstage this\n", patch);
        Assert.Contains("@@ -1,2 +1,3 @@", patch);
    }

    [Fact]
    public void Unstage_deletion_omits_non_selected_deletions()
    {
        var diff = Diff("file.txt", Hunk(1, 3, 1, 1, Del("unstage this", 1), Del("keep staged", 2), Ctx("line3", 3, 1)));
        var patch = Build(diff, PatchBase.NewSide, (0, 0));
        Assert.Contains("-unstage this\n", patch);
        Assert.DoesNotContain("keep staged", patch);
        Assert.Contains("@@ -1,2 +1,1 @@", patch);
    }

    // The Rust suite pinned the '+' start to the original hunk's new start.
    // That is only right when every earlier hunk is applied too; the partial
    // patch skips unselected hunks, so the side the target does not hold is
    // shifted by the hunks actually emitted. The base side stays exact.

    [Fact]
    public void Base_side_start_is_exact_and_other_side_follows_emitted_hunks()
    {
        var diff = Diff("file.txt", Hunk(10, 3, 12, 4, Ctx("context1", 10, 12), Ctx("context2", 11, 13), Add("new line", 14), Ctx("context3", 12, 15)));
        Assert.Contains("@@ -10,3 +10,4 @@", Build(diff, PatchBase.OldSide, (0, 2)));
        Assert.Contains("@@ -12,3 +12,4 @@", Build(diff, PatchBase.NewSide, (0, 2)));
    }

    [Fact]
    public void Each_hunk_header_accounts_for_the_hunks_before_it()
    {
        var diff = Diff(
            "app.rs",
            Hunk(5, 2, 5, 3, Ctx("fn main() {", 5, 5), Add("    println!(\"hello\");", 6), Ctx("}", 6, 7)),
            Hunk(20, 2, 21, 3, Ctx("fn other() {", 20, 21), Add("    dbg!(x);", 22), Ctx("}", 21, 23)));
        Assert.Contains("@@ -20,2 +20,3 @@", Build(diff, PatchBase.OldSide, (1, 1)));
        var both = Build(diff, PatchBase.OldSide, (0, 1), (1, 1));
        Assert.Contains("@@ -5,2 +5,3 @@", both);
        Assert.Contains("@@ -20,2 +21,3 @@", both);
    }

    // ── Parsing ───────────────────────────────────────────────────────

    [Fact]
    public void Parses_hunks_with_line_numbers_and_no_newline_markers()
    {
        var raw = "@@ -1,2 +1,2 @@ heading\n a\n-b\n\\ No newline at end of file\n+c\n\\ No newline at end of file\n";
        var hunk = Assert.Single(ParseHunks(raw));
        Assert.Equal((1, 2, 1, 2), (hunk.OldStart, hunk.OldCount, hunk.NewStart, hunk.NewCount));
        Assert.Equal("heading", hunk.Heading);
        Assert.Equal(3, hunk.Lines.Count);
        Assert.Equal((1, 1), (hunk.Lines[0].OldLine, hunk.Lines[0].NewLine));
        Assert.True(hunk.Lines[1].NoNewlineAtEnd);
        Assert.True(hunk.Lines[2].NoNewlineAtEnd);
        Assert.Equal(2, hunk.Lines[2].NewLine);
    }

    [Fact]
    public void Content_that_looks_like_a_header_stays_content()
    {
        var raw = "@@ -1,2 +1,2 @@\n-diff --git a/x b/x\n+@@ -1 +1 @@\n context\n";
        var lines = Assert.Single(ParseHunks(raw)).Lines;
        Assert.Equal("diff --git a/x b/x", lines[0].Text);
        Assert.Equal("@@ -1 +1 @@", lines[1].Text);
        Assert.Equal(DiffLineKind.Context, lines[2].Kind);
    }

    [Fact]
    public void Parses_paths_renames_modes_and_binaries()
    {
        var raw =
            "diff --git a/with space.txt b/with space.txt\nnew file mode 100755\nindex 0000000..e69de29\n--- /dev/null\n+++ b/with space.txt\t\n@@ -0,0 +1 @@\n+x\n" +
            "diff --git \"a/tab\\there\" \"b/tab\\there\"\nindex 1..2 100644\n--- \"a/tab\\there\"\n+++ \"b/tab\\there\"\n@@ -1 +1 @@\n-a\n+b\n" +
            "diff --git a/old name b/new name\nsimilarity index 100%\nrename from old name\nrename to new name\n" +
            "diff --git a/img.png b/img.png\nindex 1..2 100644\nBinary files a/img.png and b/img.png differ\n" +
            "diff --git a/gone b/gone\ndeleted file mode 100644\nindex 1..0\n--- a/gone\n+++ /dev/null\n@@ -1 +0,0 @@\n-bye\n";
        var files = DiffParser.Parse(Encoding.UTF8.GetBytes(raw));
        Assert.Equal(5, files.Count);
        Assert.Equal(("with space.txt", DiffChange.Added, "100755"), (files[0].Path.ToString(), files[0].Change, files[0].NewMode));
        Assert.Equal("tab\there", files[1].Path.ToString());
        Assert.Equal("100644", files[1].OldMode);
        Assert.Equal(("new name", "old name", DiffChange.Renamed), (files[2].Path.ToString(), files[2].OldPath.ToString(), files[2].Change));
        Assert.True(files[3].IsBinary);
        Assert.Equal((DiffChange.Deleted, "gone"), (files[4].Change, files[4].Path.ToString()));
    }

    [Fact]
    public void Blank_context_lines_survive_suppress_blank_empty()
    {
        var lines = Assert.Single(ParseHunks("@@ -1,3 +1,3 @@\n a\n\n-b\n+c\n")).Lines;
        Assert.Equal(4, lines.Count);
        Assert.Equal(DiffLineKind.Context, lines[1].Kind);
        Assert.Empty(lines[1].Content);
    }

    // ── Inline highlights (provider.rs unit tests) ───────────────────

    [Fact]
    public void Inline_highlights_paired_modification()
    {
        var lines = Assert.Single(ParseHunks("@@ -1,1 +1,1 @@\n-const result = calculateSum(a, b);\n+const result = calculateDiff(a, b);\n")).Lines;
        Assert.NotEmpty(lines[0].Highlights);
        Assert.True(lines[0].Highlights.Sum(h => h.Length) < lines[0].Content.Length);
        Assert.NotEmpty(lines[1].Highlights);
        Assert.True(lines[1].Highlights.Sum(h => h.Length) < lines[1].Content.Length);
    }

    [Fact]
    public void Inline_highlights_pure_addition_and_deletion_have_none()
    {
        Assert.Empty(ParseHunks("@@ -1,1 +1,2 @@\n context\n+added line\n")[0].Lines[1].Highlights);
        Assert.Empty(ParseHunks("@@ -1,2 +1,1 @@\n context\n-deleted line\n")[0].Lines[1].Highlights);
    }

    [Fact]
    public void Inline_highlights_entirely_different_lines_have_none()
    {
        var lines = ParseHunks("@@ -1,1 +1,1 @@\n-abcdef\n+uvwxyz\n")[0].Lines;
        Assert.Empty(lines[0].Highlights);
        Assert.Empty(lines[1].Highlights);
    }

    [Fact]
    public void Inline_highlights_partial_change_correct_spans()
    {
        var lines = ParseHunks("@@ -1,1 +1,1 @@\n-return a + b;\n+return a - b;\n")[0].Lines;
        Assert.Equal([new InlineHighlight(9, 1)], lines[0].Highlights);
        Assert.Equal([new InlineHighlight(9, 1)], lines[1].Highlights);
    }

    [Fact]
    public void Inline_highlights_unequal_blocks_pair_first_n()
    {
        var lines = ParseHunks("@@ -1,2 +1,3 @@\n-old line 1\n-old line 2\n+new line 1\n+new line 2\n+completely new line\n")[0].Lines;
        Assert.NotEmpty(lines[0].Highlights);
        Assert.NotEmpty(lines[2].Highlights);
        Assert.NotEmpty(lines[1].Highlights);
        Assert.NotEmpty(lines[3].Highlights);
        Assert.Empty(lines[4].Highlights);
    }

    [Fact]
    public void Inline_highlights_context_lines_have_none()
    {
        var lines = ParseHunks("@@ -1,3 +1,3 @@\n context before\n-old\n+new\n context after\n")[0].Lines;
        Assert.Empty(lines[0].Highlights);
        Assert.Empty(lines[3].Highlights);
    }

    [Fact]
    public void Inline_highlights_low_similarity_skipped()
    {
        var lines = ParseHunks("@@ -1,1 +1,1 @@\n-    <span className={`flex-shrink-0 font-mono text-xs`}>\n+    <FileStatusBadge status={entry.status} variant=\"inline\" />\n")[0].Lines;
        Assert.Empty(lines[0].Highlights);
        Assert.Empty(lines[1].Highlights);
    }

    [Fact]
    public void Inline_highlights_merge_over_whitespace()
    {
        var add = ParseHunks("@@ -1,1 +1,1 @@\n-// entire line is highlighted it adds no information.\n+// entire line is highlighted it adds no useful information.\n")[0].Lines[1];
        for (var i = 1; i < add.Highlights.Count; i++)
        {
            var prevEnd = add.Highlights[i - 1].Start + add.Highlights[i - 1].Length;
            var gap = Encoding.UTF8.GetString(add.Content[prevEnd..add.Highlights[i].Start]);
            Assert.False(string.IsNullOrWhiteSpace(gap), $"whitespace-only gap '{gap}' should have been merged");
        }
    }

    [Fact]
    public void Merge_highlights_over_whitespace_basic_and_non_whitespace_gaps()
    {
        var merged = InlineHighlighter.MergeOverWhitespace([new(0, 5), new(6, 5)], "hello cruel world"u8);
        Assert.Equal([new InlineHighlight(0, 11)], merged);
        Assert.Equal(2, InlineHighlighter.MergeOverWhitespace([new(0, 1), new(3, 1)], "aXXb"u8).Count);
    }

    // ── Round trips through real git apply ───────────────────────────

    private static async Task<FileDiff> GitDiff(TempRepo repo, params string[] args)
    {
        var result = await repo.Git.RunCheckedAsync("diff", repo.Path, ["diff", .. DiffParser.PatchArgs, .. args]);
        return DiffParser.Parse(result.Stdout).Single();
    }

    private static async Task Apply(TempRepo repo, byte[] patch, params string[] args) =>
        await repo.Git.RunCheckedAsync("apply", repo.Path, ["apply", "--whitespace=nowarn", .. args], GitAccess.Write, patch);

    private static IEnumerable<LineSelection> Changes(FileDiff diff, Func<DiffLine, bool> predicate) =>
        diff.Hunks.SelectMany((h, hi) => h.Lines.Select((l, li) => (l, hi, li))).Where(t => t.l.IsChange && predicate(t.l)).Select(t => new LineSelection(t.hi, t.li));

    private static string Lines(int from, int to, Func<int, string>? f = null) =>
        string.Concat(Enumerable.Range(from, to - from + 1).Select(i => (f?.Invoke(i) ?? $"line{i}") + "\n"));

    [Fact]
    public async Task Staging_the_second_of_two_hunks_applies_to_the_index()
    {
        using var repo = TempRepo.Init();
        repo.CommitFile("f.txt", Lines(1, 30), "init");
        repo.Write("f.txt", Lines(1, 30, i => i switch { 2 => "line2\nadded-top", 25 => "changed25", _ => $"line{i}" }));

        var diff = await GitDiff(repo, "--", "f.txt");
        Assert.Equal(2, diff.Hunks.Count);
        var patch = PartialPatch.Build(diff, Changes(diff, l => l.Text.Contains("25")), PatchBase.OldSide)!;
        await Apply(repo, patch, "--cached");

        var staged = repo.Raw("show", ":f.txt").Replace("\r\n", "\n");
        Assert.Equal(Lines(1, 30, i => i == 25 ? "changed25" : $"line{i}"), staged);
    }

    [Fact]
    public async Task Unstaging_the_second_of_two_hunks_applies_in_reverse()
    {
        using var repo = TempRepo.Init();
        repo.CommitFile("f.txt", Lines(1, 30), "init");
        repo.Write("f.txt", Lines(1, 30, i => i switch { 2 => "line2\nadded-top", 25 => "changed25", _ => $"line{i}" }));
        repo.Raw("add", "f.txt");

        var diff = await GitDiff(repo, "--cached", "--", "f.txt");
        var patch = PartialPatch.Build(diff, Changes(diff, l => l.Text.Contains("25")), PatchBase.NewSide)!;
        await Apply(repo, patch, "--cached", "--reverse");

        var staged = repo.Raw("show", ":f.txt").Replace("\r\n", "\n");
        Assert.Equal(Lines(1, 30, i => i == 2 ? "line2\nadded-top" : $"line{i}"), staged);
    }

    [Fact]
    public async Task Discarding_some_worktree_lines_keeps_the_others()
    {
        // The old app built this patch against the wrong side; it only worked
        // when the selection happened to cover every line of a kind.
        using var repo = TempRepo.Init();
        repo.CommitFile("f.txt", "a\nb\nc\n", "init");
        repo.Write("f.txt", "a\nkeep-1\ndrop\nkeep-2\nc\n");

        var diff = await GitDiff(repo, "--", "f.txt");
        var patch = PartialPatch.Build(diff, Changes(diff, l => l.Text is "drop" or "b"), PatchBase.NewSide)!;
        await Apply(repo, patch, "--reverse");

        // Discarding a deletion and an addition together undoes that
        // replacement: "b" comes back where "drop" was.
        Assert.Equal("a\nkeep-1\nb\nkeep-2\nc\n", repo.ReadNormalized("f.txt"));
    }

    [Fact]
    public async Task Missing_final_newline_and_crlf_survive_partial_staging()
    {
        using var repo = TempRepo.Init();
        repo.Write(".gitattributes", "*.txt -text\n");
        repo.CommitFile("f.txt", "one\r\ntwo\r\nthree", "init");
        repo.Write("f.txt", "one\r\nTWO\r\nthree\r\nfour");

        var diff = await GitDiff(repo, "--", "f.txt");
        Assert.Contains(diff.Hunks.SelectMany(h => h.Lines), l => l.NoNewlineAtEnd);
        await Apply(repo, PartialPatch.Build(diff, Changes(diff, _ => true), PatchBase.OldSide)!, "--cached");
        Assert.Equal("one\r\nTWO\r\nthree\r\nfour", repo.Raw("show", ":f.txt"));

        // Partial: only the TWO change.
        repo.Raw("reset", "-q");
        var partial = PartialPatch.Build(diff, Changes(diff, l => l.Text is "two" or "TWO"), PatchBase.OldSide)!;
        await Apply(repo, partial, "--cached");
        Assert.Equal("one\r\nTWO\r\nthree", repo.Raw("show", ":f.txt"));
    }

    [Fact]
    public async Task Untracked_file_lines_stage_as_a_new_file_and_unstage_partially()
    {
        using var repo = TempRepo.Init();
        repo.CommitFile("base.txt", "x\n", "init");
        repo.Write("new.txt", "a\nb\nc\n");

        var result = await repo.Git.RunAsync(repo.Path, ["diff", .. DiffParser.PatchArgs, "--no-index", "--", "/dev/null", "new.txt"]);
        var diff = DiffParser.Parse(result.Stdout).Single();
        Assert.Equal(DiffChange.Added, diff.Change);
        await Apply(repo, PartialPatch.Build(diff, Changes(diff, l => l.Text != "b"), PatchBase.OldSide)!, "--cached");
        Assert.Equal("a\nc\n", repo.Raw("show", ":new.txt").Replace("\r\n", "\n"));

        var staged = await GitDiff(repo, "--cached", "--", "new.txt");
        Assert.Equal(DiffChange.Added, staged.Change);
        await Apply(repo, PartialPatch.Build(staged, Changes(staged, l => l.Text == "a"), PatchBase.NewSide)!, "--cached", "--reverse");
        Assert.Equal("c\n", repo.Raw("show", ":new.txt").Replace("\r\n", "\n"));
    }

    [Fact]
    public async Task Hostile_path_round_trips_through_diff_and_apply()
    {
        if (OperatingSystem.IsWindows()) return;
        const string name = "dir with space/quote\"tab\tü.txt";
        using var repo = TempRepo.Init();
        repo.CommitFile(name, "a\nb\n", "init");
        repo.Write(name, "a\nB\n");

        var diff = await GitDiff(repo, "--", name);
        Assert.Equal(name, diff.Path.ToString());
        await Apply(repo, PartialPatch.Build(diff, Changes(diff, _ => true), PatchBase.OldSide)!, "--cached");
        Assert.Equal("a\nB\n", repo.Raw("show", ":" + name).Replace("\r\n", "\n"));
    }
}
