using System.Diagnostics;
using Avalonia.Headless.XUnit;
using CheeseGit.App.Controls;
using CheeseGit.App.Syntax;
using CheeseGit.App.ViewModels;
using CheeseGit.Core.Repository;
using CheeseGit.Core.Tests;
using CheeseGit.Fixtures;

namespace CheeseGit.App.Tests;

/// <summary>
/// The daily loop on large repositories, timed through the real view models
/// (plan: "a large-repository check of the app"). Opt-in, because generating
/// and staging 100k files takes a while: set CHEESEGIT_LARGE_TESTS=1, and
/// optionally CHEESEGIT_LARGE_REPO to a real repository (e.g. a git/git clone),
/// which is cloned and then dirtied, never modified itself.
/// Run with <c>--logger "console;verbosity=detailed"</c> to see the timings.
/// </summary>
public class LargeRepoTests(ITestOutputHelper output)
{
    private const int Timeout = 120_000;

    private static bool Enabled => Environment.GetEnvironmentVariable("CHEESEGIT_LARGE_TESTS") == "1";

    [AvaloniaFact]
    public async Task Large_worktree_daily_loop()
    {
        Assert.SkipUnless(Enabled, "set CHEESEGIT_LARGE_TESTS=1");
        using var dir = TempRepo.EmptyDirectory();
        var sw = Stopwatch.StartNew();
        var fixture = await FixtureGenerator.CreateAsync(FixtureKind.LargeWorktree, dir.Path, ct: TestContext.Current.CancellationToken);
        output.WriteLine($"fixture: {sw.ElapsedMilliseconds} ms");
        await this.DailyLoopAsync(fixture.Path, "generated/Constants.cs");
    }

