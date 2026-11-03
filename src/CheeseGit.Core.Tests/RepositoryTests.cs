using CheeseGit.Core.Diff;
using CheeseGit.Core.Git;
using CheeseGit.Core.Repository;

namespace CheeseGit.Core.Tests;

/// <summary>
/// Port of src-tauri/tests/git_provider.rs: opening, branches, status,
/// committing, staging and diffs. Tests that hard-coded "master" use the
/// repository's actual initial branch (the hostile profile sets init.defaultBranch).
/// </summary>
public class RepositoryTests
{
    /// <summary>The git_provider.rs fixture: one commit of hello.txt containing "hello" (no newline).</summary>
    internal static TempRepo RepoWithCommit()
    {
        var repo = TempRepo.Init();
        repo.CommitFile("hello.txt", "hello", "initial commit");
        return repo;
    }

    private static List<string> Paths(IEnumerable<FileChange> changes) => changes.Select(c => c.Path.ToString()).ToList();

    // ── Opening ───────────────────────────────────────────────────────

    [Fact]
    public async Task Open_valid_repository()
    {
        using var repo = TempRepo.Init();
        var log = new List<GitInvocation>();
        repo.Git.Invoked += log.Add;
        using var git = await repo.OpenAsync();

        Assert.Equal(Path.GetFileName(repo.Path), git.Name);
        Assert.Equal(Path.GetFullPath(repo.Path), git.Root);
        Assert.Contains(log, e => e.Args.Contains("rev-parse") && e.ExitCode == 0);
        // Logged with the working directory it ran in.
        Assert.All(log.Where(e => e.Args.Contains("rev-parse")), e => Assert.Equal(repo.Path, e.WorkingDirectory));
    }

    [Fact]
    public async Task Open_nonexistent_directory()
    {
        var e = await Assert.ThrowsAsync<GitOperationException>(() => GitRepository.OpenAsync(Path.Combine(Path.GetTempPath(), "cheesegit-nonexistent-path-abc123")));
        Assert.Equal(GitErrorCode.NotFound, e.Code);
    }

    [Fact]
    public async Task Open_non_repo_directory()
    {
        using var dir = TempRepo.EmptyDirectory();
        var e = await Assert.ThrowsAsync<GitOperationException>(() => GitRepository.OpenAsync(dir.Path));
        Assert.Equal(GitErrorCode.NotARepository, e.Code);
    }

    [Fact]
    public async Task Open_bare_repository_is_refused()
    {
        using var bare = TempRepo.InitBare();
        var e = await Assert.ThrowsAsync<GitOperationException>(() => GitRepository.OpenAsync(bare.Path));
        Assert.Equal(GitErrorCode.BareRepository, e.Code);
    }

    [Fact]
    public async Task Open_subdirectory_resolves_to_root()
    {
        using var repo = TempRepo.Init();
        Directory.CreateDirectory(repo.FullPath("src"));
        using var git = await GitRepository.OpenAsync(repo.FullPath("src"));
        Assert.Equal(Path.GetFileName(repo.Path), git.Name);
        Assert.Equal(Path.GetFullPath(repo.Path), git.Root);
    }

    [Fact]
    public async Task Init_creates_and_opens_a_repository()
    {
        using var dir = TempRepo.EmptyDirectory();
        var target = Path.Combine(dir.Path, "new repo");
        using var git = await GitRepository.InitAsync(target);
        Assert.Equal("new repo", git.Name);
        Assert.True((await git.HeadAsync()).IsUnborn);
    }

    [Fact]
    public async Task Clone_creates_a_folder_named_after_the_repository()
    {
        using var source = RepoWithCommit();
        using var parent = TempRepo.EmptyDirectory();
        using var git = await GitRepository.CloneAsync(source.Path, parent.Path);
        Assert.Equal(Path.GetFileName(source.Path), git.Name);
        Assert.True(File.Exists(Path.Combine(git.Root, "hello.txt")));
    }

