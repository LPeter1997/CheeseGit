using System.Diagnostics;
using System.Text;
using CheeseGit.Core.Git;
using CheeseGit.Core.Graph;
using CheeseGit.Core.Repository;
using CheeseGit.Fixtures;

namespace CheeseGit.Cli;

/// <summary>
/// Headless harness for the engine: inspect a repository the way the app
/// will, without the UI. Native AOT, like the app, so it also proves the
/// engine stays trim-safe.
/// </summary>
public static class Program
{
    private const string Usage = """
        usage: cheesegit-core <command> [options]

          status  [repo]                 status, HEAD and paused operation
          log     [repo] [-n N]          commits of all local branches, newest first
          graph   [repo] [-n N]          the history graph as text
          watch   [repo]                 print snapshot changes until Ctrl+C
          bench   [repo]                 time the main engine queries
          fixture <kind> <dir> [-n N]    generate a fixture repository (N commits for LargeHistory)

        fixture kinds:
        """;

    public static async Task<int> Main(string[] args)
    {
        // The tests use this harness as git's askpass helper (the app does the same).
        if (CheeseGit.Core.Git.Askpass.TryRunHelper(args) is { } askpassExit) return askpassExit;
        Console.OutputEncoding = Encoding.UTF8;
        if (args.Length == 0 || args[0] is "-h" or "--help")
        {
            Console.WriteLine(Usage + string.Join(", ", Enum.GetNames<FixtureKind>()));
            return args.Length == 0 ? 2 : 0;
        }

        var options = Options.Parse(args.AsSpan(1));
        try
        {
            return args[0] switch
            {
                "status" => await StatusAsync(options),
                "log" => await LogAsync(options),
                "graph" => await GraphAsync(options),
                "watch" => await WatchAsync(options),
                "bench" => await BenchAsync(options),
                "fixture" => await FixtureAsync(options),
                _ => Fail($"unknown command '{args[0]}'"),
            };
        }
        catch (GitException e)
        {
            return Fail(e.Message);
        }
    }

    private static async Task<int> StatusAsync(Options o)
    {
        using var session = await RepositorySession.OpenAsync(o.Repo, new SessionOptions { Watch = false });
        Print(session.Snapshot);
        return 0;
    }

    private static void Print(RepoSnapshot s)
    {
        var b = s.Status.Branch;
        var head = s.Head.IsUnborn ? "(no commits)" : s.Head.Commit!.Value.ToShortHex();
        Console.WriteLine(s.Head.BrowsingHistory ? $"HEAD detached at {head} (near {s.Head.Branch})" : $"On {s.Head.Branch} at {head}");
        if (b.Upstream is not null) Console.WriteLine($"Upstream {b.Upstream}: ahead {b.Ahead}, behind {b.Behind}");
        if (s.Operation.Kind != OperationKind.None) Console.WriteLine($"{s.Operation.Kind} in progress ({s.Operation.Incoming}), {s.Operation.ConflictCount} conflicted");
        Console.WriteLine($"{s.Refs.Count} refs, {s.Stashes.Count} stashes, remotes: {string.Join(", ", s.Remotes.Select(r => r.Name))}");
        foreach (var e in s.Status.Entries)
        {
            var original = e.OriginalPath is { } from ? $" (from {from})" : "";
            Console.WriteLine($"  {e.IndexState}{e.WorktreeState} {Escape(e.Path)}{original}{(e.IsSubmodule ? " [submodule]" : "")}");
        }
    }

    private static async Task<int> LogAsync(Options o)
    {
        using var git = await GitRepository.OpenAsync(o.Repo);
        var history = await git.LoadHistoryAsync(new HistoryRequest(MaxCommits: o.Count));
        for (var row = 0; row < history.Dag.Count; row++)
        {
            var c = git.GetCommit(history.Dag.OidAt(row))!;
            Console.WriteLine($"{c.Oid.ToShortHex()} {c.Author.When:yyyy-MM-dd} {c.Author.Name,-16} {Decorations(history, c.Oid)}{c.Summary}");
        }
        return 0;
    }

