using System.Diagnostics;
using System.Text;
using CheeseGit.Core.Diff;
using CheeseGit.Core.Git;
using CheeseGit.Core.Graph;
using CheeseGit.Core.Repository;
using CheeseGit.Fixtures;
using Xunit.Abstractions;

namespace CheeseGit.Core.Tests;

/// <summary>
/// Runs the same engine invariants against every generated fixture, under
/// whichever config profile is active. Each check compares two independent
/// readings of the same state (status vs. diff, numstat vs. parsed patch,
/// rev-list vs. the loaded DAG) so a parser bug shows up as a disagreement.
/// </summary>
public class FixtureTests(ITestOutputHelper output)
{
    public static TheoryData<FixtureKind> Kinds => [.. Enum.GetValues<FixtureKind>()];

    private static readonly FixtureOptions SmallOptions = new() { LargeHistoryCommits = 3_000, LargeWorktreeFiles = 2_000 };

    [Theory]
    [MemberData(nameof(Kinds))]
    public async Task Engine_invariants_hold(FixtureKind kind)
    {
        using var dir = TempRepo.EmptyDirectory();
        var fixture = await FixtureGenerator.CreateAsync(kind, dir.Path, SmallOptions);
        using var session = await RepositorySession.OpenAsync(fixture.Path, new SessionOptions { Watch = false }, dir.Git);

        await StatusAgreesWithDiffs(session);
        await HistoryLoadsAndLaysOut(session);
        await CommitDiffsAgreeWithNumstat(session, maxCommits: 150);
        await RefsAreComplete(session);
        if (session.Snapshot.Operation.Kind == OperationKind.None) await StageAndUnstageRoundTrip(session);
    }

    [Fact]
    public async Task Hostile_paths_fixture_has_every_change_kind()
    {
        using var dir = TempRepo.EmptyDirectory();
        var fixture = await FixtureGenerator.CreateAsync(FixtureKind.HostilePaths, dir.Path);
        using var git = await GitRepository.OpenAsync(fixture.Path, dir.Git);
        var status = await git.StatusAsync();

        var paths = status.Entries.Select(e => e.Path).ToHashSet();
        foreach (var name in HostileNames.ForThisOs().Take(4)) Assert.Contains(GitPath.FromBytes([.. "untracked/"u8, .. name]), paths);
        Assert.Contains(status.Entries, e => e.IndexState == 'A' && e.WorktreeState == 'D');
        Assert.Contains(status.Entries, e => e.IndexState == 'M' && e.WorktreeState == 'M');
        Assert.Contains(status.Unstaged, c => c.Status == FileStatus.Deleted);
        if (!TestEnvironment.Has("status.renames=false")) Assert.Contains(status.Staged, c => c.Status == FileStatus.Renamed);
        if (OperatingSystem.IsLinux()) Assert.Contains(status.Entries, e => e.Path.Bytes.IndexOf((byte)0xFF) >= 0);
    }

    [Fact]
    public async Task Paths_that_are_not_utf8_diff_and_read_byte_exact()
    {
        if (!OperatingSystem.IsLinux()) return;
        using var dir = TempRepo.EmptyDirectory();
        var fixture = await FixtureGenerator.CreateAsync(FixtureKind.HostilePaths, dir.Path);
        using var git = await GitRepository.OpenAsync(fixture.Path, dir.Git);
        var path = (await git.StatusAsync()).Entries.Single(e => e.Path.Bytes.IndexOf((byte)0xFF) >= 0).Path;
        Assert.False(path.ToPathspec().Literal);

        var diff = await git.DiffFileAsync(path, DiffArea.Unstaged);
        Assert.Equal(path, diff.Path);
        Assert.Equal(["changed"], diff.Hunks.SelectMany(h => h.Lines).Where(l => l.Kind == DiffLineKind.Addition).Select(l => l.Text));
        Assert.Equal("v1\nv2\n", Encoding.UTF8.GetString((await git.ShowFileAsync("HEAD", path))!));
        Assert.Equal(path, (await git.DiffCommitFileAsync((await git.ResolveAsync("HEAD"))!.Value, path)).Path);

        await git.StageAsync([path]);
        Assert.Equal(path, (await git.DiffFileAsync(path, DiffArea.Staged)).Path);
        Assert.True(await git.IsTrackedAsync(path));
    }

