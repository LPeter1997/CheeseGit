using CheeseGit.Core.Git;
using CheeseGit.Core.Repository;

namespace CheeseGit.Core.Tests;

/// <summary>Port of merge.rs, revert.rs and conflict_resolution.rs.</summary>
public class MergeRevertTests
{
    /// <summary>
    /// shared.txt is changed differently on the default branch and on
    /// "feature"; HEAD is on the default branch.
    /// </summary>
    private static TempRepo ConflictingBranches(string path = "file.txt", string @base = "original\n", string ours = "main version\n", string theirs = "feature version\n", params string[] config)
    {
        var repo = TempRepo.Init();
        for (var i = 0; i < config.Length; i += 2) repo.Raw("config", config[i], config[i + 1]);
        repo.CommitFile(path, @base, "initial");
        repo.Raw("switch", "-q", "-c", "feature");
        repo.CommitFile(path, theirs, "feature");
        repo.Raw("switch", "-q", "-");
        repo.CommitFile(path, ours, "main");
        return repo;
    }

    private static async Task<MergeConflictInfo> MergeExpectingConflict(GitRepository git, string branch = "feature")
    {
        var result = await git.MergeAsync(branch);
        return Assert.IsType<MergeResult.Conflict>(result).Info;
    }

    /// <summary>file.txt: line2 is changed by the commit returned, then changed again on top of it.</summary>
    private static TempRepo ConflictingRevert(out Oid toRevert)
    {
        var repo = TempRepo.Init();
        repo.CommitFile("file.txt", "line1\nline2\nline3\n", "initial commit");
        toRevert = Oid.ParseHex(repo.CommitFile("file.txt", "line1\nchanged_line2\nline3\n", "modify line2"));
        repo.CommitFile("file.txt", "line1\ndifferent_line2\nline3\n", "also modify line2");
        return repo;
    }

    private static bool InProgress(TempRepo repo, string stateFile) => repo.Exists(Path.Combine(".git", stateFile));

    // ── Merge ─────────────────────────────────────────────────────────

    [Fact]
    public async Task Merge_fast_forward()
    {
        using var repo = TempRepo.Init();
        repo.CommitFile("file.txt", "initial content", "initial commit");
        repo.Raw("switch", "-q", "-c", "feature");
        repo.CommitFile("feature.txt", "feature content", "feature commit");
        repo.Raw("switch", "-q", "-");
        using var git = await repo.OpenAsync();
        Assert.Equal(new MergeResult.Success(1), await git.MergeAsync("feature"));
        Assert.True(repo.Exists("feature.txt"));
    }

    [Fact]
    public async Task Merge_clean_no_conflicts()
    {
        using var repo = TempRepo.Init();
        repo.CommitFile("base.txt", "base", "initial");
        repo.Raw("switch", "-q", "-c", "feature");
        repo.CommitFile("feature.txt", "feature work", "feature commit");
        repo.Raw("switch", "-q", "-");
        repo.CommitFile("main.txt", "main work", "main commit");
        using var git = await repo.OpenAsync();
        Assert.IsType<MergeResult.Success>(await git.MergeAsync("feature"));
        Assert.True(repo.Exists("feature.txt"));
        Assert.True(repo.Exists("main.txt"));
        Assert.Equal(2, repo.Raw("rev-list", "--parents", "-n", "1", "HEAD").Split(' ').Length - 1);
    }

    [Fact]
    public async Task Merge_with_conflicts()
    {
        using var repo = ConflictingBranches("shared.txt", "line 1\nline 2\nline 3\n", "line 1\nmain change\nline 3\n", "line 1\nfeature change\nline 3\n");
        using var git = await repo.OpenAsync();
        var info = await MergeExpectingConflict(git);
        Assert.Equal("feature", info.IncomingBranch);
        Assert.Equal([(GitPath)"shared.txt"], info.ConflictedFiles);
    }

