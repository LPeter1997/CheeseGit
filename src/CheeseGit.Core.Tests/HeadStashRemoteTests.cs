using System.Text;
using CheeseGit.Core.Diff;
using CheeseGit.Core.Git;
using CheeseGit.Core.Repository;

namespace CheeseGit.Core.Tests;

/// <summary>Port of git_provider.rs: HEAD state and history browsing, stashes, remote management, many paths.</summary>
public class HeadStashRemoteTests
{
    private static TempRepo RepoWithTwoCommits(out Oid first)
    {
        var repo = RepositoryTests.RepoWithCommit();
        first = Oid.ParseHex(repo.Head());
        repo.CommitFile("hello.txt", "modified content", "second commit");
        return repo;
    }

    // ── HEAD and history browsing ─────────────────────────────────────

    [Fact]
    public async Task Head_state_on_branch()
    {
        using var repo = RepositoryTests.RepoWithCommit();
        using var git = await repo.OpenAsync();
        var state = await git.HeadAsync();
        Assert.False(state.BrowsingHistory);
        Assert.Equal(repo.CurrentBranch(), state.Branch);
        Assert.Equal(repo.Head(), state.Commit!.Value.ToHex());
    }

    [Fact]
    public async Task Head_state_of_unborn_branch()
    {
        using var repo = TempRepo.Init();
        using var git = await repo.OpenAsync();
        var state = await git.HeadAsync();
        Assert.True(state.IsUnborn);
        Assert.False(state.BrowsingHistory);
        Assert.NotNull(state.Branch);
    }

    [Fact]
    public async Task Checkout_commit_detaches_head()
    {
        using var repo = RepoWithTwoCommits(out var first);
        using var git = await repo.OpenAsync();
        await git.CheckoutCommitAsync(first);
        Assert.True((await git.HeadAsync()).BrowsingHistory);
        Assert.Null(await git.CurrentBranchAsync());
        Assert.Equal("HEAD", repo.CurrentBranch());
    }

    [Fact]
    public async Task Checkout_commit_then_switch_branch_reattaches()
    {
        using var repo = RepoWithTwoCommits(out var first);
        using var git = await repo.OpenAsync();
        var original = repo.CurrentBranch();
        await git.CheckoutCommitAsync(first);
        Assert.True((await git.HeadAsync()).BrowsingHistory);
        await git.SwitchBranchAsync(original);
        var state = await git.HeadAsync();
        Assert.False(state.BrowsingHistory);
        Assert.Equal(original, state.Branch);
    }

    [Fact]
    public async Task Checkout_commit_fails_with_dirty_working_tree()
    {
        using var repo = RepoWithTwoCommits(out var first);
        repo.Write("hello.txt", "dirty changes");
        using var git = await repo.OpenAsync();
        await Assert.ThrowsAsync<GitCommandException>(() => git.CheckoutCommitAsync(first));
        Assert.False((await git.HeadAsync()).BrowsingHistory);
    }

    [Fact]
    public async Task Create_branch_from_detached_head()
    {
        using var repo = RepoWithTwoCommits(out var first);
        using var git = await repo.OpenAsync();
        await git.CheckoutCommitAsync(first);
        await git.CreateBranchAsync("new-feature");
        var state = await git.HeadAsync();
        Assert.False(state.BrowsingHistory);
        Assert.Equal("new-feature", state.Branch);
        Assert.Equal(first, state.Commit);
    }

    [Fact]
    public async Task Switch_branch_works_with_slashes_in_name()
    {
        using var repo = RepositoryTests.RepoWithCommit();
        using var git = await repo.OpenAsync();
        var main = repo.CurrentBranch();
        await git.CreateBranchAsync("feature/my-thing");
        Assert.Equal("feature/my-thing", await git.CurrentBranchAsync());
        await git.SwitchBranchAsync(main);
        await git.SwitchBranchAsync("feature/my-thing");
        var state = await git.HeadAsync();
        Assert.False(state.BrowsingHistory);
        Assert.Equal("feature/my-thing", state.Branch);
    }

