using CheeseGit.Core.Git;
using CheeseGit.Core.Repository;

namespace CheeseGit.Core.Tests;

/// <summary>Port of remote_branches.rs and undo_last_commit.rs.</summary>
public class RemoteBranchAndUndoTests
{
    private static TempRepo PublishedRepo(TempRepo bare)
    {
        var local = RepositoryTests.RepoWithCommit();
        local.Raw("remote", "add", "origin", bare.Path);
        local.Raw("push", "-q", "-u", "origin", "HEAD");
        return local;
    }

    // ── Remote branches ───────────────────────────────────────────────

    [Fact]
    public async Task List_remote_branches_returns_only_remote_only()
    {
        using var bare = TempRepo.InitBare();
        using var local = PublishedRepo(bare);
        var main = local.CurrentBranch();
        local.Raw("switch", "-q", "-c", "feature/local");
        local.CommitFile("feature.txt", "feature content", "feat: add feature file");
        local.Raw("push", "-q", "-u", "origin", "feature/local");
        local.Raw("switch", "-q", main);

        var mainHash = bare.Raw("rev-parse", main).Trim();
        bare.Raw("branch", "feature/remote-only-a", mainHash);
        bare.Raw("branch", "feature/remote-only-b", mainHash);
        local.Raw("fetch", "-q", "origin");

        using var git = await local.OpenAsync();
        var branches = await git.ListRemoteBranchesAsync();
        Assert.Equal(["feature/remote-only-a", "feature/remote-only-b"], branches.Select(b => b.Name).Order(StringComparer.Ordinal));
        Assert.All(branches, b =>
        {
            Assert.Equal("origin", b.Remote);
            Assert.Equal(mainHash, b.Target.ToHex());
            Assert.NotEqual(default, b.LastCommitDate);
        });
    }

    [Fact]
    public async Task List_remote_branches_empty_when_no_remotes()
    {
        using var local = RepositoryTests.RepoWithCommit();
        using var git = await local.OpenAsync();
        Assert.Empty(await git.ListRemoteBranchesAsync());
    }

    [Fact]
    public async Task Switch_to_remote_branch_creates_local_tracking()
    {
        using var bare = TempRepo.InitBare();
        using var local = PublishedRepo(bare);
        bare.Raw("branch", "feature/remote-checkout", local.Head());
        local.Raw("fetch", "-q", "origin");

        using var git = await local.OpenAsync();
        Assert.Contains(await git.ListRemoteBranchesAsync(), b => b.Name == "feature/remote-checkout");

        await git.SwitchBranchAsync("feature/remote-checkout");
        Assert.Equal("feature/remote-checkout", (await git.ListBranchesAsync()).Single(b => b.IsCurrent).Name);
        Assert.Equal("origin/feature/remote-checkout", (await git.ListBranchesAsync()).Single(b => b.IsCurrent).Upstream);
        Assert.DoesNotContain(await git.ListRemoteBranchesAsync(), b => b.Name == "feature/remote-checkout");
    }

    [Fact]
    public async Task Checkout_remote_branch_picks_the_remote_when_two_have_it()
    {
        using var bare = TempRepo.InitBare();
        using var other = TempRepo.InitBare();
        using var local = PublishedRepo(bare);
        local.Raw("remote", "add", "upstream", other.Path);
        local.Raw("push", "-q", "upstream", "HEAD:refs/heads/shared");
        local.Raw("push", "-q", "origin", "HEAD:refs/heads/shared");
        local.Raw("fetch", "-q", "--all");

        using var git = await local.OpenAsync();
        // --guess refuses a name that two remotes have.
        await Assert.ThrowsAnyAsync<GitException>(() => git.SwitchBranchAsync("shared"));

        await git.CheckoutRemoteBranchAsync("upstream", "shared");
        var current = (await git.ListBranchesAsync()).Single(b => b.IsCurrent);
        Assert.Equal("shared", current.Name);
        Assert.Equal("upstream/shared", current.Upstream);
    }

    // ── Undo last commit ──────────────────────────────────────────────