    [Theory]
    [InlineData("https://github.com/git/git.git", "git")]
    [InlineData("git@github.com:user/repo.git", "repo")]
    [InlineData("https://example.com/user/repo/", "repo")]
    [InlineData("/srv/repos/project.git/", "project")]
    [InlineData("/srv/repos/project/.git", "project")]
    public void Clone_directory_name_follows_git(string url, string expected) =>
        Assert.Equal(expected, GitRepository.DirectoryNameForUrl(url));

    // ── HEAD and branches ─────────────────────────────────────────────

    [Fact]
    public async Task Current_branch_returns_default_branch()
    {
        using var repo = RepoWithCommit();
        using var git = await repo.OpenAsync();
        Assert.Equal(repo.CurrentBranch(), await git.CurrentBranchAsync());
    }

    [Fact]
    public async Task Commit_log_returns_commits()
    {
        using var repo = RepoWithCommit();
        using var git = await repo.OpenAsync();
        var commit = Assert.Single(await git.CommitLogAsync(10));
        Assert.Equal("initial commit", commit.Summary);
        Assert.Equal("Test User", commit.Author.Name);
        Assert.Equal(repo.Head(), commit.Oid.ToHex());
        Assert.NotEqual(DateTimeOffset.UnixEpoch, commit.Author.When);
    }

    [Fact]
    public async Task Commit_log_respects_limit()
    {
        using var repo = RepoWithCommit();
        repo.CommitFile("world.txt", "world", "second commit");
        using var git = await repo.OpenAsync();
        var commit = Assert.Single(await git.CommitLogAsync(1));
        Assert.Equal("second commit", commit.Summary);
    }

    // The Rust provider returned an error for an empty repository; an empty
    // list is what every caller wants.
    [Fact]
    public async Task Commit_log_of_empty_repo_is_empty()
    {
        using var repo = TempRepo.Init();
        using var git = await repo.OpenAsync();
        Assert.Empty(await git.CommitLogAsync(10));
    }

    [Fact]
    public async Task List_branches_returns_current_branch()
    {
        using var repo = RepoWithCommit();
        using var git = await repo.OpenAsync();
        var current = Assert.Single(await git.ListBranchesAsync(), b => b.IsCurrent);
        Assert.Equal(repo.CurrentBranch(), current.Name);
        Assert.NotEqual(DateTimeOffset.UnixEpoch, current.LastCommitDate);
    }

    [Fact]
    public async Task Switch_branch_changes_current_branch()
    {
        using var repo = RepoWithCommit();
        using var git = await repo.OpenAsync();
        var original = repo.CurrentBranch();
        await git.CreateBranchAsync("feature-test");
        Assert.Equal("feature-test", await git.CurrentBranchAsync());
        await git.SwitchBranchAsync(original);
        Assert.Equal(original, await git.CurrentBranchAsync());
    }

    [Fact]
    public async Task Create_branch_creates_and_switches()
    {
        using var repo = RepoWithCommit();
        using var git = await repo.OpenAsync();
        await git.CreateBranchAsync("new-feature");
        Assert.Equal("new-feature", await git.CurrentBranchAsync());
        Assert.Contains(await git.ListBranchesAsync(), b => b.Name == "new-feature");
    }

    [Fact]
    public async Task Switch_to_nonexistent_branch_fails()
    {
        using var repo = RepoWithCommit();
        using var git = await repo.OpenAsync();
        await Assert.ThrowsAsync<GitCommandException>(() => git.SwitchBranchAsync("does-not-exist"));
    }

    [Fact]
    public async Task Invalid_branch_names_are_refused_before_running_git()
    {
        using var repo = RepoWithCommit();
        using var git = await repo.OpenAsync();
        foreach (var name in new[] { "-f", "has space", "a..b", "" })
        {
            var e = await Assert.ThrowsAsync<GitOperationException>(() => git.CreateBranchAsync(name));
            Assert.Equal(GitErrorCode.InvalidName, e.Code);
        }
    }