    [Fact]
    public async Task Switching_to_a_branch_tip_with_slashes_is_not_browsing_history()
    {
        using var repo = RepositoryTests.RepoWithCommit();
        using var git = await repo.OpenAsync();
        var main = repo.CurrentBranch();
        await git.CreateBranchAsync("feature/xyz");
        var tip = repo.CommitFile("feature.txt", "feature work", "feature commit");
        await git.SwitchBranchAsync(main);
        Assert.Contains(await git.ListBranchesAsync(), b => b.Name == "feature/xyz");

        await git.SwitchBranchAsync("feature/xyz");
        var state = await git.HeadAsync();
        Assert.False(state.BrowsingHistory);
        Assert.Equal("feature/xyz", state.Branch);
        Assert.Equal(tip, state.Commit!.Value.ToHex());
    }

    [Fact]
    public async Task Head_state_returns_context_branch_when_browsing_history()
    {
        using var repo = RepositoryTests.RepoWithCommit();
        using var git = await repo.OpenAsync();
        await git.CheckoutCommitAsync(Oid.ParseHex(repo.Head()));
        var state = await git.HeadAsync();
        Assert.True(state.BrowsingHistory);
        Assert.Equal(repo.Raw("for-each-ref", "--format=%(refname:short)", "refs/heads/").Trim(), state.Branch);
    }

    [Fact]
    public async Task Head_state_returns_branch_with_slashes_in_history()
    {
        using var repo = RepositoryTests.RepoWithCommit();
        using var git = await repo.OpenAsync();
        var main = repo.CurrentBranch();
        await git.CreateBranchAsync("feature/dashboard");
        var tip = repo.CommitFile("dash.txt", "d", "dashboard commit");
        await git.SwitchBranchAsync(main);
        await git.CheckoutCommitAsync(Oid.ParseHex(tip));
        var state = await git.HeadAsync();
        Assert.True(state.BrowsingHistory);
        Assert.Equal("feature/dashboard", state.Branch);
    }

    [Fact]
    public async Task Head_state_context_branch_for_shared_commit()
    {
        using var repo = RepositoryTests.RepoWithCommit();
        var initial = Oid.ParseHex(repo.Head());
        using var git = await repo.OpenAsync();
        await git.CreateBranchAsync("other-branch");
        repo.CommitFile("other.txt", "other", "other commit");
        await git.CheckoutCommitAsync(initial);
        var state = await git.HeadAsync();
        Assert.True(state.BrowsingHistory);
        Assert.False(string.IsNullOrEmpty(state.Branch));
        Assert.DoesNotContain("(", state.Branch);
    }

    // ── Diff content ──────────────────────────────────────────────────

    [Fact]
    public async Task Diff_file_shows_old_and_new_content_correctly()
    {
        using var repo = RepositoryTests.RepoWithCommit();
        repo.Write("hello.txt", "world\n");
        using var git = await repo.OpenAsync();
        var lines = (await git.DiffFileAsync("hello.txt", DiffArea.Unstaged)).Hunks[0].Lines;
        Assert.Equal(["hello"], lines.Where(l => l.Kind == DiffLineKind.Deletion).Select(l => l.Text));
        Assert.Equal(["world"], lines.Where(l => l.Kind == DiffLineKind.Addition).Select(l => l.Text));
        Assert.True(lines.Single(l => l.Kind == DiffLineKind.Deletion).NoNewlineAtEnd);
    }

    [Fact]
    public async Task Diff_commit_file_shows_old_and_new_content_correctly()
    {
        using var repo = RepositoryTests.RepoWithCommit();
        var hash = repo.CommitFile("hello.txt", "world\n", "change hello to world");
        using var git = await repo.OpenAsync();
        var lines = (await git.DiffCommitFileAsync(Oid.ParseHex(hash), "hello.txt")).Hunks[0].Lines;
        Assert.Equal(["hello"], lines.Where(l => l.Kind == DiffLineKind.Deletion).Select(l => l.Text));
        Assert.Equal(["world"], lines.Where(l => l.Kind == DiffLineKind.Addition).Select(l => l.Text));
    }

    [Fact]
    public async Task Show_file_at_commit_returns_raw_bytes()
    {
        using var repo = RepositoryTests.RepoWithCommit();
        var first = repo.Head();
        repo.CommitFile("hello.txt", "changed", "second");
        using var git = await repo.OpenAsync();
        Assert.Equal("hello", Encoding.UTF8.GetString((await git.ShowFileAsync(first, "hello.txt"))!));
        Assert.Null(await git.ShowFileAsync(first, "missing.txt"));
    }