    [Fact]
    public async Task Merge_of_a_remote_tracking_branch_names_it()
    {
        using var bare = TempRepo.InitBare();
        using var repo = ConflictingBranches();
        repo.Raw("remote", "add", "origin", bare.Path);
        repo.Raw("push", "-q", "origin", "refs/heads/feature:refs/heads/feature");
        repo.Raw("fetch", "-q", "origin");
        repo.Raw("branch", "-q", "-D", "feature");
        using var git = await repo.OpenAsync();
        Assert.Equal("origin/feature", (await MergeExpectingConflict(git, "origin/feature")).IncomingBranch);
        Assert.Equal("origin/feature", (await git.OperationStateAsync()).Incoming);
    }

    [Fact]
    public async Task Merge_abort_restores_state()
    {
        using var repo = ConflictingBranches("shared.txt");
        using var git = await repo.OpenAsync();
        await MergeExpectingConflict(git);
        await git.MergeAbortAsync();
        Assert.Equal("main version\n", repo.ReadNormalized("shared.txt"));
        Assert.False(InProgress(repo, "MERGE_HEAD"));
    }

    [Fact]
    public async Task Merge_abort_without_a_merge_fails_with_a_code()
    {
        using var repo = TempRepo.WithCommit();
        using var git = await repo.OpenAsync();
        var e = await Assert.ThrowsAsync<GitOperationException>(() => git.MergeAbortAsync());
        Assert.Equal(GitErrorCode.NotInProgress, e.Code);
    }

    [Fact]
    public async Task Merge_of_a_missing_branch_fails_with_a_code()
    {
        using var repo = TempRepo.WithCommit();
        using var git = await repo.OpenAsync();
        var e = await Assert.ThrowsAsync<GitOperationException>(() => git.MergeAsync("does-not-exist"));
        Assert.Equal(GitErrorCode.NotFound, e.Code);
    }

    [Fact]
    public async Task Resolve_conflict_accept_current()
    {
        using var repo = ConflictingBranches();
        using var git = await repo.OpenAsync();
        await MergeExpectingConflict(git);
        await git.ResolveConflictAsync("file.txt", ConflictResolution.AcceptCurrent);
        Assert.Equal("main version\n", repo.ReadNormalized("file.txt"));
        Assert.Empty(await git.ConflictedFilesAsync());
    }

    [Fact]
    public async Task Resolve_conflict_accept_incoming()
    {
        using var repo = ConflictingBranches();
        using var git = await repo.OpenAsync();
        await MergeExpectingConflict(git);
        await git.ResolveConflictAsync("file.txt", ConflictResolution.AcceptIncoming);
        Assert.Equal("feature version\n", repo.ReadNormalized("file.txt"));
        Assert.Empty(await git.ConflictedFilesAsync());
    }

    [Fact]
    public async Task Resolve_conflict_accept_both()
    {
        using var repo = ConflictingBranches();
        using var git = await repo.OpenAsync();
        await MergeExpectingConflict(git);
        await git.ResolveConflictAsync("file.txt", ConflictResolution.AcceptBoth);
        var content = repo.ReadNormalized("file.txt");
        Assert.Equal("main version\nfeature version\n", content);
        Assert.Empty(await git.ConflictedFilesAsync());
    }

    // The old app stripped markers from the working-tree file, which broke
    // under diff3/zdiff3 (the base section survived) and with CRLF files.
    // The union is now computed from the index stages, independent of both.

    [Fact]
    public async Task Accept_both_ignores_the_conflict_style()
    {
        using var repo = ConflictingBranches();
        repo.Raw("config", "merge.conflictStyle", "zdiff3");
        using var git = await repo.OpenAsync();
        await MergeExpectingConflict(git);
        Assert.Contains("|||||||", repo.Read("file.txt"));
        await git.ResolveConflictAsync("file.txt", ConflictResolution.AcceptBoth);
        Assert.Equal("main version\nfeature version\n", repo.ReadNormalized("file.txt"));
    }

