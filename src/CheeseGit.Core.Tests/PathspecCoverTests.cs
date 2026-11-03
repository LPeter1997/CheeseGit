using CheeseGit.Core.Git;
using CheeseGit.Core.Repository;

namespace CheeseGit.Core.Tests;

/// <summary>
/// Long path lists shrink to the directories they cover (37 s → 0.4 s for
/// "stage all" of 15k files), and the result must equal the per-file command.
/// </summary>
public class PathspecCoverTests
{
    private static List<string> Cover(string[] selected, string[] changed) =>
        PathspecCover.Cover([.. selected.Select(GitPath.FromString)], changed.Select(GitPath.FromString)).Select(p => p.ToString()).Order().ToList();

    [Fact]
    public void Everything_selected_is_the_whole_tree()
    {
        Assert.Equal(["."], Cover(["a/1", "a/b/2", "c"], ["a/1", "a/b/2", "c"]));
    }

    [Fact]
    public void An_unselected_change_keeps_its_directories_open()
    {
        Assert.Equal(["a/1", "a/b", "c"], Cover(["a/1", "a/b/2", "a/b/3", "c"], ["a/1", "a/b/2", "a/b/3", "a/keep", "c"]));
    }

    [Fact]
    public void Prefix_lookalikes_are_not_the_same_directory()
    {
        // "a/b" covers "a/b/x" but not "a/bc".
        Assert.Equal(["a/b", "a/bc/1"], Cover(["a/b/1", "a/bc/1"], ["a/b/1", "a/bc/1", "a/bc/2"]));
    }

    [Fact]
    public void Paths_status_no_longer_lists_are_kept()
    {
        Assert.Equal(["gone", "x"], Cover(["x/1", "gone"], ["x/1", "y/1"]));
    }

    // ── Against git ───────────────────────────────────────────────────

    private const int Many = PathspecCover.Threshold + 20;

    /// <summary>
    /// Many modified files in "a/" (hostile names included) and "b/", one more
    /// in "a/" that tests leave unselected, a staged-only file, a staged
    /// rename and untracked files.
    /// </summary>
    private static TempRepo ManyChanges()
    {
        var repo = TempRepo.Init();
        // Names that mean something as pathspecs (magic, globs) if they were not literal.
        string[] hostile = OperatingSystem.IsWindows() ? ["a/-dash ✓.txt", "a/sp ace/[x].txt"] : ["a/-dash ✓.txt", "a/:(top)x.txt", "a/sp ace/*.txt"];
        var files = Enumerable.Range(0, Many).Select(i => $"a/f{i:000}.txt").Concat(hostile).Concat(Enumerable.Range(0, 10).Select(i => $"b/f{i}.txt")).ToList();
        foreach (var f in files.Append("a/keep.txt").Append("c/staged.txt").Append("old.txt")) repo.Write(f, "v1\n");
        repo.CommitAll("initial");
        foreach (var f in files.Append("a/keep.txt").Append("c/staged.txt")) repo.Write(f, "v2\n");
        repo.Raw("add", "c/staged.txt");
        Directory.CreateDirectory(repo.FullPath("r"));
        repo.Raw("mv", "old.txt", "r/new.txt");
        for (var i = 0; i < 10; i++) repo.Write($"u/n{i}.txt", "new\n");
        return repo;
    }

    private static async Task<RepoStatus> Status(GitRepository git) => await git.StatusAsync();

    [Fact]
    public async Task Staging_many_files_stages_exactly_those()
    {
        using var repo = ManyChanges();
        using var git = await repo.OpenAsync();
        var before = await Status(git);
        var selected = before.Unstaged.Select(c => c.Path).Where(p => p.ToString() != "a/keep.txt").ToList();
        Assert.True(selected.Count > PathspecCover.Threshold);

        await git.StageAsync(selected);

        var status = await Status(git);
        Assert.Equal(["a/keep.txt"], status.Unstaged.Select(c => c.Path.ToString()));
        // The selection plus what was staged already (c/staged.txt and the rename, one or two entries depending on status.renames).
        Assert.Equal(before.Staged.Select(c => c.Path).Concat(selected).ToHashSet(), status.Staged.Select(c => c.Path).ToHashSet());
        Assert.Contains(status.Staged, c => c.Path.ToString() == "a/-dash ✓.txt");
    }

    [Fact]
    public async Task Unstaging_many_files_unstages_exactly_those()
    {
        using var repo = ManyChanges();
        repo.Raw("add", "-A");
        using var git = await repo.OpenAsync();
        var all = (await Status(git)).Staged.Select(c => c.Path).ToList();
        var selected = all.Where(p => p.ToString() != "a/keep.txt").ToList();

        await git.UnstageAsync(selected);

        var status = await Status(git);
        Assert.Equal(["a/keep.txt"], status.Staged.Select(c => c.Path.ToString()));
        // The rename's source came back too: old.txt is a worktree deletion again, r/new.txt untracked.
        Assert.Contains(status.Unstaged, c => c.Path.ToString() == "old.txt" && c.Status == FileStatus.Deleted);
        Assert.Contains(status.Unstaged, c => c.Path.ToString() == "r/new.txt" && c.Status == FileStatus.Untracked);
    }

    [Fact]
    public async Task Discarding_many_files_discards_exactly_those()
    {
        using var repo = ManyChanges();
        using var git = await repo.OpenAsync();
        var selected = (await Status(git)).Unstaged.Select(c => c.Path).Where(p => p.ToString() != "a/keep.txt").ToList();

        await git.DiscardUnstagedAsync(selected);

        var status = await Status(git);
        Assert.Equal(["a/keep.txt"], status.Unstaged.Select(c => c.Path.ToString()));
        Assert.Equal("v2\n", repo.ReadNormalized("a/keep.txt"));
        Assert.Equal("v1\n", repo.ReadNormalized("a/-dash ✓.txt"));
        Assert.Equal("v2\n", repo.ReadNormalized("c/staged.txt")); // Staged-only: the index version stays.
        Assert.False(repo.Exists("u/n0.txt"));
    }
}