    [Fact]
    public void Pathspecs_for_paths_that_are_not_utf8_escape_glob_characters()
    {
        Assert.Equal(("plain [x].txt", true), GitPath.FromString("plain [x].txt").ToPathspec());
        Assert.Equal((":(glob)a\\*b??\\[c\\?\\\\.txt", false), GitPath.FromBytes([.. "a*b"u8, 0xFF, 0xFE, .. "[c?\\.txt"u8]).ToPathspec());
    }

    [Fact]
    public async Task Paused_merge_fixture_reports_every_conflict()
    {
        using var dir = TempRepo.EmptyDirectory();
        var fixture = await FixtureGenerator.CreateAsync(FixtureKind.PausedMerge, dir.Path);
        using var git = await GitRepository.OpenAsync(fixture.Path, dir.Git);
        var state = await git.OperationStateAsync();
        Assert.Equal(OperationKind.Merge, state.Kind);
        Assert.Equal("theirs", state.Incoming);
        Assert.Equal(
            ["add-add.txt", "content.txt", "crlf.txt", "modify-delete.txt"],
            (await git.ConflictedFilesAsync()).Select(p => p.ToString()));

        foreach (var path in await git.ConflictedFilesAsync()) await git.ResolveConflictAsync(path, ConflictResolution.AcceptBoth);
        Assert.Empty(await git.ConflictedFilesAsync());
        await git.MergeContinueAsync("merge theirs");
        Assert.Equal(OperationState.None, await git.OperationStateAsync());
    }

    [Fact]
    public async Task Fixtures_are_deterministic()
    {
        using var a = TempRepo.EmptyDirectory();
        using var b = TempRepo.EmptyDirectory();
        var fa = await FixtureGenerator.CreateAsync(FixtureKind.MergeShapes, a.Path);
        var fb = await FixtureGenerator.CreateAsync(FixtureKind.MergeShapes, b.Path);
        string Refs(TempRepo r, Fixture f) => r.Git.RunCheckedAsync("refs", f.Path, ["for-each-ref", "--format=%(refname) %(objectname)"]).Result.StdoutText;
        Assert.Equal(Refs(a, fa), Refs(b, fb));
    }

    [Fact]
    public async Task Large_history_of_100k_commits_loads_and_lays_out()
    {
        TestEnvironment.EnsureInitialized();
        var sw = Stopwatch.StartNew();
        var fixture = await FixtureCache.GetReadOnlyAsync(FixtureKind.LargeHistory, new FixtureOptions());
        var generate = sw.Elapsed;

        using var git = await GitRepository.OpenAsync(fixture.Path);
        sw.Restart();
        var history = await git.LoadHistoryAsync(new HistoryRequest());
        var load = sw.Elapsed;
        sw.Restart();
        var mainTip = history.Dag.IndexOf((await git.ResolveAsync("HEAD"))!.Value);
        var layout = GraphLayout.Compute(history.Dag, mainTip);
        var compute = sw.Elapsed;

        Assert.Equal(100_000, history.Dag.Count);
        Assert.Null(LayoutValidator.Validate(history.Dag, layout));
        output.WriteLine($"generate or reuse {generate.TotalMilliseconds:F0} ms, load {load.TotalMilliseconds:F0} ms, layout {compute.TotalMilliseconds:F0} ms, {layout.LaneCount} lanes");
    }

    // ── Invariants ────────────────────────────────────────────────────

    private static async Task StatusAgreesWithDiffs(RepositorySession session)
    {
        var git = session.Repository;
        var status = session.Snapshot.Status;
        Assert.Equal(status.Entries.Count, (await git.StatusAsync()).Entries.Count);

        foreach (var (area, changes) in new[] { (DiffArea.Staged, status.Staged), (DiffArea.Unstaged, status.Unstaged) })
        {
            var stats = (await git.DiffStatsAsync(area)).ToDictionary(s => s.Path);
            foreach (var change in changes)
            {
                var entry = status.Entries.First(e => e.Path == change.Path);
                if (entry.IsSubmodule || change.Status == FileStatus.Conflicted) continue;

                var diff = await git.DiffFileAsync(change.Path, area);
                Assert.True(diff.Path == change.Path, $"{area} diff of '{change.Path}' is for '{diff.Path}'");
                if (diff.IsBinary || !stats.TryGetValue(change.Path, out var stat) || stat.IsBinary) continue;
                Assert.True(
                    (stat.Additions, stat.Deletions) == (diff.Additions, diff.Deletions),
                    $"{area} '{change.Path}': numstat +{stat.Additions}/-{stat.Deletions}, parsed +{diff.Additions}/-{diff.Deletions}");
            }
        }
    }

