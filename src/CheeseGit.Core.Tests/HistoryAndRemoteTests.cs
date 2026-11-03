using CheeseGit.Core.Git;
using CheeseGit.Core.Graph;
using CheeseGit.Core.Repository;

namespace CheeseGit.Core.Tests;

/// <summary>Port of git_provider.rs: branch graph, remotes, fetch/push, local-only markers.</summary>
public class HistoryAndRemoteTests
{
    private static TempRepo RepoWithCommits(int n)
    {
        var repo = RepositoryTests.RepoWithCommit();
        for (var i = 1; i < n; i++) repo.CommitFile("hello.txt", $"content {i}", $"commit {i}");
        return repo;
    }

    /// <summary>A clone of an empty bare repository with one pushed commit.</summary>
    private static (TempRepo Origin, TempRepo Clone) ClonedWithPushedCommit()
    {
        var origin = TempRepo.InitBare();
        var clone = TempRepo.CloneOf(origin);
        clone.Write("file.txt", "hello");
        clone.CommitAll("initial");
        clone.Raw("push", "-q", "origin", "HEAD");
        return (origin, clone);
    }

    // ── History graph ─────────────────────────────────────────────────

    [Fact]
    public async Task Graph_returns_commits_and_branches()
    {
        using var repo = RepositoryTests.RepoWithCommit();
        using var git = await repo.OpenAsync();
        var graph = await git.LoadHistoryAsync(new HistoryRequest());
        Assert.Equal(1, graph.Dag.Count);
        Assert.Equal("initial commit", git.GetCommit(graph.Dag.OidAt(0))!.Summary);
        Assert.NotEmpty(graph.Branches);
        Assert.Empty(graph.LocalOnly);
    }

    [Fact]
    public async Task Graph_includes_all_local_branches()
    {
        using var repo = RepositoryTests.RepoWithCommit();
        using var git = await repo.OpenAsync();
        var main = repo.CurrentBranch();
        await git.CreateBranchAsync("feature");
        repo.CommitFile("feat.txt", "feat", "feature commit");
        await git.SwitchBranchAsync(main);
        var graph = await git.LoadHistoryAsync(new HistoryRequest());
        Assert.Equal(2, graph.Dag.Count);
        Assert.Contains("refs/heads/feature", graph.Branches);
    }

    [Fact]
    public async Task Graph_filters_to_specified_branches()
    {
        using var repo = RepositoryTests.RepoWithCommit();
        using var git = await repo.OpenAsync();
        var main = repo.CurrentBranch();
        repo.CommitFile("main-only.txt", "m", "main only");
        await git.CreateBranchAsync("feature", startPoint: "HEAD~1");
        repo.CommitFile("feat.txt", "feat", "feature commit");

        var graph = await git.LoadHistoryAsync(new HistoryRequest(["feature"]));
        Assert.Equal(["refs/heads/feature"], graph.Branches);
        // Hiding a branch filters by reachability: main's own commit is gone.
        Assert.Equal(2, graph.Dag.Count);
        Assert.DoesNotContain(Enumerable.Range(0, graph.Dag.Count), i => git.GetCommit(graph.Dag.OidAt(i))!.Summary == "main only");
        _ = main;
    }

    [Theory]
    [InlineData(10, 3, 3)]
    [InlineData(10, null, 10)]
    [InlineData(5, 100, 5)]
    public async Task Graph_max_commits(int total, int? max, int expected)
    {
        using var repo = RepoWithCommits(total);
        using var git = await repo.OpenAsync();
        var graph = await git.LoadHistoryAsync(new HistoryRequest(MaxCommits: max));
        Assert.Equal(expected, graph.Dag.Count);
    }

    [Fact]
    public async Task Graph_commits_have_parents()
    {
        using var repo = RepoWithCommits(3);
        using var git = await repo.OpenAsync();
        var dag = (await git.LoadHistoryAsync(new HistoryRequest())).Dag;
        Assert.Equal(1, dag.ParentsOf(0).Length);
        Assert.Equal(0, dag.ParentsOf(dag.Count - 1).Length);
    }

