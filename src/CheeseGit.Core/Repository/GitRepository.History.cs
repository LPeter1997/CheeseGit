using System.Text;
using CheeseGit.Core.Git;
using CheeseGit.Core.Graph;

namespace CheeseGit.Core.Repository;

/// <summary>History topology for the graph view.</summary>
public sealed partial class GitRepository
{
    /// <summary>
    /// Loads the commits reachable from the requested branches (plus the
    /// remote's default branch, to show fork points, and HEAD when detached),
    /// in topological order, with ref decorations and local-only markers.
    /// </summary>
    public async Task<HistoryGraph> LoadHistoryAsync(HistoryRequest request, CancellationToken ct = default)
    {
        // Every existence check below is answered from this one listing. A
        // show-ref per branch cost 8-23 s per load in a repository with 70
        // branches on Windows, where each git process takes 100-600 ms to run.
        var refs = await this.ListRefsAsync(ct);
        var names = refs.Select(r => r.FullName).ToHashSet(StringComparer.Ordinal);
        var locals = refs.Where(r => r.Kind == RefKind.LocalBranch).ToList();

        var branches = request.Branches is { Count: > 0 } requested
            ? requested.Select(b => b.StartsWith("refs/", StringComparison.Ordinal) ? b : "refs/heads/" + b).ToList()
            : locals.Select(r => r.FullName).ToList();
        var remoteDefault = request.Remote is { } remote ? await this.RemoteDefaultBranchAsync(remote, names, ct) : null;
        if (remoteDefault is not null && !branches.Contains(remoteDefault)) branches.Add(remoteDefault);
        var defaultBranch = DefaultBranchOf(remoteDefault, request.Remote, locals.Select(r => r.FullName).ToHashSet(StringComparer.Ordinal));

        var tips = new List<string>(branches.Where(names.Contains));
        var head = await this.HeadAsync(ct);
        if (head is { BrowsingHistory: true, Commit: { } detached }) tips.Add(detached.ToHex());

        var dag = await DagLoader.LoadAsync(this.Git, this.Root, tips, request.MaxCommits, ct);

        var refsByCommit = refs
            .GroupBy(r => r.Target)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<RefInfo>)[.. g.OrderBy(r => r.Kind).ThenBy(r => r.FullName, StringComparer.Ordinal)]);

        var localOnly = request.Remote is { } r2
            ? await this.LocalOnlyCommitsAsync(r2, branches.Where(b => b.StartsWith("refs/heads/", StringComparison.Ordinal)).ToList(), refs, ct)
            : [];
        return new HistoryGraph(dag, branches, refsByCommit, localOnly, defaultBranch);
    }

    private static string? DefaultBranchOf(string? remoteDefault, string? remote, IReadOnlySet<string> locals)
    {
        if (remoteDefault is not null && remote is not null && remoteDefault.StartsWith($"refs/remotes/{remote}/", StringComparison.Ordinal))
        {
            var local = "refs/heads/" + remoteDefault[$"refs/remotes/{remote}/".Length..];
            return locals.Contains(local) ? local : remoteDefault;
        }
        return new[] { "refs/heads/main", "refs/heads/master" }.FirstOrDefault(locals.Contains);
    }

    /// <summary>
    /// Author and subject of every commit reachable from <paramref name="revisions"/>,
    /// for searching the history. One streamed <c>git log</c>: it parses every
    /// commit object (1.3 s on git/git's 82k commits), where reading them one by
    /// one through cat-file would take about 5 s.
    /// </summary>
    public async Task<IReadOnlyList<CommitSummary>> CommitSummariesAsync(IReadOnlyCollection<string> revisions, CancellationToken ct = default)
    {
        var summaries = new List<CommitSummary>();
        if (revisions.Count == 0) return summaries;
        var stdin = Encoding.UTF8.GetBytes(string.Join('\n', revisions) + "\n");
        // %s joins the subject's lines and names cannot hold a newline or NUL,
        // so a record is one line of three NUL-separated fields.
        var result = await this.Git.StreamRecordsAsync(
            this.Root,
            ["log", "--encoding=UTF-8", "--format=%H%x00%an%x00%s", "--stdin"],
            (byte)'\n',
            record =>
            {
                var f = Fields(record, 3);
                if (f is not null && Oid.TryParseHex(f[0], out var oid)) summaries.Add(new CommitSummary(oid, Utf8(f[1]), Utf8(f[2])));
            },
            stdin: stdin,
            ct: ct);
        result.EnsureSuccess("Search history");
        return summaries;
    }

    /// <summary>
    /// Whether writing a commit-graph would speed up loading the history: git would use one
    /// (<c>core.commitGraph</c> is not off, the repository is not shallow) and none exists, as a
    /// single file or a split chain. With one, the topology of 100k commits loads in 180 ms
    /// instead of 850 ms (Phase 3).
    /// </summary>
    public async Task<bool> CommitGraphMissingAsync(CancellationToken ct = default)
    {
        var enabled = await this.TryReadAsync(["config", "--type=bool", "--get", "core.commitGraph"], ct: ct);
        if (enabled.Success && enabled.StdoutText.Trim() == "false") return false;
        var shallow = await this.ReadAsync("Check shallow", ["rev-parse", "--is-shallow-repository"], ct: ct);
        if (shallow.StdoutText.Trim() == "true") return false;
        return !File.Exists(await this.GitPathAsync("objects/info/commit-graph", ct))
            && !File.Exists(await this.GitPathAsync("objects/info/commit-graphs/commit-graph-chain", ct));
    }

    /// <summary>
    /// Writes a commit-graph for every reachable commit, with changed-path Bloom filters (docs/architecture.md, Git engine).
    /// 3.3 s for 100k commits. It only adds a file under objects/info, so no state changes.
    /// </summary>
    public Task WriteCommitGraphAsync(CancellationToken ct = default) =>
        this.WriteAsync("Write commit-graph", ["commit-graph", "write", "--reachable", "--changed-paths"], ct: ct);

    /// <summary>
    /// The remote's default branch: what refs/remotes/&lt;remote&gt;/HEAD points
    /// to, else its main or master branch. Null if none exists.
    /// </summary>
    public async Task<string?> RemoteDefaultBranchAsync(string remote, CancellationToken ct = default) =>
        await this.RemoteDefaultBranchAsync(remote, (await this.ListRefsAsync(ct)).Select(r => r.FullName).ToHashSet(StringComparer.Ordinal), ct);

    private async Task<string?> RemoteDefaultBranchAsync(string remote, IReadOnlySet<string> refNames, CancellationToken ct)
    {
        var symbolic = await this.TryReadAsync(["symbolic-ref", "--quiet", $"refs/remotes/{remote}/HEAD"], ct: ct);
        if (symbolic.Success) return symbolic.StdoutText.Trim();
        return new[] { "main", "master" }.Select(name => $"refs/remotes/{remote}/{name}").FirstOrDefault(refNames.Contains);
    }

    /// <summary>
    /// Commits of local branches that are not on <paramref name="remote"/>.
    /// A branch published there is compared with its counterpart; an
    /// unpublished branch with every branch of that remote.
    /// </summary>
    private async Task<HashSet<Oid>> LocalOnlyCommitsAsync(string remote, IReadOnlyList<string> localBranches, IReadOnlyList<RefInfo> refs, CancellationToken ct)
    {
        var result = new HashSet<Oid>();
        var remotePrefix = $"refs/remotes/{remote}/";
        // ListRefsAsync leaves out refs/remotes/<remote>/HEAD, which only repeats a branch.
        var remoteRefs = refs.Where(r => r.Kind == RefKind.RemoteBranch && r.FullName.StartsWith(remotePrefix, StringComparison.Ordinal)).Select(r => r.FullName).ToList();
        var remoteNames = remoteRefs.ToHashSet(StringComparer.Ordinal);
        var published = new List<string>();
        var unpublished = new List<string>();
        foreach (var branch in localBranches)
        {
            var counterpart = remotePrefix + branch["refs/heads/".Length..];
            if (remoteNames.Contains(counterpart)) published.Add($"{counterpart}..{branch}");
            else unpublished.Add(branch);
        }

        // Two walks, since one rev-list would apply the exclusions to both. They
        // are independent, so they run side by side (RevListIntoAsync locks the set).
        await Task.WhenAll(
            published.Count > 0 ? this.RevListIntoAsync(published, result, ct) : Task.CompletedTask,
            unpublished.Count > 0 ? this.RevListIntoAsync([.. unpublished, .. remoteRefs.Select(r => "^" + r)], result, ct) : Task.CompletedTask);
        return result;
    }

    private async Task RevListIntoAsync(IEnumerable<string> revisions, HashSet<Oid> into, CancellationToken ct)
    {
        var stdin = Encoding.UTF8.GetBytes(string.Concat(revisions.Select(r => r + "\n")));
        var result = await this.Git.StreamRecordsAsync(
            this.Root,
            ["rev-list", "--stdin"],
            (byte)'\n',
            record =>
            {
                if (Oid.TryParseHex(record, out var oid))
                {
                    lock (into) into.Add(oid);
                }
            },
            stdin: stdin,
            ct: ct);
        result.EnsureSuccess("List local-only commits");
    }
}