    [Fact]
    public async Task Undo_last_commit_restores_message_and_staged_changes()
    {
        using var repo = RepositoryTests.RepoWithCommit();
        var initialSummary = repo.Raw("log", "-1", "--format=%s").Trim();
        repo.Write("hello.txt", "line1 changed\nline2\nline3\nline4\n");
        using var git = await repo.OpenAsync();
        await git.StageAsync(["hello.txt"]);
        await git.CommitAsync("feat: update hello", "body line 1\nbody line 2");

        Assert.Equal(new RestoredCommitMessage("feat: update hello", "body line 1\nbody line 2"), await git.UndoLastCommitAsync());

        var status = await git.StatusAsync();
        Assert.Empty(status.Unstaged);
        Assert.Contains(status.Staged, c => c.Path == "hello.txt");
        Assert.Equal(initialSummary, (await git.CommitLogAsync(1))[0].Summary);
    }

    [Fact]
    public async Task Undo_last_commit_rejects_if_latest_commit_is_pushed()
    {
        using var bare = TempRepo.InitBare();
        using var repo = PublishedRepo(bare);
        repo.Write("hello.txt", "line1\nline2\nline3 changed\nline4\n");
        using var git = await repo.OpenAsync();
        await git.StageAsync(["hello.txt"]);
        await git.CommitAsync("feat: pushed commit");
        repo.Raw("push", "-q", "origin", repo.CurrentBranch());

        var head = repo.Head();
        var e = await Assert.ThrowsAsync<GitOperationException>(() => git.UndoLastCommitAsync());
        Assert.Equal(GitErrorCode.AlreadyPushed, e.Code);
        Assert.Equal(head, repo.Head());
    }

    [Fact]
    public async Task Undo_last_commit_allows_when_no_remote_configured()
    {
        using var repo = RepositoryTests.RepoWithCommit();
        repo.Write("hello.txt", "local change only\n");
        using var git = await repo.OpenAsync();
        await git.StageAsync(["hello.txt"]);
        await git.CommitAsync("feat: local only");
        Assert.Equal(new RestoredCommitMessage("feat: local only", ""), await git.UndoLastCommitAsync());
    }

    [Fact]
    public async Task Undo_last_commit_allows_when_remote_exists_but_commit_is_unpushed()
    {
        using var bare = TempRepo.InitBare();
        using var repo = PublishedRepo(bare);
        repo.Write("hello.txt", "unpushed local commit\n");
        using var git = await repo.OpenAsync();
        await git.StageAsync(["hello.txt"]);
        await git.CommitAsync("feat: local ahead");
        Assert.Equal("feat: local ahead", (await git.UndoLastCommitAsync()).Summary);
    }

    [Fact]
    public async Task Undo_last_commit_preserves_partial_staging_state()
    {
        using var repo = RepositoryTests.RepoWithCommit();
        repo.Write("hello.txt", "line1 changed\nline2\nline3\nline4\n");
        using var git = await repo.OpenAsync();
        await git.StageAsync(["hello.txt"]);
        repo.Write("hello.txt", "line1 changed\nline2\nline3 changed\nline4\n");

        var partial = await git.StatusAsync();
        Assert.Contains(partial.Staged, c => c.Path == "hello.txt");
        Assert.Contains(partial.Unstaged, c => c.Path == "hello.txt");

        await git.CommitAsync("feat: partial", "partial body");
        var afterCommit = await git.StatusAsync();
        Assert.Empty(afterCommit.Staged);
        Assert.Contains(afterCommit.Unstaged, c => c.Path == "hello.txt");

        Assert.Equal(new RestoredCommitMessage("feat: partial", "partial body"), await git.UndoLastCommitAsync());
        var afterUndo = await git.StatusAsync();
        Assert.Contains(afterUndo.Staged, c => c.Path == "hello.txt");
        Assert.Contains(afterUndo.Unstaged, c => c.Path == "hello.txt");
    }

    [Fact]
    public async Task Undo_initial_commit_and_detached_head_fail_with_codes()
    {
        using var repo = RepositoryTests.RepoWithCommit();
        using var git = await repo.OpenAsync();
        Assert.Equal(GitErrorCode.InitialCommit, (await Assert.ThrowsAsync<GitOperationException>(() => git.UndoLastCommitAsync())).Code);

        repo.CommitFile("hello.txt", "two", "second");
        await git.CheckoutCommitAsync(Oid.ParseHex(repo.Head()));
        Assert.Equal(GitErrorCode.DetachedHead, (await Assert.ThrowsAsync<GitOperationException>(() => git.UndoLastCommitAsync())).Code);
    }
}