    private static async Task HistoryLoadsAndLaysOut(RepositorySession session)
    {
        var git = session.Repository;
        var history = await git.LoadHistoryAsync(new HistoryRequest());
        var expected = await git.Git.RunCheckedAsync("count", git.Root, ["rev-list", "--count", "--branches", "HEAD", "--"]);
        Assert.Equal(int.Parse(expected.StdoutText.Trim()), history.Dag.Count);

        var head = session.Snapshot.Head.Commit!.Value;
        var layout = GraphLayout.Compute(history.Dag, history.Dag.IndexOf(head));
        Assert.Null(LayoutValidator.Validate(history.Dag, layout));

        // Hiding branches: only the required ones, then every other one alone.
        var main = session.Snapshot.Head.Branch is { } b ? "refs/heads/" + b : null;
        var required = HistoryView.RequiredBranches(history, main);
        foreach (var visible in history.Branches.Select(x => (IReadOnlyList<string>)[x]).Prepend([]))
        {
            var view = HistoryView.Create(history, [.. required, .. visible], main, head);
            Assert.Null(LayoutValidator.Validate(view.Dag, view.Layout));
            var count = await git.Git.RunCheckedAsync("count", git.Root, ["rev-list", "--count", .. view.VisibleBranches, "HEAD", "--"]);
            Assert.Equal(int.Parse(count.StdoutText.Trim()), view.Count);
        }
    }

    private static async Task CommitDiffsAgreeWithNumstat(RepositorySession session, int maxCommits)
    {
        var git = session.Repository;
        var history = await git.LoadHistoryAsync(new HistoryRequest());
        for (var row = 0; row < Math.Min(maxCommits, history.Dag.Count); row++)
        {
            var oid = history.Dag.OidAt(row);
            Assert.NotNull(git.GetCommit(oid));
            var diffs = await git.DiffCommitAsync(oid);
            var stats = await git.CommitFileStatsAsync(oid);
            Assert.Equal(stats.Select(s => s.Path).Order(), diffs.Select(d => d.Path).Order());
            foreach (var diff in diffs)
            {
                var stat = stats.First(s => s.Path == diff.Path);
                Assert.Equal(stat.IsBinary, diff.IsBinary);
                if (diff.IsBinary) continue;
                Assert.True(
                    (stat.Additions, stat.Deletions) == (diff.Additions, diff.Deletions),
                    $"{oid.ToShortHex()} '{diff.Path}': numstat +{stat.Additions}/-{stat.Deletions}, parsed +{diff.Additions}/-{diff.Deletions}");
            }
        }
    }

    private static async Task RefsAreComplete(RepositorySession session)
    {
        var git = session.Repository;
        var raw = await git.Git.RunCheckedAsync("refs", git.Root, ["for-each-ref", "--format=%(refname)", "refs/heads/", "refs/remotes/", "refs/tags/"]);
        var expected = raw.StdoutText.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Where(r => !(r.StartsWith("refs/remotes/", StringComparison.Ordinal) && r.EndsWith("/HEAD", StringComparison.Ordinal)));
        Assert.Equal(expected.Order(StringComparer.Ordinal), session.Snapshot.Refs.Select(r => r.FullName).Order(StringComparer.Ordinal));
    }

    private static async Task StageAndUnstageRoundTrip(RepositorySession session)
    {
        bool Plain(StatusEntry e) => !e.IsSubmodule;

        var toStage = session.Snapshot.Status.Entries.Where(e => Plain(e) && e.Unstaged is not null).Select(e => e.Path).ToList();
        await session.RunAsync("Stage all", (git, ct) => git.StageAsync(toStage, ct));
        var leftover = session.Snapshot.Status.Entries.Where(e => Plain(e) && e.Unstaged is not null).ToList();
        Assert.True(leftover.Count == 0, "still unstaged: " + string.Join(", ", leftover.Select(e => $"{e.Path} {e.IndexState}{e.WorktreeState}")));

        var toUnstage = session.Snapshot.Status.Entries.Where(e => Plain(e) && e.Staged is not null).Select(e => e.Path).ToList();
        await session.RunAsync("Unstage all", (git, ct) => git.UnstageAsync(toUnstage, ct));
        var staged = session.Snapshot.Status.Entries.Where(e => Plain(e) && e.Staged is not null).ToList();
        Assert.True(staged.Count == 0, "still staged: " + string.Join(", ", staged.Select(e => $"{e.Path} {e.IndexState}{e.WorktreeState}")));
    }
}