    // ── Stashes ───────────────────────────────────────────────────────

    private static async Task StashChange(GitRepository git, TempRepo repo, string content, string message)
    {
        repo.Write("hello.txt", content);
        await git.StageAsync(["hello.txt"]);
        await git.StashStagedAsync(message);
    }

    [Fact]
    public async Task Stash_staged_creates_stash_entry()
    {
        using var repo = RepositoryTests.RepoWithCommit();
        using var git = await repo.OpenAsync();
        await StashChange(git, repo, "stashed content", "my stash message");
        var stash = Assert.Single(await git.ListStashesAsync());
        Assert.Equal(0, stash.Index);
        Assert.Contains("my stash message", stash.Message);
        Assert.Equal("Test User", stash.Author);
        Assert.Equal(repo.Raw("rev-parse", "stash@{0}").Trim(), stash.Oid.ToHex());
        Assert.Empty((await git.StatusAsync()).Staged);
    }

    [Fact]
    public async Task Stash_staged_keeps_unstaged_changes()
    {
        using var repo = RepositoryTests.RepoWithCommit();
        repo.Write("other.txt", "unstaged and untracked");
        using var git = await repo.OpenAsync();
        await StashChange(git, repo, "stashed content", "s");
        Assert.True(repo.Exists("other.txt"));
        Assert.Equal("hello", repo.ReadNormalized("hello.txt"));
    }

    [Fact]
    public async Task List_stashes_empty_repo()
    {
        using var repo = RepositoryTests.RepoWithCommit();
        using var git = await repo.OpenAsync();
        Assert.Empty(await git.ListStashesAsync());
    }

    [Fact]
    public async Task Stash_apply_restores_changes()
    {
        using var repo = RepositoryTests.RepoWithCommit();
        using var git = await repo.OpenAsync();
        await StashChange(git, repo, "stashed", "test stash");
        await git.StashApplyAsync(0);
        Assert.Single(await git.ListStashesAsync());
        Assert.Equal("stashed", repo.ReadNormalized("hello.txt"));
    }

    [Fact]
    public async Task Stash_pop_restores_and_removes()
    {
        using var repo = RepositoryTests.RepoWithCommit();
        using var git = await repo.OpenAsync();
        await StashChange(git, repo, "stashed", "test stash");
        await git.StashPopAsync(0);
        Assert.Empty(await git.ListStashesAsync());
        Assert.Equal("stashed", repo.ReadNormalized("hello.txt"));
    }

    [Fact]
    public async Task Stash_drop_removes_entry()
    {
        using var repo = RepositoryTests.RepoWithCommit();
        using var git = await repo.OpenAsync();
        await StashChange(git, repo, "one", "stash one");
        await StashChange(git, repo, "two", "stash two");
        Assert.Equal(2, (await git.ListStashesAsync()).Count);
        await git.StashDropAsync(0);
        Assert.Contains("stash one", Assert.Single(await git.ListStashesAsync()).Message);
    }

    [Fact]
    public async Task List_stash_files_returns_changed_files()
    {
        using var repo = RepositoryTests.RepoWithCommit();
        repo.Write("new.txt", "new");
        using var git = await repo.OpenAsync();
        await git.StageAsync(["new.txt"]);
        await StashChange(git, repo, "changed", "multi file stash");
        var files = await git.ListStashFilesAsync(0);
        Assert.Contains(new FileChange("hello.txt", FileStatus.Modified), files);
        Assert.Contains(new FileChange("new.txt", FileStatus.Added), files);
    }

    [Fact]
    public async Task Diff_stats_and_content_of_a_stash_file()
    {
        using var repo = RepositoryTests.RepoWithCommit();
        using var git = await repo.OpenAsync();
        await StashChange(git, repo, "stash file content\n", "content stash");

        var diff = await git.DiffStashFileAsync(0, "hello.txt");
        Assert.Equal("hello.txt", diff.Path.ToString());
        Assert.NotEmpty(diff.Hunks);
        Assert.Contains(await git.StashFileStatsAsync(0), s => s.Path.ToString() == "hello.txt");
        Assert.Equal("stash file content", Encoding.UTF8.GetString((await git.ShowFileAtStashAsync(0, "hello.txt"))!).Trim());
    }