    [Fact]
    public async Task Graph_commits_have_refs()
    {
        using var repo = RepositoryTests.RepoWithCommit();
        repo.Raw("tag", "-a", "v1", "-m", "annotated");
        using var git = await repo.OpenAsync();
        var graph = await git.LoadHistoryAsync(new HistoryRequest());
        var refs = graph.RefsOf(graph.Dag.OidAt(0)).Select(r => r.ShortName).ToList();
        Assert.Contains(repo.CurrentBranch(), refs);
        Assert.Contains("v1", refs); // annotated tags are peeled to their commit
    }

    [Fact]
    public async Task Graph_layout_is_valid_with_head_in_lane_zero()
    {
        using var repo = RepositoryTests.RepoWithCommit();
        using var git = await repo.OpenAsync();
        var main = repo.CurrentBranch();
        await git.CreateBranchAsync("side");
        repo.CommitFile("s.txt", "s", "side");
        await git.SwitchBranchAsync(main);
        repo.CommitFile("m.txt", "m", "main");
        repo.Raw("merge", "-q", "--no-ff", "-m", "merge", "side");

        var graph = await git.LoadHistoryAsync(new HistoryRequest());
        var head = graph.Dag.IndexOf(Oid.ParseHex(repo.Head()));
        var layout = GraphLayout.Compute(graph.Dag, head);
        Assert.Null(LayoutValidator.Validate(graph.Dag, layout));
        Assert.Equal(0, layout.NodeLane(head));
    }

    [Fact]
    public async Task Graph_includes_detached_head_not_on_any_branch()
    {
        using var repo = RepositoryTests.RepoWithCommit();
        using var git = await repo.OpenAsync();
        repo.Raw("switch", "-q", "--detach");
        var detached = repo.CommitFile("d.txt", "d", "detached work");
        var graph = await git.LoadHistoryAsync(new HistoryRequest());
        Assert.True(graph.Dag.IndexOf(Oid.ParseHex(detached)) >= 0);
    }

    [Fact]
    public async Task Commit_stats_are_read_in_one_batch()
    {
        using var repo = RepositoryTests.RepoWithCommit();
        var second = repo.CommitFile("hello.txt", "hello\nworld\n", "two lines");
        repo.Raw("commit", "-q", "--allow-empty", "-m", "empty");
        var empty = repo.Head();
        using var git = await repo.OpenAsync();
        var first = (await git.ResolveAsync("HEAD~2"))!.Value;
        var log = new List<GitInvocation>();
        repo.Git.Invoked += log.Add;

        var stats = await git.CommitStatsAsync([first, Oid.ParseHex(second), Oid.ParseHex(empty)]);

        Assert.Single(log);
        Assert.Equal(new CommitStats(1, 0, 1), stats[first]);
        Assert.Equal(new CommitStats(2, 1, 1), stats[Oid.ParseHex(second)]);
        Assert.Equal(new CommitStats(0, 0, 0), stats[Oid.ParseHex(empty)]);
    }

    // ── Remotes ───────────────────────────────────────────────────────

    [Fact]
    public async Task List_remotes_prefers_origin_first()
    {
        using var bare = TempRepo.InitBare();
        using var clone = TempRepo.CloneOf(bare);
        clone.Raw("remote", "add", "abc-mirror", "https://example.com/mirror.git");
        clone.Raw("remote", "add", "dotted.name", "https://example.com/dotted.git");
        using var git = await clone.OpenAsync();
        var remotes = await git.ListRemotesAsync();
        Assert.Equal(["origin", "abc-mirror", "dotted.name"], remotes.Select(r => r.Name));
        Assert.Equal("https://example.com/dotted.git", remotes[2].Url);
    }

    [Fact]
    public async Task Remote_branch_status_returns_null_for_unpublished_branch()
    {
        var (origin, clone) = ClonedWithPushedCommit();
        using var _o = origin;
        using var _c = clone;
        using var mirror = TempRepo.InitBare();
        clone.Raw("remote", "add", "mirror", mirror.Path);
        using var git = await clone.OpenAsync();

        var originStatus = await git.RemoteBranchStatusAsync("origin");
        Assert.NotNull(originStatus);
        Assert.Equal(0, originStatus.Ahead);
        Assert.Null(await git.RemoteBranchStatusAsync("mirror"));
    }

