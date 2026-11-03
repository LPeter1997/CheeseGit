using CheeseGit.Core.Diff;
using CheeseGit.Core.Git;
using CheeseGit.Core.Repository;

namespace CheeseGit.Core.Tests;

/// <summary>Port of git_provider.rs: diff/commit stats, graph stats, discarding.</summary>
public class StatsAndDiscardTests
{
    private static FileStats Stat(IReadOnlyList<FileStats> stats, string path) => Assert.Single(stats, s => s.Path.ToString() == path);

    // ── Diff stats ────────────────────────────────────────────────────

    [Fact]
    public async Task Diff_stats_unstaged_modification()
    {
        using var repo = RepositoryTests.RepoWithCommit();
        repo.Write("hello.txt", "line1\nline2\nline3\n");
        using var git = await repo.OpenAsync();
        var stats = await git.DiffStatsAsync(DiffArea.Unstaged);
        Assert.Equal(new FileStats("hello.txt", 3, 1), Assert.Single(stats));
    }

    [Fact]
    public async Task Diff_stats_staged_modification()
    {
        using var repo = RepositoryTests.RepoWithCommit();
        repo.Write("hello.txt", "changed content\n");
        repo.Raw("add", "hello.txt");
        using var git = await repo.OpenAsync();
        Assert.Equal(new FileStats("hello.txt", 1, 1), Assert.Single(await git.DiffStatsAsync(DiffArea.Staged)));
    }

    [Fact]
    public async Task Diff_stats_staged_new_file()
    {
        using var repo = RepositoryTests.RepoWithCommit();
        repo.Write("new.txt", "a\nb\nc\n");
        repo.Raw("add", "new.txt");
        using var git = await repo.OpenAsync();
        Assert.Equal(new FileStats("new.txt", 3, 0), Assert.Single(await git.DiffStatsAsync(DiffArea.Staged)));
    }

    [Fact]
    public async Task Diff_stats_staged_deleted_file()
    {
        using var repo = RepositoryTests.RepoWithCommit();
        repo.Delete("hello.txt");
        repo.Raw("add", "hello.txt");
        using var git = await repo.OpenAsync();
        Assert.Equal(new FileStats("hello.txt", 0, 1), Assert.Single(await git.DiffStatsAsync(DiffArea.Staged)));
    }

    [Fact]
    public async Task Diff_stats_multiple_files()
    {
        using var repo = RepositoryTests.RepoWithCommit();
        repo.Write("hello.txt", "modified\n");
        repo.Write("another.txt", "new file\n");
        using var git = await repo.OpenAsync();
        var stats = await git.DiffStatsAsync(DiffArea.Unstaged);
        Stat(stats, "hello.txt");
        Assert.Equal(1, Stat(stats, "another.txt").Additions);
    }

    [Fact]
    public async Task Diff_stats_count_only_the_untracked_files_the_caller_passes()
    {
        using var repo = RepositoryTests.RepoWithCommit();
        repo.Write("hello.txt", "modified\n");
        repo.Write("a.txt", "one\ntwo\n");
        repo.Write("b.txt", "one\n");
        using var git = await repo.OpenAsync();
        var stats = await git.DiffStatsAsync(DiffArea.Unstaged, [GitPath.FromString("a.txt")]);
        Stat(stats, "hello.txt");
        Assert.Equal(2, Stat(stats, "a.txt").Additions);
        Assert.DoesNotContain(stats, s => s.Path.ToString() == "b.txt");
        Assert.DoesNotContain(await git.DiffStatsAsync(DiffArea.Unstaged, []), s => s.Path.ToString() == "a.txt");
    }

    [Fact]
    public async Task Diff_stats_empty_when_clean()
    {
        using var repo = RepositoryTests.RepoWithCommit();
        using var git = await repo.OpenAsync();
        Assert.Empty(await git.DiffStatsAsync(DiffArea.Unstaged));
        Assert.Empty(await git.DiffStatsAsync(DiffArea.Staged));
    }

    [Fact]
    public async Task Diff_stats_include_untracked_files()
    {
        using var repo = RepositoryTests.RepoWithCommit();
        repo.Write("new_file.txt", "line1\nline2\nline3\nline4\nline5\n");
        repo.Write("no-final-newline.txt", "a\nb");
        repo.Write("binary.bin", new byte[] { 1, 0, 2 });
        using var git = await repo.OpenAsync();
        var stats = await git.DiffStatsAsync(DiffArea.Unstaged);
        Assert.Equal(new FileStats("new_file.txt", 5, 0), Stat(stats, "new_file.txt"));
        Assert.Equal(2, Stat(stats, "no-final-newline.txt").Additions);
        Assert.True(Stat(stats, "binary.bin").IsBinary);
    }