    [Fact]
    public async Task Accept_both_keeps_crlf_content_byte_exact()
    {
        // Byte-exact comparison needs git to leave line endings alone, from the first commit on.
        using var repo = ConflictingBranches(
            "file.txt", "a\r\nbase\r\nz\r\n", "a\r\nours\r\nz\r\n", "a\r\ntheirs\r\nz\r\n",
            "core.autocrlf", "false", "core.eol", "lf");
        using var git = await repo.OpenAsync();
        await MergeExpectingConflict(git);
        await git.ResolveConflictAsync("file.txt", ConflictResolution.AcceptBoth);
        Assert.Equal("a\r\nours\r\ntheirs\r\nz\r\n", repo.Read("file.txt"));
        Assert.Equal("a\r\nours\r\ntheirs\r\nz\r\n", repo.Raw("cat-file", "blob", ":file.txt"));
    }

    [Fact]
    public async Task Accept_incoming_deletion_removes_the_file()
    {
        using var repo = TempRepo.Init();
        repo.CommitFile("file.txt", "original\n", "initial");
        repo.Raw("switch", "-q", "-c", "feature");
        repo.Raw("rm", "-q", "file.txt");
        repo.Raw("commit", "-q", "-m", "delete");
        repo.Raw("switch", "-q", "-");
        repo.CommitFile("file.txt", "changed\n", "change");
        using var git = await repo.OpenAsync();
        await MergeExpectingConflict(git);
        await git.ResolveConflictAsync("file.txt", ConflictResolution.AcceptIncoming);
        Assert.False(repo.Exists("file.txt"));
        Assert.Empty(await git.ConflictedFilesAsync());
        await git.MergeContinueAsync("merge");
        Assert.Empty(repo.Raw("ls-files", "file.txt"));
    }

    [Fact]
    public async Task Diff_stats_list_a_conflicted_file_once_and_only_as_unstaged()
    {
        using var repo = ConflictingBranches();
        using var git = await repo.OpenAsync();
        await MergeExpectingConflict(git);
        Assert.Empty(await git.DiffStatsAsync(DiffArea.Staged));
        Assert.Equal("file.txt", Assert.Single(await git.DiffStatsAsync(DiffArea.Unstaged)).Path.ToString());
    }

    [Fact]
    public async Task Merge_conflicts_returns_empty_when_no_merge()
    {
        using var repo = TempRepo.WithCommit();
        using var git = await repo.OpenAsync();
        var info = await git.MergeConflictsAsync();
        Assert.Empty(info.ConflictedFiles);
        Assert.Equal("", info.IncomingBranch);
    }

    [Fact]
    public async Task Merge_continue_after_resolving_all_conflicts()
    {
        using var repo = ConflictingBranches();
        using var git = await repo.OpenAsync();
        await MergeExpectingConflict(git);
        await git.ResolveConflictAsync("file.txt", ConflictResolution.AcceptCurrent);
        Assert.Equal(1, await git.MergeContinueAsync("Merge branch 'feature'"));
        Assert.False(InProgress(repo, "MERGE_HEAD"));
        var status = await git.StatusAsync();
        Assert.Empty(status.Staged);
        Assert.Empty(status.Unstaged);
        Assert.Equal("Merge branch 'feature'", git.GetCommit(Oid.ParseHex(repo.Head()))!.Summary);
    }

    [Fact]
    public async Task Merge_continue_with_unresolved_conflicts_fails_and_keeps_the_merge()
    {
        using var repo = ConflictingBranches();
        using var git = await repo.OpenAsync();
        await MergeExpectingConflict(git);
        await Assert.ThrowsAsync<GitCommandException>(() => git.MergeContinueAsync("merge"));
        Assert.Equal(OperationKind.Merge, (await git.OperationStateAsync()).Kind);
    }

    [Fact]
    public async Task Merge_already_up_to_date()
    {
        using var repo = TempRepo.WithCommit();
        repo.Raw("branch", "feature");
        using var git = await repo.OpenAsync();
        Assert.IsType<MergeResult.AlreadyUpToDate>(await git.MergeAsync("feature"));
    }

    [Fact]
    public async Task Merge_success_reports_commit_count()
    {
        using var repo = TempRepo.Init();
        repo.CommitFile("base.txt", "base", "initial");
        repo.Raw("switch", "-q", "-c", "feature");
        repo.CommitFile("a.txt", "a", "first feature commit");
        repo.CommitFile("b.txt", "b", "second feature commit");
        repo.Raw("switch", "-q", "-");
        using var git = await repo.OpenAsync();
        // Two whether this fast-forwards or merge.ff=false makes a merge commit.
        Assert.Equal(new MergeResult.Success(2), await git.MergeAsync("feature"));
    }