    [Fact]
    public async Task Stash_show_ignores_stash_show_config()
    {
        using var repo = RepositoryTests.RepoWithCommit();
        repo.Raw("config", "stash.showPatch", "true");
        repo.Raw("config", "stash.showStat", "true");
        using var git = await repo.OpenAsync();
        await StashChange(git, repo, "x\n", "s");
        Assert.Equal([new FileChange("hello.txt", FileStatus.Modified)], await git.ListStashFilesAsync(0));
    }

    [Fact]
    public async Task Multiple_stashes_ordered_correctly()
    {
        using var repo = RepositoryTests.RepoWithCommit();
        using var git = await repo.OpenAsync();
        await StashChange(git, repo, "first", "first stash");
        await StashChange(git, repo, "second", "second stash");
        var stashes = await git.ListStashesAsync();
        Assert.Equal(2, stashes.Count);
        Assert.Contains("second stash", stashes[0].Message);
        Assert.Equal(0, stashes[0].Index);
        Assert.Contains("first stash", stashes[1].Message);
        Assert.Equal(1, stashes[1].Index);
    }

    [Fact]
    public async Task Untracked_files_of_a_stash_are_listed_diffed_and_counted()
    {
        // git stash show leaves them out (they live in the stash's third parent); the old app did too.
        using var repo = RepositoryTests.RepoWithCommit();
        repo.Write("hello.txt", "changed\n");
        repo.Write("notes/new.md", "one\ntwo\n");
        repo.Raw("stash", "push", "-q", "--include-untracked", "-m", "with untracked");
        using var git = await repo.OpenAsync();
        var stash = Assert.Single(await git.ListStashesAsync()).Oid;

        var files = await git.ListStashFilesAsync(stash);
        Assert.Equal(2, files.Count);
        Assert.Contains(new FileChange("hello.txt", FileStatus.Modified), files);
        Assert.Contains(new FileChange("notes/new.md", FileStatus.Untracked), files);
        Assert.Equal(files, await git.ListStashFilesAsync(0));

        var diff = await git.DiffStashFileAsync(stash, "notes/new.md");
        Assert.Equal("notes/new.md", diff.Path.ToString());
        Assert.Equal(2, diff.Hunks.Sum(h => h.Lines.Count(l => l.Kind == DiffLineKind.Addition)));
        Assert.Contains(await git.StashFileStatsAsync(stash), s => s.Path.ToString() == "notes/new.md" && s.Additions == 2);
    }

    [Fact]
    public async Task A_stash_without_untracked_files_lists_only_its_changes()
    {
        using var repo = RepositoryTests.RepoWithCommit();
        repo.Write("loose.txt", "not stashed");
        using var git = await repo.OpenAsync();
        await StashChange(git, repo, "x\n", "s");
        var stash = Assert.Single(await git.ListStashesAsync()).Oid;
        Assert.Equal([new FileChange("hello.txt", FileStatus.Modified)], await git.ListStashFilesAsync(stash));
    }

    [Fact]
    public async Task Popping_into_conflicts_reports_them_and_keeps_the_stash()
    {
        using var repo = RepositoryTests.RepoWithCommit();
        using var git = await repo.OpenAsync();
        await StashChange(git, repo, "stashed\n", "s");
        repo.CommitFile("hello.txt", "committed meanwhile\n", "meanwhile");

        Assert.Equal(StashApplyResult.Conflicts, await git.StashPopAsync(0));
        Assert.Single(await git.ListStashesAsync());
        Assert.Contains("hello.txt", repo.Raw("diff", "--name-only", "--diff-filter=U"));
    }

    [Fact]
    public async Task A_clean_apply_reports_applied()
    {
        using var repo = RepositoryTests.RepoWithCommit();
        using var git = await repo.OpenAsync();
        await StashChange(git, repo, "stashed\n", "s");
        Assert.Equal(StashApplyResult.Applied, await git.StashApplyAsync(0));
    }

    // ── Remote management ─────────────────────────────────────────────

    [Fact]
    public async Task Add_remote_to_repo()
    {
        using var bare = TempRepo.InitBare();
        using var repo = RepositoryTests.RepoWithCommit();
        using var git = await repo.OpenAsync();
        await git.AddRemoteAsync("origin", bare.Path);
        Assert.Equal([new RemoteInfo("origin", bare.Path)], await git.ListRemotesAsync());
    }