    [Fact]
    public async Task Diff_stats_include_deleted_files()
    {
        using var repo = RepositoryTests.RepoWithCommit();
        repo.Delete("hello.txt");
        using var git = await repo.OpenAsync();
        var stat = Stat(await git.DiffStatsAsync(DiffArea.Unstaged), "hello.txt");
        Assert.Equal(0, stat.Additions);
        Assert.True(stat.Deletions > 0);
    }

    [Fact]
    public async Task Diff_stats_untracked_contribute_to_totals()
    {
        using var repo = RepositoryTests.RepoWithCommit();
        repo.Write("hello.txt", "hello\nextra line\n");
        repo.Write("untracked.txt", "one\ntwo\nthree\n");
        using var git = await repo.OpenAsync();
        var stats = await git.DiffStatsAsync(DiffArea.Unstaged);
        Stat(stats, "hello.txt");
        Stat(stats, "untracked.txt");
        Assert.True(stats.Sum(s => s.Additions) >= 4);
    }

    // ── Commit stats ──────────────────────────────────────────────────

    [Fact]
    public async Task Commit_file_stats_initial_commit()
    {
        using var repo = RepositoryTests.RepoWithCommit();
        using var git = await repo.OpenAsync();
        var stats = await git.CommitFileStatsAsync(Oid.ParseHex(repo.Head()));
        Assert.Equal(new FileStats("hello.txt", 1, 0), Assert.Single(stats));
    }

    [Fact]
    public async Task Commit_file_stats_modification_commit()
    {
        using var repo = RepositoryTests.RepoWithCommit();
        repo.CommitFile("hello.txt", "line1\nline2\n", "modify hello");
        using var git = await repo.OpenAsync();
        Assert.Equal(new FileStats("hello.txt", 2, 1), Assert.Single(await git.CommitFileStatsAsync(Oid.ParseHex(repo.Head()))));
    }

    [Fact]
    public async Task Commit_file_stats_multiple_files()
    {
        using var repo = RepositoryTests.RepoWithCommit();
        repo.Write("hello.txt", "updated\n");
        repo.Write("a.txt", "aaa\n");
        repo.Write("b.txt", "bbb\nccc\n");
        repo.CommitAll("multi-file commit");
        using var git = await repo.OpenAsync();
        var stats = await git.CommitFileStatsAsync(Oid.ParseHex(repo.Head()));
        Assert.Equal(3, stats.Count);
        Assert.Equal((1, 0), (Stat(stats, "a.txt").Additions, Stat(stats, "a.txt").Deletions));
        Assert.Equal((2, 0), (Stat(stats, "b.txt").Additions, Stat(stats, "b.txt").Deletions));
        Assert.Equal((1, 1), (Stat(stats, "hello.txt").Additions, Stat(stats, "hello.txt").Deletions));
    }

    [Fact]
    public async Task Commit_file_stats_deletion_commit()
    {
        using var repo = RepositoryTests.RepoWithCommit();
        repo.Delete("hello.txt");
        repo.CommitAll("delete hello");
        using var git = await repo.OpenAsync();
        Assert.Equal(new FileStats("hello.txt", 0, 1), Assert.Single(await git.CommitFileStatsAsync(Oid.ParseHex(repo.Head()))));
    }

    [Fact]
    public async Task List_commit_files_reports_kinds_and_renames()
    {
        using var repo = RepositoryTests.RepoWithCommit();
        repo.CommitFile("other.txt", "some content that is long enough to be detected as a rename\n", "add");
        repo.Raw("mv", "other.txt", "moved.txt");
        repo.Delete("hello.txt");
        repo.Write("added.txt", "x\n");
        repo.CommitAll("mixed");
        using var git = await repo.OpenAsync();
        var files = await git.ListCommitFilesAsync(Oid.ParseHex(repo.Head()));
        Assert.Contains(new FileChange("moved.txt", FileStatus.Renamed, "other.txt"), files);
        Assert.Contains(new FileChange("hello.txt", FileStatus.Deleted), files);
        Assert.Contains(new FileChange("added.txt", FileStatus.Added), files);
    }