    [Fact]
    public async Task Check_merge_state_none_when_clean()
    {
        using var repo = TempRepo.WithCommit();
        using var git = await repo.OpenAsync();
        Assert.Equal(OperationState.None, await git.OperationStateAsync());
    }

    [Fact]
    public async Task Check_merge_state_detects_in_progress_merge()
    {
        using var repo = ConflictingBranches();
        using var git = await repo.OpenAsync();
        await MergeExpectingConflict(git);
        Assert.Equal(new OperationState(OperationKind.Merge, "feature", 1), await git.OperationStateAsync());
    }

    [Fact]
    public async Task Check_merge_state_none_after_abort()
    {
        using var repo = ConflictingBranches();
        using var git = await repo.OpenAsync();
        await MergeExpectingConflict(git);
        await git.MergeAbortAsync();
        Assert.Equal(OperationState.None, await git.OperationStateAsync());
    }

    [Fact]
    public async Task Merge_multiple_files_different_strategies()
    {
        using var repo = TempRepo.Init();
        repo.CommitFile("file1.txt", "main1", "initial");
        repo.CommitFile("file2.txt", "main2", "initial");
        repo.Raw("switch", "-q", "-c", "feature");
        repo.CommitFile("file1.txt", "feature1", "feature");
        repo.CommitFile("file2.txt", "feature2", "feature");
        repo.Raw("switch", "-q", "-");
        repo.CommitFile("file1.txt", "main1-modified", "main");
        repo.CommitFile("file2.txt", "main2-modified", "main");
        using var git = await repo.OpenAsync();
        Assert.Equal(2, (await MergeExpectingConflict(git)).ConflictedFiles.Count);

        await git.ResolveConflictAsync("file1.txt", ConflictResolution.AcceptCurrent);
        Assert.Equal([(GitPath)"file2.txt"], await git.ConflictedFilesAsync());
        await git.ResolveConflictAsync("file2.txt", ConflictResolution.AcceptIncoming);
        await git.MergeContinueAsync("Merge with mixed strategies");

        Assert.Equal("main1-modified", repo.Read("file1.txt"));
        Assert.Equal("feature2", repo.Read("file2.txt"));
        Assert.False(InProgress(repo, "MERGE_HEAD"));
    }

    [Fact]
    public async Task Merge_conflict_externally_resolved()
    {
        using var repo = ConflictingBranches();
        using var git = await repo.OpenAsync();
        await MergeExpectingConflict(git);
        Assert.Equal([new FileConflictInfo("file.txt", 1)], await git.ConflictCountsAsync());

        repo.Write("file.txt", "externally resolved content");
        repo.Raw("add", "file.txt");
        Assert.Empty(await git.ConflictCountsAsync());

        await git.MergeContinueAsync("Merge with external resolution");
        Assert.False(InProgress(repo, "MERGE_HEAD"));
    }

    // ── Revert ────────────────────────────────────────────────────────

    [Fact]
    public async Task Revert_clean()
    {
        using var repo = TempRepo.Init();
        repo.CommitFile("file.txt", "initial content\n", "initial commit");
        var hash = repo.CommitFile("file.txt", "modified content\n", "second commit");
        using var git = await repo.OpenAsync();
        Assert.IsType<RevertResult.Success>(await git.RevertAsync(Oid.ParseHex(hash)));
        Assert.Equal("initial content\n", repo.ReadNormalized("file.txt"));
        Assert.StartsWith("Revert \"second commit\"", git.GetCommit(Oid.ParseHex(repo.Head()))!.Summary);
    }

    [Fact]
    public async Task Revert_with_conflicts()
    {
        using var repo = ConflictingRevert(out var hash);
        using var git = await repo.OpenAsync();
        var info = Assert.IsType<RevertResult.Conflict>(await git.RevertAsync(hash)).Info;
        Assert.Equal([(GitPath)"file.txt"], info.ConflictedFiles);
        Assert.True(InProgress(repo, "REVERT_HEAD"));
    }