    [Fact]
    public async Task Remote_branch_status_shows_ahead_count()
    {
        var (origin, clone) = ClonedWithPushedCommit();
        using var _o = origin;
        using var _c = clone;
        clone.Write("new.txt", "new");
        clone.CommitAll("local commit");
        using var git = await clone.OpenAsync();
        var status = await git.RemoteBranchStatusAsync("origin");
        Assert.Equal((1, 0), (status!.Ahead, status.Behind));
    }

    [Fact]
    public async Task Tracking_status_reads_upstream_ahead_behind()
    {
        var (origin, clone) = ClonedWithPushedCommit();
        using var _o = origin;
        using var _c = clone;
        using var git = await clone.OpenAsync();
        await git.CreateBranchAsync("topic");
        Assert.Null(await git.TrackingStatusAsync()); // a new branch has no upstream
        await git.PublishAsync("origin");
        clone.Write("n.txt", "n");
        clone.CommitAll("ahead");
        var tracking = await git.TrackingStatusAsync();
        Assert.Equal(new TrackingStatus(1, 0, "origin/" + clone.CurrentBranch()), tracking);
    }

    [Fact]
    public async Task Push_to_secondary_remote_works()
    {
        var (origin, clone) = ClonedWithPushedCommit();
        using var _o = origin;
        using var _c = clone;
        using var mirror = TempRepo.InitBare();
        clone.Raw("remote", "add", "mirror", mirror.Path);
        using var git = await clone.OpenAsync();
        await git.PushAsync("mirror");
        Assert.Contains("initial", mirror.Raw("log", "--format=%s", "-1"));
    }

    [Fact]
    public async Task Fetch_from_secondary_remote_works()
    {
        var (origin, clone) = ClonedWithPushedCommit();
        using var _o = origin;
        using var _c = clone;
        using var mirror = TempRepo.InitBare();
        clone.Raw("remote", "add", "mirror", mirror.Path);
        clone.Raw("push", "-q", "mirror", "HEAD");
        using (var other = TempRepo.CloneOf(mirror))
        {
            other.Write("new.txt", "new content");
            other.CommitAll("mirror-only commit");
            other.Raw("push", "-q");
        }
        using var git = await clone.OpenAsync();
        await git.FetchAsync("mirror");
        Assert.Contains("mirror-only commit", clone.Raw("log", "--format=%s", "refs/remotes/mirror/" + clone.CurrentBranch()));
    }

    [Fact]
    public async Task Pull_merges_the_upstream_branch()
    {
        var (origin, clone) = ClonedWithPushedCommit();
        using var _o = origin;
        using var _c = clone;
        using (var other = TempRepo.CloneOf(origin))
        {
            other.Write("other.txt", "from other");
            other.CommitAll("other commit");
            other.Raw("push", "-q");
        }
        using var git = await clone.OpenAsync();
        await git.PullAsync("origin");
        Assert.True(clone.Exists("other.txt"));
    }

    [Fact]
    public async Task Publish_sets_upstream_and_delete_info_sees_it()
    {
        var (origin, clone) = ClonedWithPushedCommit();
        using var _o = origin;
        using var _c = clone;
        using var git = await clone.OpenAsync();
        await git.CreateBranchAsync("topic");
        await git.PublishAsync("origin");
        Assert.Equal(new BranchDeleteInfo(true, "origin", "topic"), await git.BranchDeleteInfoAsync("topic"));

        await git.DeleteRemoteBranchAsync("origin", "topic");
        Assert.Equal(new BranchDeleteInfo(false, "origin", "topic"), await git.BranchDeleteInfoAsync("topic"));
    }