    [Fact]
    public async Task Graph_stats_latest_commit()
    {
        using var repo = RepositoryTests.RepoWithCommit();
        repo.CommitFile("hello.txt", "line1\nline2\nline3\n", "add lines");
        using var git = await repo.OpenAsync();
        var graph = await git.LoadHistoryAsync(new HistoryRequest());
        var latest = graph.Dag.OidAt(0);
        Assert.Equal("add lines", git.GetCommit(latest)!.Summary);
        Assert.Equal(new CommitStats(3, 1, 1), (await git.CommitStatsAsync([latest]))[latest]);
    }

    [Fact]
    public async Task Graph_stats_initial_and_empty_commits()
    {
        using var repo = RepositoryTests.RepoWithCommit();
        var initial = Oid.ParseHex(repo.Head());
        repo.Raw("commit", "-q", "--allow-empty", "-m", "empty");
        var empty = Oid.ParseHex(repo.Head());
        using var git = await repo.OpenAsync();
        var stats = await git.CommitStatsAsync([initial, empty]);
        Assert.Equal(new CommitStats(1, 0, 1), stats[initial]);
        Assert.Equal(new CommitStats(0, 0, 0), stats[empty]);
    }

    [Fact]
    public async Task Graph_stats_with_multiple_commits()
    {
        using var repo = RepositoryTests.RepoWithCommit();
        for (var i = 1; i < 5; i++) repo.CommitFile("hello.txt", $"content {i}", $"commit {i}");
        using var git = await repo.OpenAsync();
        var dag = (await git.LoadHistoryAsync(new HistoryRequest())).Dag;
        Assert.Equal(5, dag.Count);
        var oids = Enumerable.Range(0, dag.Count).Select(dag.OidAt).ToList();
        var stats = await git.CommitStatsAsync(oids);
        Assert.All(oids, o => Assert.True(stats[o].Insertions > 0));
    }

    [Fact]
    public async Task Merge_commit_stats_are_against_the_first_parent()
    {
        using var repo = RepositoryTests.RepoWithCommit();
        var main = repo.CurrentBranch();
        repo.Raw("switch", "-q", "-c", "side");
        repo.CommitFile("side.txt", "1\n2\n", "side");
        repo.Raw("switch", "-q", main);
        repo.Raw("merge", "-q", "--no-ff", "-m", "merge side", "side");
        using var git = await repo.OpenAsync();
        var merge = Oid.ParseHex(repo.Head());
        Assert.Equal(new CommitStats(2, 0, 1), (await git.CommitStatsAsync([merge]))[merge]);
        Assert.Single(await git.DiffCommitAsync(merge));
    }

    // ── Discarding ────────────────────────────────────────────────────

    [Fact]
    public async Task Discard_unstaged_tracked_file()
    {
        using var repo = RepositoryTests.RepoWithCommit();
        repo.Write("hello.txt", "modified");
        using var git = await repo.OpenAsync();
        await git.DiscardUnstagedAsync(["hello.txt"]);
        Assert.Equal("hello", repo.ReadNormalized("hello.txt"));
    }

    [Fact]
    public async Task Discard_unstaged_untracked_file()
    {
        using var repo = RepositoryTests.RepoWithCommit();
        repo.Write("new.txt", "new content");
        using var git = await repo.OpenAsync();
        await git.DiscardUnstagedAsync(["new.txt"]);
        Assert.False(repo.Exists("new.txt"));
    }

    [Fact]
    public async Task Discard_staged_file()
    {
        using var repo = RepositoryTests.RepoWithCommit();
        repo.Write("hello.txt", "staged change");
        repo.Raw("add", "hello.txt");
        using var git = await repo.OpenAsync();
        await git.DiscardStagedAsync(["hello.txt"]);
        Assert.Equal("hello", repo.ReadNormalized("hello.txt"));
        var status = await git.StatusAsync();
        Assert.Empty(status.Staged);
        Assert.Empty(status.Unstaged);
    }

    [Fact]
    public async Task Discard_staged_new_file_deletes_it()
    {
        using var repo = RepositoryTests.RepoWithCommit();
        repo.Write("new.txt", "new");
        repo.Raw("add", "new.txt");
        using var git = await repo.OpenAsync();
        await git.DiscardStagedAsync(["new.txt"]);
        Assert.False(repo.Exists("new.txt"));
        Assert.Empty((await git.StatusAsync()).Entries);
    }