    [Fact]
    public async Task Add_unreachable_remote_is_rolled_back()
    {
        using var repo = RepositoryTests.RepoWithCommit();
        using var git = await repo.OpenAsync();
        await Assert.ThrowsAsync<GitCommandException>(() => git.AddRemoteAsync("broken", Path.Combine(repo.Path, "no-such-repo")));
        Assert.Empty(await git.ListRemotesAsync());
    }

    [Fact]
    public async Task Add_remote_duplicate_name_fails()
    {
        using var bare = TempRepo.InitBare();
        using var clone = TempRepo.CloneOf(bare);
        using var git = await clone.OpenAsync();
        await Assert.ThrowsAsync<GitCommandException>(() => git.AddRemoteAsync("origin", "https://example.com/dup.git"));
    }

    [Fact]
    public async Task Remove_remote_from_repo()
    {
        using var bare = TempRepo.InitBare();
        using var clone = TempRepo.CloneOf(bare);
        using var git = await clone.OpenAsync();
        Assert.Equal("origin", Assert.Single(await git.ListRemotesAsync()).Name);
        await git.RemoveRemoteAsync("origin");
        Assert.Empty(await git.ListRemotesAsync());
    }

    [Fact]
    public async Task Remove_nonexistent_remote_fails()
    {
        using var repo = RepositoryTests.RepoWithCommit();
        using var git = await repo.OpenAsync();
        await Assert.ThrowsAsync<GitCommandException>(() => git.RemoveRemoteAsync("nonexistent"));
    }

    [Fact]
    public async Task Add_multiple_remotes()
    {
        using var bare1 = TempRepo.InitBare();
        using var bare2 = TempRepo.InitBare();
        using var repo = RepositoryTests.RepoWithCommit();
        using var git = await repo.OpenAsync();
        await git.AddRemoteAsync("origin", bare1.Path);
        await git.AddRemoteAsync("mirror", bare2.Path);
        Assert.Equal(["origin", "mirror"], (await git.ListRemotesAsync()).Select(r => r.Name));
    }

    [Fact]
    public async Task Remote_names_that_look_like_options_are_refused()
    {
        using var repo = RepositoryTests.RepoWithCommit();
        using var git = await repo.OpenAsync();
        var e = await Assert.ThrowsAsync<GitOperationException>(() => git.FetchAsync("--upload-pack=touch pwned"));
        Assert.Equal(GitErrorCode.InvalidName, e.Code);
    }

    // ── Many paths (#47) ──────────────────────────────────────────────

    private static List<string> ManyFiles(TempRepo repo)
    {
        var names = new List<string>();
        for (var i = 0; i < 500; i++)
        {
            var name = $"deeply/nested/directory/structure/component_number_{i:0000}_with_a_long_filename.txt";
            repo.Write(name, $"content {i}\n");
            names.Add(name);
        }
        return names;
    }

    // The Rust provider split long path lists into several command lines.
    // Paths now go through stdin, so any number of them takes one git call.

    [Fact]
    public async Task Stage_and_unstage_many_paths_in_one_call()
    {
        using var repo = RepositoryTests.RepoWithCommit();
        var names = ManyFiles(repo);
        using var git = await repo.OpenAsync();
        var log = new List<GitInvocation>();
        repo.Git.Invoked += log.Add;

        await git.StageAsync(names.Select(GitPath.FromString));
        Assert.Single(log, e => e.Args.Contains("add"));
        Assert.Equal(500, (await git.StatusAsync()).Staged.Count());

        await git.UnstageAsync(names.Select(GitPath.FromString));
        Assert.Single(log, e => e.Args.Contains("reset"));
        Assert.Empty((await git.StatusAsync()).Staged);
    }

    [Fact]
    public async Task Discard_many_paths()
    {
        using var repo = RepositoryTests.RepoWithCommit();
        var names = ManyFiles(repo);
        repo.Write("hello.txt", "modified");
        using var git = await repo.OpenAsync();
        await git.DiscardUnstagedAsync([.. names.Select(GitPath.FromString), "hello.txt"]);
        Assert.Empty((await git.StatusAsync()).Unstaged);
        Assert.False(repo.Exists(names[0]));
        Assert.Equal("hello", repo.ReadNormalized("hello.txt"));
    }
}