    private static async Task<int> GraphAsync(Options o)
    {
        using var git = await GitRepository.OpenAsync(o.Repo);
        var history = await git.LoadHistoryAsync(new HistoryRequest(MaxCommits: o.Count));
        var head = await git.ResolveAsync("HEAD");
        var layout = GraphLayout.Compute(history.Dag, head is { } h ? Math.Max(0, history.Dag.IndexOf(h)) : 0, maxEdgeRows: 40);
        if (LayoutValidator.Validate(history.Dag, layout) is { } error) Console.Error.WriteLine($"layout invalid: {error}");

        for (var row = 0; row < layout.RowCount; row++)
        {
            var c = git.GetCommit(history.Dag.OidAt(row))!;
            var (node, connectors) = TextGraph.Row(layout, row);
            Console.WriteLine($"{node} {c.Oid.ToShortHex()} {Decorations(history, c.Oid)}{c.Summary}");
            if (connectors is not null) Console.WriteLine(connectors);
        }
        return 0;
    }

    private static async Task<int> WatchAsync(Options o)
    {
        using var session = await RepositorySession.OpenAsync(o.Repo);
        Console.WriteLine(session.IsWatching ? "watching (Ctrl+C to stop)" : "watching unavailable, polling");
        Print(session.Snapshot);
        session.SnapshotChanged += change =>
        {
            Console.WriteLine($"── v{change.Snapshot.Version}: {change.Changed}");
            Print(change.Snapshot);
        };
        session.RefreshFailed += e => Console.Error.WriteLine($"refresh failed: {e.Message}");
        var stop = new TaskCompletionSource();
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            stop.TrySetResult();
        };
        await stop.Task;
        return 0;
    }

    private static async Task<int> BenchAsync(Options o)
    {
        static async Task<T> Time<T>(string label, Func<Task<T>> run)
        {
            var sw = Stopwatch.StartNew();
            var result = await run();
            Console.WriteLine($"{label,-28} {sw.Elapsed.TotalMilliseconds,8:F1} ms");
            return result;
        }

        using var git = await Time("open", () => GitRepository.OpenAsync(o.Repo));
        await Time("status (cold)", () => git.StatusAsync());
        await Time("status (warm)", () => git.StatusAsync());
        await Time("refs", () => git.ListRefsAsync());
        var history = await Time("history (all branches)", () => git.LoadHistoryAsync(new HistoryRequest()));
        var head = await git.ResolveAsync("HEAD");
        var layout = await Time("layout", () => Task.FromResult(GraphLayout.Compute(history.Dag, head is { } h ? Math.Max(0, history.Dag.IndexOf(h)) : 0)));
        await Time("metadata, first 100 rows", () => Task.FromResult(Enumerable.Range(0, Math.Min(100, history.Dag.Count)).Count(r => git.GetCommit(history.Dag.OidAt(r)) is not null)));
        Console.WriteLine($"{history.Dag.Count} commits, {layout.LaneCount} lanes, {layout.SegmentCount} segments");
        return 0;
    }

    private static async Task<int> FixtureAsync(Options o)
    {
        if (o.Positional.Count < 2 || !Enum.TryParse<FixtureKind>(o.Positional[0], ignoreCase: true, out var kind))
        {
            return Fail("usage: cheesegit-core fixture <kind> <dir> [-n commits]");
        }
        var options = new FixtureOptions { LargeHistoryCommits = o.Count ?? 100_000 };
        var fixture = await FixtureGenerator.CreateAsync(kind, Path.GetFullPath(o.Positional[1]), options);
        Console.WriteLine(fixture.Path);
        return 0;
    }

    private static string Decorations(HistoryGraph history, Oid oid)
    {
        var refs = history.RefsOf(oid);
        return refs.Count == 0 ? "" : $"({string.Join(", ", refs.Select(r => r.ShortName))}) ";
    }

    /// <summary>Control characters (newlines in file names) would break the line-based output.</summary>
    private static string Escape(GitPath path)
    {
        var text = path.ToString();
        return text.Any(char.IsControl) ? string.Concat(text.Select(ch => char.IsControl(ch) ? $"\\x{(int)ch:x2}" : ch.ToString())) : text;
    }

    private static int Fail(string message)
    {
        Console.Error.WriteLine($"cheesegit-core: {message}");
        return 1;
    }

    private sealed record Options(List<string> Positional, int? Count)
    {
        public string Repo => this.Positional.Count > 0 ? this.Positional[0] : Directory.GetCurrentDirectory();

        public static Options Parse(ReadOnlySpan<string> args)
        {
            var positional = new List<string>();
            int? count = null;
            for (var i = 0; i < args.Length; i++)
            {
                if (args[i] == "-n" && i + 1 < args.Length && int.TryParse(args[i + 1], out var n))
                {
                    count = n;
                    i++;
                }
                else
                {
                    positional.Add(args[i]);
                }
            }
            return new Options(positional, count);
        }
    }
}