    [Fact]
    public async Task Branch_named_like_a_file_switches()
    {
        using var repo = RepoWithCommit();
        repo.CommitFile("feature", "a file named like the branch\n", "file");
        using var git = await repo.OpenAsync();
        var main = repo.CurrentBranch();
        await git.CreateBranchAsync("feature");
        await git.SwitchBranchAsync(main);
        await git.SwitchBranchAsync("feature");
        Assert.Equal("feature", await git.CurrentBranchAsync());
    }

    [Fact]
    public async Task Cherry_pick_commits_to_existing_branch()
    {
        using var repo = RepoWithCommit();
        using var git = await repo.OpenAsync();
        var main = repo.CurrentBranch();
        await git.CreateBranchAsync("feature-source");
        var hash1 = repo.CommitFile("feature1.txt", "one\n", "feature change 1");
        var hash2 = repo.CommitFile("feature2.txt", "two\n", "feature change 2");
        await git.SwitchBranchAsync(main);
        await git.CreateBranchAsync("target-branch");
        await git.SwitchBranchAsync("feature-source");

        await git.CherryPickAsync([Oid.ParseHex(hash1), Oid.ParseHex(hash2)], "target-branch", createBranch: false);

        Assert.Equal("target-branch", await git.CurrentBranchAsync());
        var log = repo.Raw("log", "--format=%s", "-3");
        Assert.Contains("feature change 2", log);
        Assert.Contains("feature change 1", log);
    }

    [Fact]
    public async Task Cherry_pick_commits_create_new_branch_from_current_point()
    {
        using var repo = RepoWithCommit();
        using var git = await repo.OpenAsync();
        var main = repo.CurrentBranch();
        var baseBefore = repo.Head();
        await git.CreateBranchAsync("feature-source");
        var pick = repo.CommitFile("pick-me.txt", "picked\n", "pick me");
        await git.SwitchBranchAsync(main);

        await git.CherryPickAsync([Oid.ParseHex(pick)], "cherry-target", createBranch: true);

        Assert.Equal("cherry-target", await git.CurrentBranchAsync());
        Assert.Equal(baseBefore, repo.Raw("rev-parse", "HEAD^").Trim());
        Assert.True(repo.Exists("pick-me.txt"));
    }

    [Fact]
    public async Task Cherry_pick_conflict_pauses_for_the_conflict_dialog()
    {
        using var repo = RepoWithCommit();
        using var git = await repo.OpenAsync();
        var main = repo.CurrentBranch();
        await git.CreateBranchAsync("source");
        var pick = repo.CommitFile("hello.txt", "from source", "source edit");
        await git.SwitchBranchAsync(main);
        repo.CommitFile("hello.txt", "from main", "main edit");

        var conflict = Assert.IsType<CherryPickResult.Conflict>(await git.CherryPickAsync([Oid.ParseHex(pick)], main, false));
        Assert.Equal(["hello.txt"], conflict.Info.ConflictedFiles.Select(f => f.ToString()));
        Assert.Equal(OperationKind.CherryPick, (await git.OperationStateAsync()).Kind);
    }

    [Fact]
    public async Task List_branches_ordered_by_recent()
    {
        using var repo = RepoWithCommit();
        using var git = await repo.OpenAsync();
        await git.CreateBranchAsync("branch-a");
        // Commit timestamps have one-second resolution; make the order unambiguous.
        repo.Write("a.txt", "a");
        repo.Raw("add", ".");
        repo.RawWithEnv(new Dictionary<string, string> { ["GIT_COMMITTER_DATE"] = "3786912000 +0000" }, "commit", "-q", "-m", "commit on a");
        Assert.Equal("branch-a", (await git.ListBranchesAsync())[0].Name);
    }

    // ── Status ────────────────────────────────────────────────────────