    [Fact]
    public async Task Push_when_the_remote_moved_fetches_and_counts_its_commits()
    {
        var (origin, clone) = ClonedWithPushedCommit();
        using var _o = origin;
        using var _c = clone;
        using var other = TempRepo.CloneOf(origin);
        other.CommitFile("theirs.txt", "1", "theirs 1");
        other.CommitFile("theirs.txt", "2", "theirs 2");
        other.Raw("push", "-q", "origin", "HEAD");
        clone.CommitFile("ours.txt", "ours", "ours");

        using var git = await clone.OpenAsync();
        Assert.Equal(2, await git.PushAsync("origin"));
        // Fetched: the tracking branch knows the remote's commits, so the branch is now behind.
        clone.Raw("merge-base", "--is-ancestor", other.Head(), "refs/remotes/origin/" + clone.Raw("branch", "--show-current").Trim());
        Assert.NotEqual(clone.Head(), origin.Raw("rev-parse", "HEAD").Trim());

        await git.PullAsync("origin");
        Assert.Equal(0, await git.PushAsync("origin"));
        Assert.Equal(clone.Head(), origin.Raw("rev-parse", "HEAD").Trim());
    }

    [Fact]
    public async Task Push_refused_by_the_remote_for_another_reason_throws()
    {
        var (origin, clone) = ClonedWithPushedCommit();
        using var _o = origin;
        using var _c = clone;
        var hook = Path.Combine(origin.Path, "hooks", "pre-receive");
        File.WriteAllText(hook, "#!/bin/sh\necho 'protected branch' >&2\nexit 1\n");
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(hook, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        clone.CommitFile("ours.txt", "ours", "ours");

        using var git = await clone.OpenAsync();
        await Assert.ThrowsAnyAsync<GitCommandException>(() => git.PushAsync("origin"));
    }

    [Fact]
    public async Task Push_while_browsing_history_is_refused()
    {
        var (origin, clone) = ClonedWithPushedCommit();
        using var _o = origin;
        using var _c = clone;
        clone.Raw("switch", "-q", "--detach");
        using var git = await clone.OpenAsync();
        var e = await Assert.ThrowsAsync<GitOperationException>(() => git.PushAsync("origin"));
        Assert.Equal(GitErrorCode.DetachedHead, e.Code);
    }

    [Fact]
    public async Task Local_only_uses_the_selected_remote()
    {
        var (origin, clone) = ClonedWithPushedCommit();
        using var _o = origin;
        using var _c = clone;
        using var mirror = TempRepo.InitBare();
        clone.Raw("remote", "add", "mirror", mirror.Path);
        clone.Raw("push", "-q", "mirror", "HEAD");
        clone.Write("new.txt", "new");
        clone.CommitAll("new commit");
        clone.Raw("push", "-q", "mirror", "HEAD");
        clone.Raw("fetch", "-q", "mirror");
        using var git = await clone.OpenAsync();

        var vsOrigin = await git.LoadHistoryAsync(new HistoryRequest(Remote: "origin"));
        Assert.Equal([Oid.ParseHex(clone.Head())], vsOrigin.LocalOnly);
        var vsMirror = await git.LoadHistoryAsync(new HistoryRequest(Remote: "mirror"));
        Assert.Empty(vsMirror.LocalOnly);
    }

    [Fact]
    public async Task Local_only_marks_branch_not_published_on_that_remote()
    {
        var (origin, clone) = ClonedWithPushedCommit();
        using var _o = origin;
        using var _c = clone;
        using var mirror = TempRepo.InitBare();
        clone.Raw("remote", "add", "mirror", mirror.Path);
        clone.Raw("push", "-q", "mirror", "HEAD");
        clone.Raw("fetch", "-q", "mirror");
        clone.Raw("switch", "-q", "-c", "feature-x");
        clone.Write("feature.txt", "feature");
        clone.CommitAll("feature commit");
        clone.Raw("push", "-q", "origin", "feature-x");
        clone.Raw("fetch", "-q", "origin");
        using var git = await clone.OpenAsync();

        Assert.Empty((await git.LoadHistoryAsync(new HistoryRequest(Remote: "origin"))).LocalOnly);
        Assert.Equal([Oid.ParseHex(clone.Head())], (await git.LoadHistoryAsync(new HistoryRequest(Remote: "mirror"))).LocalOnly);
    }

    [Fact]
    public async Task Local_only_includes_unpublished_branches()
    {
        var (origin, clone) = ClonedWithPushedCommit();
        using var _o = origin;
        using var _c = clone;
        clone.Raw("switch", "-q", "-c", "local-only-branch");
        clone.Write("local.txt", "local");
        clone.CommitAll("local commit");
        using var git = await clone.OpenAsync();
        var graph = await git.LoadHistoryAsync(new HistoryRequest(Remote: "origin"));
        Assert.True(graph.Dag.Count >= 2);
        Assert.Contains("refs/heads/local-only-branch", graph.Branches);
        Assert.Equal([Oid.ParseHex(clone.Head())], graph.LocalOnly);
    }

    /// <summary>
    /// A show-ref per branch made each load take 8-23 s with 70 branches on
    /// Windows (100-600 ms per git process). The count must not grow with branches.
    /// </summary>
    [Fact]
    public async Task Loading_history_runs_a_fixed_number_of_git_calls_however_many_branches()
    {
        var (origin, clone) = ClonedWithPushedCommit();
        using var _o = origin;
        using var _c = clone;
        var main = clone.CurrentBranch();
        for (var i = 0; i < 20; i++)
        {
            clone.Raw("switch", "-q", "-c", $"b{i}", main);
            clone.CommitFile($"b{i}.txt", "x", $"b{i}");
            // Half are published, with one more commit on the local side.
            if (i % 2 == 0)
            {
                clone.Raw("push", "-q", "origin", $"b{i}");
                clone.CommitFile($"b{i}.txt", "y", $"b{i} again");
            }
        }
        clone.Raw("switch", "-q", main);
        using var git = await clone.OpenAsync();
        var calls = 0;
        clone.Git.Invoked += _ => Interlocked.Increment(ref calls);

        var graph = await git.LoadHistoryAsync(new HistoryRequest(Remote: "origin"));

        Assert.InRange(calls, 1, 8);
        // Every branch tip is local-only: unpublished branches with their one
        // commit, published ones with the commit made after the push.
        var tips = Enumerable.Range(0, 20).Select(i => Oid.ParseHex(clone.Raw("rev-parse", $"b{i}").Trim())).ToHashSet();
        Assert.Equal(tips, graph.LocalOnly.ToHashSet());
    }

    [Fact]
    public async Task Remote_default_branch_is_added_to_the_graph()
    {
        var (origin, clone) = ClonedWithPushedCommit();
        using var _o = origin;
        using var _c = clone;
        var main = clone.CurrentBranch();
        // A clone of an empty repository has no origin/HEAD; set it as a normal clone would.
        clone.Raw("remote", "set-head", "origin", main);
        clone.Raw("switch", "-q", "-c", "topic");
        clone.Raw("branch", "-q", "-D", main);
        using var git = await clone.OpenAsync();
        var graph = await git.LoadHistoryAsync(new HistoryRequest(["topic"], Remote: "origin"));
        Assert.Contains($"refs/remotes/origin/{main}", graph.Branches);
    }

    [Fact]
    public async Task Fetch_prunes_stale_remote_refs()
    {
        var (origin, clone) = ClonedWithPushedCommit();
        using var _o = origin;
        using var _c = clone;
        var main = clone.CurrentBranch();
        clone.Raw("switch", "-q", "-c", "feature-branch");
        clone.Write("feature.txt", "feature");
        clone.CommitAll("feature commit");
        clone.Raw("push", "-q", "origin", "feature-branch");
        Assert.Equal(0, clone.TryRaw("rev-parse", "--verify", "refs/remotes/origin/feature-branch").ExitCode);
        origin.Raw("branch", "-D", "feature-branch");
        clone.Raw("switch", "-q", main);

        using var git = await clone.OpenAsync();
        await git.FetchAsync("origin");
        Assert.NotEqual(0, clone.TryRaw("rev-parse", "--verify", "refs/remotes/origin/feature-branch").ExitCode);
    }

    /// <summary>
    /// On a case-insensitive file system, pruning <c>origin/Release/x</c> and <c>origin/release/x</c>
    /// in one transaction fails ("cannot lock ref … File exists"), deletes nothing, and every later
    /// fetch fails the same way. A reference-transaction hook rejects such transactions, so the
    /// failure shows on any file system.
    /// </summary>
    [Fact]
    public async Task Fetch_prunes_refs_that_differ_only_in_case()
    {
        var (origin, clone) = ClonedWithPushedCommit();
        using var _o = origin;
        using var _c = clone;
        var head = clone.Head();
        // Packed between updates, so the names never need two loose files (macOS and Windows runners).
        foreach (var name in new[] { "Release/x", "release/x", "Release/y", "keep" })
        {
            origin.Raw("update-ref", "refs/heads/" + name, head);
            origin.Raw("pack-refs", "--all");
        }
        clone.Raw("fetch", "-q", "origin");
        clone.Raw("pack-refs", "--all");
        foreach (var name in new[] { "Release/x", "release/x", "Release/y" })
        {
            origin.Raw("update-ref", "-d", "refs/heads/" + name);
        }
        origin.Raw("update-ref", "refs/heads/Feature/new", head);

        var hooks = clone.FullPath("case-hooks");
        Directory.CreateDirectory(hooks);
        var hook = Path.Combine(hooks, "reference-transaction");
        File.WriteAllText(hook, """
            #!/bin/sh
            [ "$1" = prepared ] || exit 0
            dups=$(cut -d' ' -f3 | tr 'A-Z' 'a-z' | sort | uniq -d)
            [ -z "$dups" ] && exit 0
            echo "cannot lock ref: case collision $dups" >&2
            exit 1
            """.Replace("\r\n", "\n"));
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(hook, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        clone.Raw("config", "core.hooksPath", hooks);
        clone.Raw("config", "core.ignorecase", "true");
        Assert.NotEqual(0, clone.TryRaw("fetch", "--prune", "origin").ExitCode);

        using var git = await clone.OpenAsync();
        await git.FetchAsync("origin");
        var refs = clone.Raw("for-each-ref", "--format=%(refname)", "refs/remotes/origin/").Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.DoesNotContain(refs, r => r.Contains("Release/", StringComparison.OrdinalIgnoreCase));
        Assert.Contains("refs/remotes/origin/keep", refs);
        Assert.Contains("refs/remotes/origin/Feature/new", refs);
    }

    [Fact]
    public void Prune_rounds_never_put_names_differing_only_in_case_together()
    {
        var porcelain = "- a 0 refs/remotes/o/Release/x\n- a 0 refs/remotes/o/release/x\n* 0 b refs/remotes/o/new\n- a 0 refs/remotes/o/RELEASE/X\n- a 0 refs/remotes/o/y\n"u8;
        var rounds = GitRepository.PruneRounds(GitRepository.PrunedRefs(porcelain))
            .Select(round => round.Select(n => System.Text.Encoding.UTF8.GetString(n)).ToArray()).ToArray();
        Assert.Equal(
            [["refs/remotes/o/Release/x", "refs/remotes/o/y"], ["refs/remotes/o/release/x"], ["refs/remotes/o/RELEASE/X"]],
            rounds);
    }

    [Fact]
    public async Task Remote_branch_status_detects_unpublished_after_prune()
    {
        var (origin, clone) = ClonedWithPushedCommit();
        using var _o = origin;
        using var _c = clone;
        clone.Raw("switch", "-q", "-c", "my-feature");
        clone.Write("feature.txt", "feature");
        clone.CommitAll("feature commit");
        clone.Raw("push", "-q", "origin", "my-feature");
        using var git = await clone.OpenAsync();
        Assert.NotNull(await git.RemoteBranchStatusAsync("origin"));

        origin.Raw("branch", "-D", "my-feature");
        await git.FetchAsync("origin");
        Assert.Null(await git.RemoteBranchStatusAsync("origin"));
    }
}
