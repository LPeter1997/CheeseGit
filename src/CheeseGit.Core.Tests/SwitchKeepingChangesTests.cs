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

    /// <summary>
    /// main and "other" both change lines.txt (other changes line 10); on main, <paramref name="line1"/>
    /// replaces line 1 or <paramref name="line10"/> line 10 unstaged, b.txt is staged and new.txt untracked.
    /// git refuses a plain switch to other: lines.txt "would be overwritten by checkout".
    /// </summary>
    private static (TempRepo Repo, string Main) RefusedRepo(string? line1 = null, string? line10 = null)
    {
        var repo = TempRepo.Init();
        var lines = Enumerable.Range(1, 12).Select(i => $"line {i}").ToArray();
        repo.Write("lines.txt", string.Join("\n", lines) + "\n");
        repo.Write("b.txt", "b\n");
        repo.CommitAll("initial");
        var main = repo.CurrentBranch();
        repo.Raw("switch", "-q", "-c", "other");
        var theirs = (string[])lines.Clone();
        theirs[9] = "line 10 on other";
        repo.Write("lines.txt", string.Join("\n", theirs) + "\n");
        repo.CommitAll("other changes line 10");
        repo.Raw("switch", "-q", main);
        var ours = (string[])lines.Clone();
        if (line1 is not null) ours[0] = line1;
        if (line10 is not null) ours[9] = line10;
        repo.Write("lines.txt", string.Join("\n", ours) + "\n");
        repo.Write("b.txt", "b staged\n");
        repo.Raw("add", "b.txt");
        repo.Write("new.txt", "new\n");
        return (repo, main);
    }

    [Fact]
    public async Task Bringing_changes_git_refuses_merges_them_into_the_new_branch()
    {
        var (repo, _) = RefusedRepo(line1: "line 1 here");
        using var _r = repo;
        using var git = await repo.OpenAsync();
        // The precondition the fallback exists for.
        await Assert.ThrowsAnyAsync<GitException>(() => git.SwitchBranchAsync("other"));

        var outcome = await git.SwitchKeepingChangesAsync(false, ct => git.SwitchBranchAsync("other", ct));

        Assert.Equal(BringOutcome.Merged, outcome.Brought);
        Assert.Equal("other", repo.CurrentBranch());
        var text = File.ReadAllText(Path.Combine(repo.Path, "lines.txt")).Replace("\r", "");
        Assert.Contains("line 1 here\n", text);
        Assert.Contains("line 10 on other\n", text);
        // Staged stays staged, untracked stays untracked, and nothing is left in the stash.
        Assert.Equal("M  b.txt\n M lines.txt\n?? new.txt\n", Porcelain(repo));
        Assert.Empty(await git.ListStashesAsync());
    }

    [Fact]
    public async Task Brought_changes_that_clash_leave_conflicts_and_keep_the_stash()
    {
        var (repo, main) = RefusedRepo(line10: "line 10 on main");
        using var _r = repo;
        using var git = await repo.OpenAsync();

        var outcome = await git.SwitchKeepingChangesAsync(false, ct => git.SwitchBranchAsync("other", ct));

        Assert.Equal(BringOutcome.Conflicts, outcome.Brought);
        Assert.Equal("other", repo.CurrentBranch());
        Assert.Contains("UU lines.txt", Porcelain(repo));
        var kept = Assert.Single(await git.ListStashesAsync());
        Assert.EndsWith(GitRepository.BroughtChangesMessage + main, kept.Message);
    }

    [Fact]
    public async Task A_switch_failing_for_another_reason_leaves_the_changes_alone()
    {
        var (repo, main) = RefusedRepo(line1: "line 1 here");
        using var _r = repo;
        using var git = await repo.OpenAsync();
        var before = Porcelain(repo);

        await Assert.ThrowsAnyAsync<GitException>(() => git.SwitchKeepingChangesAsync(false, ct => git.SwitchBranchAsync("no-such-branch", ct)));

        Assert.Equal(main, repo.CurrentBranch());
        Assert.Equal(before, Porcelain(repo));
        Assert.Empty(await git.ListStashesAsync());
    }
}