    [Fact]
    public async Task Status_shows_unstaged_changes()
    {
        using var repo = RepoWithCommit();
        repo.Write("hello.txt", "modified");
        using var git = await repo.OpenAsync();
        var status = await git.StatusAsync();
        Assert.Empty(status.Staged);
        Assert.Equal(["hello.txt"], Paths(status.Unstaged));
    }

    [Fact]
    public async Task Status_shows_staged_changes()
    {
        using var repo = RepoWithCommit();
        repo.Write("new.txt", "new");
        repo.Raw("add", "new.txt");
        using var git = await repo.OpenAsync();
        var status = await git.StatusAsync();
        Assert.Equal(["new.txt"], Paths(status.Staged));
        Assert.Equal(FileStatus.Added, status.Staged.Single().Status);
    }

    [Fact]
    public async Task Status_shows_file_in_both_staged_and_unstaged()
    {
        using var repo = RepoWithCommit();
        repo.Write("hello.txt", "staged version");
        repo.Raw("add", "hello.txt");
        repo.Write("hello.txt", "unstaged version");
        using var git = await repo.OpenAsync();
        var status = await git.StatusAsync();
        Assert.Contains("hello.txt", Paths(status.Staged));
        Assert.Contains("hello.txt", Paths(status.Unstaged));
    }

    [Fact]
    public async Task Status_clean_repo_returns_empty()
    {
        using var repo = RepoWithCommit();
        using var git = await repo.OpenAsync();
        var status = await git.StatusAsync();
        Assert.Empty(status.Staged);
        Assert.Empty(status.Unstaged);
    }

    [Fact]
    public async Task Status_lists_individual_files_in_untracked_directory()
    {
        using var repo = RepoWithCommit();
        repo.Write("newdir/one.txt", "1");
        repo.Write("newdir/two.txt", "2");
        using var git = await repo.OpenAsync();
        var paths = Paths((await git.StatusAsync()).Unstaged);
        paths.Sort(StringComparer.Ordinal);
        Assert.Equal(["newdir/one.txt", "newdir/two.txt"], paths);
    }

    [Fact]
    public async Task Status_ignores_untracked_nested_git_directory()
    {
        using var repo = RepoWithCommit();
        Directory.CreateDirectory(repo.FullPath(".tmp-test-repo"));
        repo.Raw("-C", ".tmp-test-repo", "init", "-q");
        using var git = await repo.OpenAsync();
        var status = await git.StatusAsync();
        Assert.Empty(status.Unstaged);
        Assert.Empty(status.Staged);
    }

    // ── Commit ────────────────────────────────────────────────────────

    [Fact]
    public async Task Commit_creates_a_new_commit()
    {
        using var repo = RepoWithCommit();
        repo.Write("committed.txt", "data");
        repo.Raw("add", ".");
        using var git = await repo.OpenAsync();
        var oid = await git.CommitAsync("test commit");
        Assert.Equal(repo.Head(), oid.ToHex());
        Assert.Equal("test commit", (await git.CommitLogAsync(10))[0].Summary);
    }

    [Fact]
    public async Task Commit_with_description()
    {
        using var repo = RepoWithCommit();
        repo.Write("desc.txt", "data");
        repo.Raw("add", ".");
        using var git = await repo.OpenAsync();
        await git.CommitAsync("summary line", "detailed description");
        var commit = (await git.CommitLogAsync(1))[0];
        Assert.Equal("summary line", commit.Summary);
        Assert.Equal("detailed description", commit.Body);
    }

    [Fact]
    public async Task Commit_message_that_looks_like_an_option_is_kept_verbatim()
    {
        using var repo = RepoWithCommit();
        using var git = await repo.OpenAsync();
        await git.CommitAsync("--amend", "-m \"quoted\"\n\nsecond paragraph", allowEmpty: true);
        var commit = (await git.CommitLogAsync(1))[0];
        Assert.Equal("--amend", commit.Summary);
        Assert.Equal("-m \"quoted\"\n\nsecond paragraph", commit.Body);
    }

