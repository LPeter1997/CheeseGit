using CheeseGit.Core.Git;
using CheeseGit.Core.Repository;

namespace CheeseGit.Core.Tests;

/// <summary>#52: leaving local changes on a branch when switching, and getting them back.</summary>
public class SwitchKeepingChangesTests
{
    /// <summary>main with a commit and a branch "feature/ü" at the same commit; on main, a staged, an unstaged and an untracked change.</summary>
    private static (TempRepo Repo, string Main) DirtyRepo()
    {
        var repo = TempRepo.Init();
        repo.Write("tracked.txt", "one\n");
        repo.Write("staged.txt", "one\n");
        repo.CommitAll("initial");
        repo.Raw("branch", "feature/ü");
        repo.Write("tracked.txt", "two\n");
        repo.Write("staged.txt", "two\n");
        repo.Raw("add", "staged.txt");
        repo.Write("new.txt", "new\n");
        return (repo, repo.CurrentBranch());
    }

    private static string Porcelain(TempRepo repo) => repo.Raw("status", "--porcelain", "--untracked-files=all").Replace("\r", "");

    [Fact]
    public async Task Left_changes_stay_on_their_branch_and_come_back()
    {
        var (repo, main) = DirtyRepo();
        using var _ = repo;
        var dirty = Porcelain(repo);
        using var git = await repo.OpenAsync();

        var outcome = await git.SwitchKeepingChangesAsync(true, ct => git.SwitchBranchAsync("feature/ü", ct));
        Assert.Equal(new SwitchOutcome(true, false, false, false), outcome);
        Assert.Equal("feature/ü", repo.CurrentBranch());
        Assert.Equal("", Porcelain(repo));
        Assert.EndsWith(": " + GitRepository.LeftChangesMessage + main, Assert.Single(await git.ListStashesAsync()).Message);

        outcome = await git.SwitchKeepingChangesAsync(false, ct => git.SwitchBranchAsync(main, ct));
        Assert.Equal(new SwitchOutcome(false, true, false, false), outcome);
        // Staged stays staged, untracked comes back, and the stash entry is gone.
        Assert.Equal(dirty, Porcelain(repo));
        Assert.Empty(await git.ListStashesAsync());
    }

    [Fact]
    public async Task Bringing_changes_carries_them_as_git_does()
    {
        var (repo, _) = DirtyRepo();
        using var __ = repo;
        var dirty = Porcelain(repo);
        using var git = await repo.OpenAsync();

        var outcome = await git.SwitchKeepingChangesAsync(false, ct => git.SwitchBranchAsync("feature/ü", ct));
        Assert.Equal(SwitchOutcome.Nothing, outcome);
        Assert.Equal(dirty, Porcelain(repo));
        Assert.Empty(await git.ListStashesAsync());
    }

    [Fact]
    public async Task A_failed_switch_puts_the_changes_back()
    {
        var (repo, main) = DirtyRepo();
        using var _ = repo;
        var dirty = Porcelain(repo);
        using var git = await repo.OpenAsync();

        await Assert.ThrowsAnyAsync<GitException>(() => git.SwitchKeepingChangesAsync(true, ct => git.SwitchBranchAsync("no-such-branch", ct)));
        Assert.Equal(main, repo.CurrentBranch());
        Assert.Equal(dirty, Porcelain(repo));
        Assert.Empty(await git.ListStashesAsync());
    }

    [Fact]
    public async Task Left_changes_wait_while_the_tree_has_other_changes()
    {
        var (repo, main) = DirtyRepo();
        using var _ = repo;
        using var git = await repo.OpenAsync();
        await git.SwitchKeepingChangesAsync(true, ct => git.SwitchBranchAsync("feature/ü", ct));

        repo.Write("other.txt", "made on feature\n");
        var outcome = await git.SwitchKeepingChangesAsync(false, ct => git.SwitchBranchAsync(main, ct));
        Assert.Equal(new SwitchOutcome(false, false, true, false), outcome);
        Assert.Single(await git.ListStashesAsync());
        Assert.Equal("?? other.txt\n", Porcelain(repo));
    }

    [Fact]
    public async Task Leaving_a_clean_tree_makes_no_stash()
    {
        using var repo = TempRepo.Init();
        repo.Write("a.txt", "a\n");
        repo.CommitAll("initial");
        repo.Raw("branch", "other");
        using var git = await repo.OpenAsync();

        var outcome = await git.SwitchKeepingChangesAsync(true, ct => git.SwitchBranchAsync("other", ct));
        Assert.Equal(SwitchOutcome.Nothing, outcome);
        Assert.Empty(await git.ListStashesAsync());
    }

    [Fact]
    public async Task A_user_stash_with_a_similar_message_is_not_taken()
    {
        var (repo, main) = DirtyRepo();
        using var _ = repo;
        repo.Raw("stash", "push", "-q", "--include-untracked", "-m", "notes about " + main);
        using var git = await repo.OpenAsync();
        await git.SwitchBranchAsync("feature/ü");

        var outcome = await git.SwitchKeepingChangesAsync(false, ct => git.SwitchBranchAsync(main, ct));
        Assert.Equal(SwitchOutcome.Nothing, outcome);
        Assert.Single(await git.ListStashesAsync());
    }
}