    [Fact]
    public async Task Discard_unstaged_lines()
    {
        using var repo = RepositoryTests.RepoWithCommit();
        repo.CommitFile("hello.txt", "line1\nline2\nline3\n", "multiline");
        repo.Write("hello.txt", "line1\nline2\nline3\nline4\n");
        using var git = await repo.OpenAsync();
        var diff = await git.DiffFileAsync("hello.txt", DiffArea.Unstaged);
        var add = diff.Hunks[0].Lines.ToList().FindIndex(l => l.Kind == DiffLineKind.Addition);
        await git.DiscardLinesAsync(diff, [new LineSelection(0, add)], DiffArea.Unstaged);
        Assert.Equal("line1\nline2\nline3\n", repo.ReadNormalized("hello.txt"));
    }

    [Fact]
    public async Task Discard_staged_lines_changes_index_and_worktree()
    {
        using var repo = RepositoryTests.RepoWithCommit();
        repo.CommitFile("hello.txt", "a\nb\n", "base");
        repo.Write("hello.txt", "a\nx\nb\ny\n");
        repo.Raw("add", "hello.txt");
        using var git = await repo.OpenAsync();
        var diff = await git.DiffFileAsync("hello.txt", DiffArea.Staged);
        var lines = diff.Hunks[0].Lines.ToList();
        await git.DiscardLinesAsync(diff, [new LineSelection(0, lines.FindIndex(l => l.Text == "x"))], DiffArea.Staged);
        Assert.Equal("a\nb\ny\n", repo.Raw("show", ":hello.txt").Replace("\r\n", "\n"));
        Assert.Equal("a\nb\ny\n", repo.ReadNormalized("hello.txt"));
    }

    [Fact]
    public async Task Stage_and_unstage_lines()
    {
        using var repo = RepositoryTests.RepoWithCommit();
        repo.CommitFile("hello.txt", "a\nb\nc\n", "base");
        repo.Write("hello.txt", "a\nB\nc\nd\n");
        using var git = await repo.OpenAsync();
        var diff = await git.DiffFileAsync("hello.txt", DiffArea.Unstaged);
        var lines = diff.Hunks[0].Lines.ToList();
        await git.StageLinesAsync(diff, [new LineSelection(0, lines.FindIndex(l => l.Text == "d"))]);
        Assert.Equal("a\nb\nc\nd\n", repo.Raw("show", ":hello.txt").Replace("\r\n", "\n"));

        var staged = await git.DiffFileAsync("hello.txt", DiffArea.Staged);
        await git.UnstageLinesAsync(staged, [new LineSelection(0, staged.Hunks[0].Lines.ToList().FindIndex(l => l.Text == "d"))]);
        Assert.Equal("a\nb\nc\n", repo.Raw("show", ":hello.txt").Replace("\r\n", "\n"));
        Assert.Equal("a\nB\nc\nd\n", repo.ReadNormalized("hello.txt"));
    }

    [Fact]
    public async Task Line_staging_with_nothing_selected_is_refused()
    {
        using var repo = RepositoryTests.RepoWithCommit();
        repo.Write("hello.txt", "changed\n");
        using var git = await repo.OpenAsync();
        var diff = await git.DiffFileAsync("hello.txt", DiffArea.Unstaged);
        var e = await Assert.ThrowsAsync<GitOperationException>(() => git.StageLinesAsync(diff, []));
        Assert.Equal(GitErrorCode.NothingSelected, e.Code);
    }

    [Fact]
    public async Task Line_staging_works_under_apply_whitespace_error()
    {
        using var repo = RepositoryTests.RepoWithCommit();
        repo.Raw("config", "apply.whitespace", "error");
        repo.Write("hello.txt", "hello\ntrailing spaces   \n");
        using var git = await repo.OpenAsync();
        var diff = await git.DiffFileAsync("hello.txt", DiffArea.Unstaged);
        var all = diff.Hunks.SelectMany((h, hi) => h.Lines.Select((l, li) => (l, hi, li))).Where(t => t.l.IsChange).Select(t => new LineSelection(t.hi, t.li));
        await git.StageLinesAsync(diff, all);
        Assert.Contains("trailing spaces   ", repo.Raw("show", ":hello.txt"));
    }
}