    [Fact]
    public async Task Commit_with_nothing_staged_fails()
    {
        using var repo = RepoWithCommit();
        using var git = await repo.OpenAsync();
        await Assert.ThrowsAsync<GitCommandException>(() => git.CommitAsync("empty commit"));
    }

    [Fact]
    public async Task Commit_allow_empty_succeeds_with_nothing_staged()
    {
        using var repo = RepoWithCommit();
        using var git = await repo.OpenAsync();
        await git.CommitAsync("empty commit", allowEmpty: true);
        Assert.Equal("empty commit", (await git.CommitLogAsync(1))[0].Summary);
    }

    // ── Deleting branches ─────────────────────────────────────────────

    [Fact]
    public async Task Delete_merged_branch()
    {
        using var repo = RepoWithCommit();
        using var git = await repo.OpenAsync();
        var main = repo.CurrentBranch();
        await git.CreateBranchAsync("feature");
        await git.SwitchBranchAsync(main);
        Assert.False((await git.BranchDeleteInfoAsync("feature")).ExistsOnRemote);
        await git.DeleteBranchAsync("feature", force: false);
        Assert.DoesNotContain(await git.ListBranchesAsync(), b => b.Name == "feature");
    }

    [Fact]
    public async Task Delete_unmerged_branch_without_force_fails()
    {
        using var repo = RepoWithCommit();
        using var git = await repo.OpenAsync();
        var main = repo.CurrentBranch();
        await git.CreateBranchAsync("unmerged-feature");
        repo.Write("feature.txt", "new work");
        await git.StageAsync(["feature.txt"]);
        await git.CommitAsync("feature work");
        await git.SwitchBranchAsync(main);
        Assert.False((await git.BranchDeleteInfoAsync("unmerged-feature")).ExistsOnRemote);

        var e = await Assert.ThrowsAsync<GitOperationException>(() => git.DeleteBranchAsync("unmerged-feature", force: false));
        Assert.Equal(GitErrorCode.NotFullyMerged, e.Code);
    }

    [Fact]
    public async Task Delete_unmerged_branch_with_force_succeeds()
    {
        using var repo = RepoWithCommit();
        using var git = await repo.OpenAsync();
        var main = repo.CurrentBranch();
        await git.CreateBranchAsync("unmerged-feature");
        repo.Write("feature.txt", "new work");
        await git.StageAsync(["feature.txt"]);
        await git.CommitAsync("feature work");
        await git.SwitchBranchAsync(main);
        await git.DeleteBranchAsync("unmerged-feature", force: true);
        Assert.DoesNotContain(await git.ListBranchesAsync(), b => b.Name == "unmerged-feature");
    }

    [Fact]
    public async Task Branch_delete_info_no_remote()
    {
        using var repo = RepoWithCommit();
        using var git = await repo.OpenAsync();
        var main = repo.CurrentBranch();
        await git.CreateBranchAsync("local-only");
        await git.SwitchBranchAsync(main);
        var info = await git.BranchDeleteInfoAsync("local-only");
        Assert.Equal(new BranchDeleteInfo(false, null, null), info);
    }

    // ── Staging ───────────────────────────────────────────────────────

    [Fact]
    public async Task Stage_file_moves_to_staged()
    {
        using var repo = RepoWithCommit();
        repo.Write("new.txt", "new");
        using var git = await repo.OpenAsync();
        var status = await git.StatusAsync();
        Assert.Single(status.Unstaged);
        Assert.Empty(status.Staged);

        await git.StageAsync(["new.txt"]);
        status = await git.StatusAsync();
        Assert.Equal(["new.txt"], Paths(status.Staged));
        Assert.Empty(status.Unstaged);
    }