    [AvaloniaFact]
    public async Task Real_repository_daily_loop()
    {
        Assert.SkipUnless(Enabled, "set CHEESEGIT_LARGE_TESTS=1");
        var source = Environment.GetEnvironmentVariable("CHEESEGIT_LARGE_REPO");
        Assert.SkipWhen(string.IsNullOrEmpty(source), "set CHEESEGIT_LARGE_REPO to a repository to clone");
        using var dir = TempRepo.EmptyDirectory();
        dir.Raw("clone", "-q", "--local", source!, "repo");
        var repo = Path.Combine(dir.Path, "repo");

        // A sweeping change: every tracked text file edited, 5,000 new files.
        var tracked = dir.Raw("-C", repo, "ls-files", "-z").Split('\0', StringSplitOptions.RemoveEmptyEntries);
        var edited = 0;
        foreach (var file in tracked)
        {
            var full = Path.Combine(repo, file);
            // Dotfiles (.gitmodules, .gitattributes) are config: an appended line can make git refuse to run.
            if (Path.GetFileName(file).StartsWith('.') || !File.Exists(full) || new FileInfo(full).LinkTarget is not null || IsBinary(full)) continue;
            File.AppendAllText(full, "// edited\n");
            edited++;
        }
        for (var i = 0; i < 5000; i++)
        {
            var path = Path.Combine(repo, "untracked", $"u{i / 100:00}", $"new{i % 100:00}.txt");
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, $"new {i}\n");
        }
        output.WriteLine($"{tracked.Length} tracked files, {edited} edited, 5000 untracked");
        var largest = tracked.Where(f => f.EndsWith(".c", StringComparison.Ordinal)).MaxBy(f => new FileInfo(Path.Combine(repo, f)).Length)!;
        await this.DailyLoopAsync(repo, largest);
    }

    [AvaloniaFact]
    public async Task Large_history()
    {
        Assert.SkipUnless(Enabled, "set CHEESEGIT_LARGE_TESTS=1");
        using var dir = TempRepo.EmptyDirectory();
        var source = Environment.GetEnvironmentVariable("CHEESEGIT_LARGE_REPO") is { Length: > 0 } real
            ? real
            : (await FixtureGenerator.CreateAsync(FixtureKind.LargeHistory, Path.Combine(dir.Path, "fixture"), ct: TestContext.Current.CancellationToken)).Path;
        dir.Raw("clone", "-q", "--local", "--no-checkout", source, "repo");
        var repo = Path.Combine(dir.Path, "repo");
        dir.Raw("-C", repo, "checkout", "-q");
        // Local branches for every remote one, so the visibility list and "show all" have work to do.
        foreach (var remote in dir.Raw("-C", repo, "for-each-ref", "--format=%(refname:lstrip=3)", "refs/remotes/origin/").Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            if (remote != "HEAD") dir.TryRaw("-C", repo, "branch", "-q", "--track", remote, "origin/" + remote);
        }
        await this.HistoryAsync(repo);
    }

    private async Task HistoryAsync(string repo)
    {
        using var main = AppHarness.CreateMain();
        this.log = main.CommandLog;
        await main.OpenRepositoryAsync(repo);
        var tab = Assert.Single(main.Tabs);
        await AppHarness.Until(() => tab.Staging.Loaded, "the staging lists", Timeout);
        var history = tab.History;

        var sw = Stopwatch.StartNew();
        tab.LeftTab = LeftPanelTab.History;
        await AppHarness.Until(() => history.Rows.Count > 0, "the history", Timeout);
        this.Lap(sw, $"history shown ({history.Rows.Count} commits, {history.View!.Layout.LaneCount} lanes, {history.Branches.Count} branches)");

        _ = history.Rows.Take(30).Select(r => r.Summary).ToList();
        await AppHarness.Until(() => history.Rows.Take(30).All(r => r.Summary.Length > 0), "the first screen's text", Timeout);
        this.Lap(sw, "first screen's text");
        await AppHarness.Until(() => history.Rows.Take(30).All(r => r.Stats is not null), "the first screen's stats", Timeout);
        this.Lap(sw, "first screen's stats");

        history.ToggleAllCommand.Execute(null);
        await AppHarness.Until(() => history.AllBranchesVisible && history.View!.VisibleBranches.Count >= history.Branches.Count, "show all", Timeout);
        this.Lap(sw, $"show all branches ({history.Rows.Count} commits, {history.View!.Layout.LaneCount} lanes)");

        var far = history.Rows[history.Rows.Count / 2];
        history.Select(far);
        _ = far.Summary;
        await AppHarness.Until(() => !history.Details.FilesLoading, "the commit's files", Timeout);
        this.Lap(sw, $"select a commit in the middle ({history.Details.Files.Count} files)");

        var before = history.Rows.Count;
        File.WriteAllText(Path.Combine(repo, "history-check.txt"), "x\n");
        await AppHarness.Until(() => !tab.Staging.Unstaged.IsEmpty, "the new file", Timeout);
        await tab.Staging.Unstaged.BulkActionCommand.ExecuteAsync(null);
        await AppHarness.Until(() => tab.Staging.Unstaged.IsEmpty, "staging", Timeout);
        sw.Restart();
        tab.Staging.Summary = "History check";
        await tab.Staging.CommitCommand.ExecuteAsync(null);
        await AppHarness.Until(() => history.Rows.Count == before + 1, "the history to show the commit", Timeout);
        Assert.Equal(far.Oid, history.Selected?.Oid);
        this.Lap(sw, "commit until the history shows it (selection kept)");

        await history.CheckoutCommand.ExecuteAsync(history.Rows[50]);
        await AppHarness.Until(() => tab.BrowsingHistory && history.Rows.Any(r => r.IsHead), "checkout", Timeout);
        this.Lap(sw, "check out a commit, history reloaded");
        await history.JumpToPresentCommand.ExecuteAsync(null);
        await AppHarness.Until(() => !tab.BrowsingHistory && history.Rows[0].IsHead, "jump back", Timeout);
        this.Lap(sw, "jump back to present");
    }

    private static bool IsBinary(string path)
    {
        using var stream = File.OpenRead(path);
        var buffer = new byte[8000];
        var n = stream.Read(buffer);
        return buffer.AsSpan(0, n).Contains((byte)0);
    }

    private async Task DailyLoopAsync(string repo, string bigFile)
    {
        var main = AppHarness.CreateMain();
        using var _ = main;
        this.log = main.CommandLog;
        var sw = Stopwatch.StartNew();

        await main.OpenRepositoryAsync(repo);
        var tab = Assert.Single(main.Tabs);
        Assert.Null(tab.OpenError);
        await AppHarness.Until(() => tab.Staging.Loaded, "the staging lists", Timeout);
        var unstaged = tab.Staging.Unstaged.TotalCount;
        var staged = tab.Staging.Staged.TotalCount;
        this.Lap(sw, $"open, lists filled ({unstaged} unstaged, {staged} staged)");

        await AppHarness.Until(() => tab.Staging.Unstaged.TotalAdditions.Length > 0, "the diff stats", Timeout);
        this.Lap(sw, $"diff stats ({tab.Staging.Unstaged.TotalAdditions} {tab.Staging.Unstaged.TotalDeletions})");

        tab.Staging.Filter = "1";
        var filtered = tab.Staging.Unstaged.Rows.Count;
        tab.Staging.Filter = "";
        this.Lap(sw, $"filter on and off ({filtered} matched)");

        var rows = tab.Staging.Unstaged.Rows;
        tab.Staging.Unstaged.Click(rows[0], ctrl: false, shift: false);
        tab.Staging.Unstaged.Click(rows[^1], ctrl: false, shift: true);
        Assert.Equal(rows.Count, tab.Staging.Unstaged.SelectedCount);
        tab.Staging.Unstaged.Click(rows[0], ctrl: false, shift: false);
        this.Lap(sw, "select all rows with Shift+click");

        var big = tab.Staging.Unstaged.All.First(r => r.DisplayPath == bigFile);
        tab.Staging.Unstaged.Click(big, ctrl: false, shift: false);
        await AppHarness.Until(() => tab.Diff.Diff is { } d && d.Path == big.Path, "the big diff", Timeout);
        var diff = tab.Diff.Diff!;
        this.Lap(sw, $"big diff loaded ({diff.Hunks.Count} hunks, +{diff.Additions} −{diff.Deletions})");
        var unifiedRows = DiffRows.Unified(diff).Count;
        DiffRows.Split(diff);
        this.Lap(sw, $"big diff rows, unified ({unifiedRows}) and split");
        SyntaxHighlighter.Shared.TokenizeDiff(diff, TestContext.Current.CancellationToken);
        this.Lap(sw, "big diff tokenized");

        var total = unstaged + staged;
        await tab.Staging.Unstaged.BulkActionCommand.ExecuteAsync(null);
        await AppHarness.Until(() => tab.Staging.Unstaged.IsEmpty && tab.Staging.Staged.TotalCount > staged, "stage all", Timeout);
        this.Lap(sw, $"stage all ({tab.Staging.Staged.TotalCount} staged)");

        await tab.Staging.Staged.BulkActionCommand.ExecuteAsync(null);
        await AppHarness.Until(() => tab.Staging.Staged.IsEmpty, "unstage all", Timeout);
        this.Lap(sw, $"unstage all ({tab.Staging.Unstaged.TotalCount} unstaged)");

        await tab.Staging.Unstaged.BulkActionCommand.ExecuteAsync(null);
        await AppHarness.Until(() => tab.Staging.Unstaged.IsEmpty, "stage all again", Timeout);
        sw.Restart();
        tab.Staging.Summary = "Large commit";
        await tab.Staging.CommitCommand.ExecuteAsync(null);
        await AppHarness.Until(() => tab.Staging.IsClean, "the commit", Timeout);
        this.Lap(sw, "commit everything");

        await tab.Staging.UndoLastCommitAsync();
        await AppHarness.Until(() => tab.Staging.Staged.TotalCount >= total - 1, "undo", Timeout);
        this.Lap(sw, "undo the commit");
    }

    private CommandLogViewModel? log;
    private readonly HashSet<CommandCall> reported = [];

    /// <summary>Prints the step's time and the slow git calls (over 50 ms) made since the last step.</summary>
    private void Lap(Stopwatch sw, string what)
    {
        output.WriteLine($"{what}: {sw.ElapsedMilliseconds} ms");
        foreach (var entry in this.log?.Entries.ToList() ?? [])
        {
            foreach (var call in entry.Calls.ToList())
            {
                if (this.reported.Add(call) && call.Elapsed.TotalMilliseconds > 50) output.WriteLine($"    {call.ElapsedText,8}  {entry.Label}: {Truncate(call.CommandLine)}");
            }
        }
        sw.Restart();
    }

    private static string Truncate(string s) => s.Length > 110 ? s[..110] + "…" : s;
}
