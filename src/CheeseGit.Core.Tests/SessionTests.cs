using CheeseGit.Core.Git;
using CheeseGit.Core.Repository;

namespace CheeseGit.Core.Tests;

/// <summary>RepositorySession: snapshot publication, the operation queue and file system invalidation.</summary>
public class SessionTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(15);

    private static Task<RepositorySession> OpenSession(TempRepo repo, SessionOptions? options = null) =>
        RepositorySession.OpenAsync(repo.Path, options ?? new SessionOptions { Debounce = TimeSpan.FromMilliseconds(50) }, repo.Git);

    /// <summary>Waits until a published snapshot (or the current one) satisfies <paramref name="condition"/>.</summary>
    private static async Task<RepoSnapshot> WaitFor(RepositorySession session, Func<RepoSnapshot, bool> condition)
    {
        var done = new TaskCompletionSource<RepoSnapshot>(TaskCreationOptions.RunContinuationsAsynchronously);
        void Handler(SnapshotChange change)
        {
            if (condition(change.Snapshot)) done.TrySetResult(change.Snapshot);
        }
        session.SnapshotChanged += Handler;
        try
        {
            if (condition(session.Snapshot)) return session.Snapshot;
            return await done.Task.WaitAsync(Timeout);
        }
        finally
        {
            session.SnapshotChanged -= Handler;
        }
    }

    private static bool HasEntry(RepoSnapshot s, string path) => s.Status.Entries.Any(e => e.Path == path);

    // ── Snapshot and queue ────────────────────────────────────────────

    [Fact]
    public async Task Open_reads_a_full_snapshot()
    {
        using var repo = RepositoryTests.RepoWithCommit();
        repo.Write("new.txt", "x");
        using var session = await OpenSession(repo);
        var s = session.Snapshot;
        Assert.Equal(repo.CurrentBranch(), s.Head.Branch);
        Assert.True(HasEntry(s, "new.txt"));
        Assert.Contains(s.Refs, r => r.FullName == "refs/heads/" + repo.CurrentBranch());
        Assert.Equal(OperationState.None, s.Operation);
        Assert.Empty(s.Stashes);
        Assert.Empty(s.Remotes);
        Assert.True(session.IsWatching);
    }

    [Fact]
    public async Task A_mutation_publishes_one_change_with_the_parts_that_changed()
    {
        using var repo = RepositoryTests.RepoWithCommit();
        repo.Write("new.txt", "x");
        using var session = await OpenSession(repo, new SessionOptions { Watch = false });
        var changes = new List<SnapshotChange>();
        session.SnapshotChanged += changes.Add;

        await session.RunAsync("Stage", (git, ct) => git.StageAsync(["new.txt"], ct));

        var change = Assert.Single(changes);
        Assert.Equal(SnapshotParts.Status, change.Changed);
        Assert.Equal(1, change.Snapshot.Version);
        Assert.Same(change.Snapshot, session.Snapshot);
        Assert.Contains(session.Snapshot.Status.Staged, c => c.Path == "new.txt");
    }

    [Fact]
    public async Task A_refresh_that_reads_the_same_state_publishes_nothing()
    {
        using var repo = RepositoryTests.RepoWithCommit();
        repo.Write("new.txt", "x");
        using var session = await OpenSession(repo, new SessionOptions { Watch = false });
        var before = session.Snapshot;
        var changes = 0;
        session.SnapshotChanged += _ => changes++;

        await session.RefreshAsync();

        Assert.Equal(0, changes);
        Assert.Same(before, session.Snapshot);
    }

    [Fact]
    public async Task Refreshed_reports_every_refresh_even_when_nothing_changed()
    {
        using var repo = RepositoryTests.RepoWithCommit();
        repo.Write("hello.txt", "first edit\n");
        using var session = await OpenSession(repo, new SessionOptions { Watch = false });
        var refreshed = new List<SnapshotParts>();
        session.Refreshed += refreshed.Add;

        // A second edit keeps the file "modified": the snapshot stays the same, the diff does not.
        repo.Write("hello.txt", "second edit\n");
        await session.RefreshAsync(SnapshotParts.Status);

        Assert.Equal([SnapshotParts.Status], refreshed);
    }

    [Fact]
    public async Task A_failed_mutation_reports_its_operation_as_failed_but_a_non_zero_exit_alone_does_not()
    {
        using var repo = RepositoryTests.RepoWithCommit();
        using var session = await OpenSession(repo, new SessionOptions { Watch = false });
        var ours = new HashSet<long>();
        var failed = new List<(GitOperation Operation, string Message)>();
        repo.Git.Invoked += call =>
        {
            if (call.Operation is { } op) lock (ours) ours.Add(op.Id);
        };
        // The event is static and tests run in parallel: keep only this session's operations.
        Action<GitOperation, string> handler = (op, message) =>
        {
            lock (ours) if (ours.Contains(op.Id)) lock (failed) failed.Add((op, message));
        };
        GitRunner.OperationFailed += handler;
        try
        {
            // Exit 1 is the answer, not an error.
            await session.RunAsync("Probe", async (git, ct) =>
                (await git.Git.RunAsync(git.Root, ["rev-parse", "--verify", "--quiet", "refs/heads/nope"], ct: ct)).Args);
            await Assert.ThrowsAsync<GitCommandException>(() => session.RunAsync("Checkout", (git, ct) =>
                git.Git.RunCheckedAsync("Checkout", git.Root, ["checkout", "--end-of-options", "refs/heads/nope"], GitAccess.Write, ct: ct)));
        }
        finally
        {
            GitRunner.OperationFailed -= handler;
        }

        var only = Assert.Single(failed);
        Assert.Equal("Checkout", only.Operation.Label);
        Assert.NotEmpty(only.Message);
    }

    [Fact]
    public async Task Mutations_run_one_at_a_time_in_order()
    {
        using var repo = RepositoryTests.RepoWithCommit();
        using var session = await OpenSession(repo, new SessionOptions { Watch = false });
        var active = 0;
        var maxActive = 0;
        var order = new List<int>();

        var tasks = Enumerable.Range(0, 8).Select(i => session.RunAsync($"op {i}", async (git, ct) =>
        {
            var now = Interlocked.Increment(ref active);
            InterlockedMax(ref maxActive, now);
            lock (order) order.Add(i);
            await Task.Delay(20, ct);
            Interlocked.Decrement(ref active);
        }, SnapshotParts.None)).ToList();
        await Task.WhenAll(tasks);

        Assert.Equal(1, maxActive);
        Assert.Equal(8, order.Count);
    }

    /// <summary>
    /// A fetch once waited 112 s behind a background git status. The status
    /// is made slow with an fsmonitor hook that sleeps once, until released.
    /// </summary>
    [Fact]
    public async Task A_mutation_stops_a_slow_background_refresh_and_refreshes_afterwards()
    {
        using var repo = RepositoryTests.RepoWithCommit();
        var gitDir = repo.FullPath(".git").Replace('\\', '/');
        var hook = Path.Combine(gitDir, "slow-fsmonitor");
        File.WriteAllText(hook, $"""
            #!/bin/sh
            dir='{gitDir}'
            if [ -e "$dir/slow" ]; then
              rm -f "$dir/slow"
              : > "$dir/started"
              i=0
              while [ ! -e "$dir/release" ] && [ $i -lt 600 ]; do sleep 0.1; i=$((i+1)); done
            fi
            exit 1
            """.ReplaceLineEndings("\n"));
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(hook, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        repo.Raw("config", "core.fsmonitor", hook.Replace('\\', '/'));
        using var session = await OpenSession(repo, new SessionOptions { Watch = false, PollInterval = TimeSpan.FromHours(1) });
        try
        {
            File.WriteAllText(Path.Combine(gitDir, "slow"), "");
            repo.Write("new.txt", "x");
            session.Invalidate(SnapshotParts.Status);
            var deadline = DateTime.UtcNow + Timeout;
            while (!File.Exists(Path.Combine(gitDir, "started")))
            {
                Assert.True(DateTime.UtcNow < deadline, "the background status never reached the hook");
                await Task.Delay(20);
            }

            var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var mutation = session.RunAsync("op", (_, _) =>
            {
                started.TrySetResult();
                return Task.CompletedTask;
            }, SnapshotParts.None);

            // The hook would hold the refresh for 60 s.
            await started.Task.WaitAsync(Timeout);
            await mutation.WaitAsync(Timeout);
            Assert.True(HasEntry(session.Snapshot, "new.txt"));
        }
        finally
        {
            File.WriteAllText(Path.Combine(gitDir, "release"), "");
        }
    }

    private static void InterlockedMax(ref int target, int value)
    {
        int current;
        while ((current = Volatile.Read(ref target)) < value && Interlocked.CompareExchange(ref target, value, current) != current) { }
    }

    [Fact]
    public async Task A_failing_mutation_still_refreshes_and_rethrows()
    {
        using var repo = RepositoryTests.RepoWithCommit();
        repo.Write("new.txt", "x");
        using var session = await OpenSession(repo, new SessionOptions { Watch = false });

        await Assert.ThrowsAsync<InvalidOperationException>(() => session.RunAsync("Stage then fail", async (git, ct) =>
        {
            await git.StageAsync(["new.txt"], ct);
            throw new InvalidOperationException("boom");
        }));

        Assert.Contains(session.Snapshot.Status.Staged, c => c.Path == "new.txt");
        Assert.Null(session.CurrentOperation);
    }

    [Fact]
    public async Task Current_operation_is_reported_while_a_mutation_runs()
    {
        using var repo = RepositoryTests.RepoWithCommit();
        using var session = await OpenSession(repo, new SessionOptions { Watch = false });
        var seen = new List<string?>();
        session.CurrentOperationChanged += seen.Add;
        string? inside = null;
        await session.RunAsync("Commit", (git, ct) =>
        {
            inside = session.CurrentOperation;
            return Task.CompletedTask;
        }, SnapshotParts.None);
        Assert.Equal("Commit", inside);
        Assert.Equal(["Commit", null], seen);
    }

    [Fact]
    public async Task Git_calls_of_a_mutation_carry_its_label()
    {
        using var repo = RepositoryTests.RepoWithCommit();
        repo.Write("new.txt", "x");
        using var session = await OpenSession(repo, new SessionOptions { Watch = false });
        var log = new List<GitInvocation>();
        repo.Git.Invoked += e => { lock (log) log.Add(e); };
        await session.RunAsync("Stage new.txt", (git, ct) => git.StageAsync(["new.txt"], ct));
        lock (log)
        {
            Assert.Contains(log, e => e.Operation?.Label == "Stage new.txt" && e.Args.Contains("add"));
            Assert.Contains(log, e => e.Operation?.Label == "Refresh" && e.Operation.Background && e.Args.Contains("status"));
        }
    }

    [Fact]
    public async Task Polling_is_the_fallback_when_not_watching()
    {
        using var repo = RepositoryTests.RepoWithCommit();
        using var session = await OpenSession(repo, new SessionOptions { Watch = false, PollInterval = TimeSpan.FromMilliseconds(100) });
        Assert.False(session.IsWatching);
        repo.Write("polled.txt", "x");
        await WaitFor(session, s => HasEntry(s, "polled.txt"));
    }

    // ── Watching ──────────────────────────────────────────────────────

    [Fact]
    public async Task An_external_worktree_edit_is_picked_up()
    {
        using var repo = RepositoryTests.RepoWithCommit();
        using var session = await OpenSession(repo);
        repo.Write("dir/external.txt", "x");
        await WaitFor(session, s => HasEntry(s, "dir/external.txt"));
        repo.Delete("dir/external.txt");
        await WaitFor(session, s => !HasEntry(s, "dir/external.txt"));
    }

    [Fact]
    public async Task An_external_commit_updates_head_and_refs()
    {
        using var repo = RepositoryTests.RepoWithCommit();
        using var session = await OpenSession(repo);
        var head = repo.CommitFile("other.txt", "x", "external commit");
        var s = await WaitFor(session, s => s.Head.Commit?.ToHex() == head);
        Assert.Contains(s.Refs, r => r.Target.ToHex() == head);
        await WaitFor(session, s => s.Status.Entries.Count == 0);
    }

    [Fact]
    public async Task An_external_branch_switch_and_new_branch_are_picked_up()
    {
        using var repo = RepositoryTests.RepoWithCommit();
        using var session = await OpenSession(repo);
        repo.Raw("switch", "-q", "-c", "elsewhere");
        await WaitFor(session, s => s.Head.Branch == "elsewhere" && s.Refs.Any(r => r.FullName == "refs/heads/elsewhere"));
    }

    [Fact]
    public async Task Packed_refs_changes_are_picked_up()
    {
        using var repo = RepositoryTests.RepoWithCommit();
        repo.Raw("branch", "packed");
        using var session = await OpenSession(repo);
        repo.Raw("pack-refs", "--all");
        repo.Raw("branch", "-D", "packed");
        await WaitFor(session, s => s.Refs.All(r => r.FullName != "refs/heads/packed"));
    }

    [Fact]
    public async Task An_external_merge_conflict_shows_up_as_operation_state()
    {
        using var repo = TempRepo.Init();
        repo.CommitFile("file.txt", "base\n", "initial");
        repo.Raw("switch", "-q", "-c", "feature");
        repo.CommitFile("file.txt", "theirs\n", "feature");
        repo.Raw("switch", "-q", "-");
        repo.CommitFile("file.txt", "ours\n", "main");
        using var session = await OpenSession(repo);

        Assert.NotEqual(0, repo.TryRaw("merge", "feature").ExitCode);
        var s = await WaitFor(session, s => s.Operation.Kind == OperationKind.Merge);
        Assert.Equal("feature", s.Operation.Incoming);

        repo.Raw("merge", "--abort");
        await WaitFor(session, s => s.Operation == OperationState.None && s.Status.Entries.Count == 0);
    }

    [Fact]
    public async Task External_stash_changes_are_picked_up_including_dropping_an_older_entry()
    {
        using var repo = RepositoryTests.RepoWithCommit();
        repo.Write("hello.txt", "one");
        repo.Raw("stash", "push", "-q", "-m", "one");
        using var session = await OpenSession(repo);

        repo.Write("hello.txt", "two");
        repo.Raw("stash", "push", "-q", "-m", "two");
        await WaitFor(session, s => s.Stashes.Count == 2);

        // Only logs/refs/stash changes here; refs/stash still names "two".
        repo.Raw("stash", "drop", "-q", "stash@{1}");
        var s = await WaitFor(session, s => s.Stashes.Count == 1);
        Assert.Contains("two", s.Stashes[0].Message);
    }

    [Fact]
    public async Task External_remote_configuration_is_picked_up()
    {
        using var repo = RepositoryTests.RepoWithCommit();
        using var session = await OpenSession(repo);
        repo.Raw("remote", "add", "upstream", "https://example.com/x.git");
        await WaitFor(session, s => s.Remotes.Any(r => r.Name == "upstream"));
    }

    [Fact]
    public async Task Events_in_ignored_directories_do_not_refresh_status()
    {
        using var repo = RepositoryTests.RepoWithCommit();
        repo.CommitFile(".gitignore", "build/\n*.o\n", "ignore");
        Directory.CreateDirectory(repo.FullPath("build"));
        using var session = await OpenSession(repo);
        var batches = new List<(int Raw, int Kept)>();
        var settled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        session.Watcher!.WorktreeBatchProcessed += (raw, kept) =>
        {
            lock (batches) batches.Add((raw, kept));
            settled.TrySetResult();
        };

        for (var i = 0; i < 50; i++) repo.Write($"build/out/deep/{i}.bin", "x");
        repo.Write("src.o", "x");
        await settled.Task.WaitAsync(Timeout);
        // Give events that straddled the first batch time to arrive.
        await Task.Delay(300);

        lock (batches)
        {
            Assert.NotEmpty(batches);
            Assert.All(batches, b => Assert.Equal(0, b.Kept));
        }
        Assert.Empty(session.Snapshot.Status.Entries);
    }

    [Fact]
    public async Task A_tracked_file_in_an_ignored_directory_still_counts()
    {
        using var repo = RepositoryTests.RepoWithCommit();
        repo.CommitFile(".gitignore", "vendor/\n", "ignore");
        repo.Write("vendor/kept.txt", "v1");
        repo.Raw("add", "-f", "vendor/kept.txt");
        repo.Raw("commit", "-q", "-m", "force-add");
        using var session = await OpenSession(repo);

        repo.Write("vendor/kept.txt", "v2");
        await WaitFor(session, s => HasEntry(s, "vendor/kept.txt"));
    }

    [Fact]
    public async Task Changing_gitignore_takes_effect_for_later_events()
    {
        using var repo = RepositoryTests.RepoWithCommit();
        repo.CommitFile(".gitignore", "logs/\n", "ignore");
        using var session = await OpenSession(repo);
        // Wait until the ignore result is cached, then un-ignore the directory. Subscribe before writing: with a
        // 50 ms debounce the batch could be processed before a subscription made after the write (it timed out on CI).
        var batch = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        session.Watcher!.WorktreeBatchProcessed += (raw, kept) =>
        {
            if (raw > 0 && kept == 0) batch.TrySetResult();
        };
        repo.Write("logs/a.log", "x");
        await batch.Task.WaitAsync(Timeout);

        repo.Write(".gitignore", "");
        repo.Write("logs/b.log", "x");
        await WaitFor(session, s => HasEntry(s, "logs/b.log"));
    }

    [Fact]
    public async Task Running_git_does_not_feed_back_into_refreshes()
    {
        // The old app's watcher fired on .git/index writes caused by its own
        // refreshes, which refreshed again: a self-sustaining loop.
        using var repo = RepositoryTests.RepoWithCommit();
        repo.Write("new.txt", "x");
        using var session = await OpenSession(repo);
        await session.RunAsync("Stage", (git, ct) => git.StageAsync(["new.txt"], ct));
        await Task.Delay(800);

        var calls = 0;
        repo.Git.Invoked += _ => Interlocked.Increment(ref calls);
        await Task.Delay(1000);
        Assert.Equal(0, Volatile.Read(ref calls));
    }

    [Fact]
    public async Task A_linked_worktree_sees_ref_changes_made_from_the_main_worktree()
    {
        using var repo = RepositoryTests.RepoWithCommit();
        using var linked = TempRepo.EmptyDirectory();
        Directory.Delete(linked.Path);
        repo.Raw("worktree", "add", "-q", "-b", "linked", linked.Path);
        using var session = await OpenSession(linked);
        Assert.Equal("linked", session.Snapshot.Head.Branch);

        repo.Raw("branch", "from-main");
        await WaitFor(session, s => s.Refs.Any(r => r.FullName == "refs/heads/from-main"));
        linked.Write("wt.txt", "x");
        await WaitFor(session, s => HasEntry(s, "wt.txt"));
        Assert.False(HasEntry(session.Snapshot, ".git"));
    }

    [Fact]
    public async Task Check_ignored_reports_ignored_files_and_directories_literally()
    {
        using var repo = RepositoryTests.RepoWithCommit();
        repo.CommitFile(".gitignore", "build/\nx.o\n", "ignore");
        repo.Write("build/a.txt", "x");
        using var git = await repo.OpenAsync();
        var ignored = await git.CheckIgnoredAsync(["build/", "build/a.txt", "x.o", "*.o", ":(top)x.o", "hello.txt"]);
        Assert.Equal(["build/", "build/a.txt", "x.o"], ignored.Select(p => p.ToString()).Order(StringComparer.Ordinal));
    }

    [Theory]
    [InlineData("HEAD", SnapshotParts.Head | SnapshotParts.Status)]
    [InlineData("index", SnapshotParts.Status | SnapshotParts.Operation)]
    [InlineData("MERGE_HEAD", SnapshotParts.Operation | SnapshotParts.Status)]
    [InlineData("rebase-merge", SnapshotParts.Operation | SnapshotParts.Status)]
    [InlineData("index.lock", SnapshotParts.None)]
    [InlineData("HEAD.lock", SnapshotParts.None)]
    [InlineData("FETCH_HEAD", SnapshotParts.None)]
    [InlineData("ORIG_HEAD", SnapshotParts.None)]
    [InlineData("objects", SnapshotParts.None)]
    public void Git_dir_entries_map_to_snapshot_parts(string name, SnapshotParts expected) =>
        Assert.Equal(expected, RepositoryWatcher.ClassifyGitDirEntry(name));
}