    [Fact]
    public async Task Unstage_file_moves_to_unstaged()
    {
        using var repo = RepoWithCommit();
        repo.Write("hello.txt", "changed");
        using var git = await repo.OpenAsync();
        await git.StageAsync(["hello.txt"]);
        Assert.Single((await git.StatusAsync()).Staged);

        await git.UnstageAsync(["hello.txt"]);
        var status = await git.StatusAsync();
        Assert.Empty(status.Staged);
        Assert.Equal(["hello.txt"], Paths(status.Unstaged));
    }

    [Fact]
    public async Task Stage_and_unstage_multiple_files()
    {
        using var repo = RepoWithCommit();
        repo.Write("a.txt", "a");
        repo.Write("b.txt", "b");
        using var git = await repo.OpenAsync();
        await git.StageAsync(["a.txt", "b.txt"]);
        var status = await git.StatusAsync();
        Assert.Equal(2, status.Staged.Count());
        Assert.Empty(status.Unstaged);

        await git.UnstageAsync(["a.txt", "b.txt"]);
        status = await git.StatusAsync();
        Assert.Empty(status.Staged);
        Assert.Equal(2, status.Unstaged.Count());
    }

    [Fact]
    public async Task Stage_and_unstage_a_deletion()
    {
        using var repo = RepoWithCommit();
        repo.Delete("hello.txt");
        using var git = await repo.OpenAsync();
        await git.StageAsync(["hello.txt"]);
        Assert.Equal(FileStatus.Deleted, (await git.StatusAsync()).Staged.Single().Status);
        await git.UnstageAsync(["hello.txt"]);
        Assert.Equal(FileStatus.Deleted, (await git.StatusAsync()).Unstaged.Single().Status);
    }

    [Fact]
    public async Task Unstaging_a_staged_rename_restores_both_paths()
    {
        if (TestEnvironment.Has("status.renames=false")) return;
        using var repo = RepoWithCommit();
        repo.Raw("mv", "hello.txt", "renamed.txt");
        using var git = await repo.OpenAsync();
        Assert.Equal(FileStatus.Renamed, (await git.StatusAsync()).Staged.Single().Status);

        await git.UnstageAsync(["renamed.txt"]);
        var status = await git.StatusAsync();
        Assert.Empty(status.Staged);
        Assert.Contains(status.Unstaged, c => c.Path.ToString() == "hello.txt" && c.Status == FileStatus.Deleted);
        Assert.Contains(status.Unstaged, c => c.Path.ToString() == "renamed.txt" && c.Status == FileStatus.Untracked);
    }

    [Fact]
    public async Task Unstage_works_before_the_first_commit()
    {
        using var repo = TempRepo.Init();
        repo.Write("a.txt", "a");
        using var git = await repo.OpenAsync();
        await git.StageAsync(["a.txt"]);
        await git.UnstageAsync(["a.txt"]);
        var status = await git.StatusAsync();
        Assert.Empty(status.Staged);
        Assert.Equal(FileStatus.Untracked, status.Unstaged.Single().Status);
    }

    [Fact]
    public async Task Staging_hostile_paths_is_byte_exact_and_literal()
    {
        using var repo = RepoWithCommit();
        var names = GitRunnerTests.HostileNamesForThisOs;
        foreach (var n in names) repo.Write(n, "x\n");
        repo.Write("decoy.txt", "must not be staged by a glob\n");
        using var git = await repo.OpenAsync();

        await git.StageAsync(names.Select(GitPath.FromString));
        var status = await git.StatusAsync();
        Assert.Equal(names.OrderBy(n => n, StringComparer.Ordinal), Paths(status.Staged).OrderBy(n => n, StringComparer.Ordinal));
        Assert.Equal(["decoy.txt"], Paths(status.Unstaged));

        await git.UnstageAsync(names.Select(GitPath.FromString));
        Assert.Empty((await git.StatusAsync()).Staged);
    }

    // ── Diffs ─────────────────────────────────────────────────────────