    [Fact]
    public async Task Revert_abort()
    {
        using var repo = ConflictingRevert(out var hash);
        using var git = await repo.OpenAsync();
        Assert.IsType<RevertResult.Conflict>(await git.RevertAsync(hash));
        await git.RevertAbortAsync();
        Assert.False(InProgress(repo, "REVERT_HEAD"));
        Assert.Equal("line1\ndifferent_line2\nline3\n", repo.ReadNormalized("file.txt"));
    }

    [Theory]
    [InlineData(ConflictResolution.AcceptCurrent, "line1\ndifferent_line2\nline3\n")]
    [InlineData(ConflictResolution.AcceptIncoming, "line1\nline2\nline3\n")]
    [InlineData(ConflictResolution.AcceptBoth, "line1\ndifferent_line2\nline2\nline3\n")]
    public async Task Revert_resolve_and_continue(ConflictResolution resolution, string expected)
    {
        using var repo = ConflictingRevert(out var hash);
        var before = repo.Head();
        using var git = await repo.OpenAsync();
        Assert.IsType<RevertResult.Conflict>(await git.RevertAsync(hash));
        await git.ResolveConflictAsync("file.txt", resolution);
        Assert.Equal(expected, repo.ReadNormalized("file.txt"));
        await git.RevertContinueAsync();
        Assert.False(InProgress(repo, "REVERT_HEAD"));
        Assert.Equal(OperationState.None, await git.OperationStateAsync());
        // Keeping the current version leaves nothing to commit: the revert is skipped.
        Assert.Equal(resolution == ConflictResolution.AcceptCurrent, repo.Head() == before);
    }

    [Fact]
    public async Task Revert_merge_conflicts_detects_revert_state()
    {
        using var repo = ConflictingRevert(out var hash);
        using var git = await repo.OpenAsync();
        Assert.IsType<RevertResult.Conflict>(await git.RevertAsync(hash));
        var info = await git.MergeConflictsAsync();
        Assert.Equal([(GitPath)"file.txt"], info.ConflictedFiles);
        Assert.Equal(new OperationState(OperationKind.Revert, hash.ToShortHex(), 1), await git.OperationStateAsync());
        await git.RevertAbortAsync();
    }

    [Fact]
    public async Task Revert_of_a_file_addition_removes_it()
    {
        using var repo = TempRepo.Init();
        repo.CommitFile("file.txt", "content\n", "initial commit");
        var hash = repo.CommitFile("added.txt", "new file\n", "add new file");
        using var git = await repo.OpenAsync();
        Assert.IsType<RevertResult.Success>(await git.RevertAsync(Oid.ParseHex(hash)));
        Assert.False(repo.Exists("added.txt"));
    }

    [Fact]
    public async Task Revert_merge_commit()
    {
        using var repo = TempRepo.Init();
        repo.CommitFile("file.txt", "initial\n", "initial commit");
        repo.Raw("switch", "-q", "-c", "feature");
        repo.CommitFile("feature.txt", "feature content\n", "add feature file");
        repo.Raw("switch", "-q", "-");
        // The Rust test fast-forwarded here, so it never reverted a real merge.
        repo.Raw("merge", "-q", "--no-ff", "--no-edit", "feature");
        Assert.True(repo.Exists("feature.txt"));
        using var git = await repo.OpenAsync();
        Assert.IsType<RevertResult.Success>(await git.RevertAsync(Oid.ParseHex(repo.Head())));
        Assert.False(repo.Exists("feature.txt"));
    }

    [Fact]
    public async Task Revert_of_a_missing_commit_fails_with_a_code()
    {
        using var repo = TempRepo.WithCommit();
        using var git = await repo.OpenAsync();
        var e = await Assert.ThrowsAsync<GitOperationException>(() => git.RevertAsync(Oid.ParseHex(new string('1', 40))));
        Assert.Equal(GitErrorCode.NotFound, e.Code);
    }
}