    [Fact]
    public async Task Diff_unstaged_modification()
    {
        using var repo = RepoWithCommit();
        repo.Write("hello.txt", "hello world\n");
        using var git = await repo.OpenAsync();
        var diff = await git.DiffFileAsync("hello.txt", DiffArea.Unstaged);
        Assert.Equal("hello.txt", diff.Path.ToString());
        var lines = Assert.Single(diff.Hunks).Lines;
        Assert.Contains(lines, l => l.Kind == DiffLineKind.Deletion);
        Assert.Contains(lines, l => l.Kind == DiffLineKind.Addition);
    }

    [Fact]
    public async Task Diff_staged_modification()
    {
        using var repo = RepoWithCommit();
        repo.Write("hello.txt", "staged change\n");
        repo.Raw("add", "hello.txt");
        using var git = await repo.OpenAsync();
        var diff = await git.DiffFileAsync("hello.txt", DiffArea.Staged);
        Assert.Equal("hello.txt", diff.Path.ToString());
        Assert.NotEmpty(diff.Hunks);
    }

    [Fact]
    public async Task Diff_staged_vs_unstaged_are_different()
    {
        using var repo = RepoWithCommit();
        repo.Write("hello.txt", "staged version");
        repo.Raw("add", "hello.txt");
        repo.Write("hello.txt", "unstaged version");
        using var git = await repo.OpenAsync();
        var staged = await git.DiffFileAsync("hello.txt", DiffArea.Staged);
        var unstaged = await git.DiffFileAsync("hello.txt", DiffArea.Unstaged);
        static List<string> Adds(FileDiff d) => d.Hunks[0].Lines.Where(l => l.Kind == DiffLineKind.Addition).Select(l => l.Text).ToList();
        Assert.Equal(["staged version"], Adds(staged));
        Assert.Equal(["unstaged version"], Adds(unstaged));
    }

    [Fact]
    public async Task Diff_no_changes_returns_empty_hunks()
    {
        using var repo = RepoWithCommit();
        using var git = await repo.OpenAsync();
        Assert.Empty((await git.DiffFileAsync("hello.txt", DiffArea.Unstaged)).Hunks);
    }

    [Fact]
    public async Task Diff_line_numbers_are_correct()
    {
        using var repo = RepoWithCommit();
        repo.CommitFile("multi.txt", "line1\nline2\nline3\n", "add multi");
        repo.Write("multi.txt", "line1\nchanged\nline3\n");
        using var git = await repo.OpenAsync();
        var lines = (await git.DiffFileAsync("multi.txt", DiffArea.Unstaged)).Hunks[0].Lines;
        var del = lines.First(l => l.Kind == DiffLineKind.Deletion);
        Assert.Equal((2, "line2"), (del.OldLine, del.Text));
        var add = lines.First(l => l.Kind == DiffLineKind.Addition);
        Assert.Equal((2, "changed"), (add.NewLine, add.Text));
    }

    [Fact]
    public async Task Diff_of_untracked_file_is_a_new_file()
    {
        using var repo = RepoWithCommit();
        repo.Write("new dir/new file.txt", "a\nb\n");
        using var git = await repo.OpenAsync();
        var diff = await git.DiffFileAsync("new dir/new file.txt", DiffArea.Unstaged);
        Assert.Equal(DiffChange.Added, diff.Change);
        Assert.Equal("new dir/new file.txt", diff.Path.ToString());
        Assert.Equal(["a", "b"], diff.Hunks.Single().Lines.Select(l => l.Text));
    }

    [Fact]
    public async Task Diff_ignores_external_diff_and_textconv()
    {
        using var repo = RepoWithCommit();
        repo.Write(".gitattributes", "*.txt diff=upper\n");
        repo.Raw("config", "diff.upper.textconv", "tr a-z A-Z");
        repo.Raw("config", "diff.upper.command", "false");
        repo.Write("hello.txt", "changed\n");
        using var git = await repo.OpenAsync();
        var diff = await git.DiffFileAsync("hello.txt", DiffArea.Unstaged);
        Assert.Contains(diff.Hunks.Single().Lines, l => l.Text == "changed");
    }
}
