using System.Text;
using CheeseGit.Core.Diff;
using CheeseGit.Core.Git;

namespace CheeseGit.Core.Repository;

/// <summary>Remotes, fetch/pull/push, and stashes.</summary>
public sealed partial class GitRepository
{
    /// <param name="progress">
    /// Gets git's progress meter, line by line, on a background thread. Without it git is not
    /// asked for progress (it prints none when stderr is not a terminal).
    /// </param>
    public async Task FetchAsync(string remote, IProgress<GitProgress>? progress = null, CancellationToken ct = default)
    {
        ValidateRemoteName(remote);
        string[] args = ["fetch", "--prune", .. ProgressFlag(progress), "--end-of-options", remote];
        try
        {
            await this.WriteAsync("Fetch", args, ct: ct, progress: progress);
        }
        catch (GitCommandException)
        {
            if (!await this.PruneCaseCollisionsAsync(remote, ct)) throw;
            await this.WriteAsync("Fetch", args, ct: ct, progress: progress);
        }
    }

    /// <summary>
    /// Prunes stale remote-tracking refs whose names differ only in case
    /// (<c>origin/Release/x</c> and <c>origin/release/x</c>) in separate transactions. On a
    /// case-insensitive file system both lock the same <c>.lock</c> file, so git's single prune
    /// transaction fails with "cannot lock ref … File exists", deletes nothing, and every later
    /// fetch fails the same way (seen on Windows with 623 pruned refs). Returns whether it deleted
    /// anything, i.e. whether retrying the fetch can succeed. Needs <c>fetch --porcelain</c> (git 2.41).
    /// </summary>
    private async Task<bool> PruneCaseCollisionsAsync(string remote, CancellationToken ct)
    {
        if (this.Version.CompareTo(new GitVersion(2, 41, 0, "2.41.0")) < 0) return false;
        var ignoreCase = await this.TryReadAsync(["config", "--type=bool", "--get", "core.ignorecase"], ct: ct);
        if (ignoreCase.StdoutText.Trim() != "true") return false;

        var dryRun = await this.TryWriteAsync(["fetch", "--prune", "--dry-run", "--porcelain", "--end-of-options", remote], ct: ct);
        if (!dryRun.Success) return false;
        var rounds = PruneRounds(PrunedRefs(dryRun.Stdout));
        if (rounds.Count < 2) return false;

        foreach (var round in rounds)
        {
            var stdin = new List<byte>();
            foreach (var name in round)
            {
                stdin.AddRange("delete "u8);
                stdin.AddRange(name);
                stdin.Add((byte)'\n');
            }
            await this.WriteAsync("Prune remote branches", ["update-ref", "--stdin"], stdin.ToArray(), ct);
        }
        return true;
    }

    /// <summary>Refs from <c>fetch --porcelain</c> lines (<c>flag old new ref</c>) with the prune flag <c>-</c>.</summary>
    internal static List<byte[]> PrunedRefs(ReadOnlySpan<byte> porcelain)
    {
        var refs = new List<byte[]>();
        foreach (var range in porcelain.Split((byte)'\n'))
        {
            var line = porcelain[range];
            if (line.Length < 2 || line[0] != (byte)'-' || line[1] != (byte)' ') continue;
            var name = line[(line.LastIndexOf((byte)' ') + 1)..];
            if (name.StartsWith("refs/"u8)) refs.Add(name.ToArray());
        }
        return refs;
    }

    /// <summary>
    /// Splits refs into transactions in which no two names are equal ignoring case:
    /// round k deletes the k-th name of each case-folded group.
    /// </summary>
    internal static List<List<byte[]>> PruneRounds(IEnumerable<byte[]> refs)
    {
        var rounds = new List<List<byte[]>>();
        var seen = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var name in refs)
        {
            var key = Encoding.UTF8.GetString(name).ToUpperInvariant();
            var round = seen.GetValueOrDefault(key);
            seen[key] = round + 1;
            if (round == rounds.Count) rounds.Add([]);
            rounds[round].Add(name);
        }
        return rounds;
    }

    /// <summary>
    /// Pushes the current branch to the branch of the same name on <paramref name="remote"/>.
    /// Returns 0 when pushed, or, when git refused because the remote branch has commits this
    /// branch lacks, how many (they are fetched, so a pull can follow). Other failures throw.
    /// </summary>
    public async Task<int> PushAsync(string remote, IProgress<GitProgress>? progress = null, CancellationToken ct = default)
    {
        ValidateRemoteName(remote);
        var branch = await this.RequireBranchAsync("push", ct);
        return await this.PushOrCountMissingAsync("Push", remote, branch, ["push", .. ProgressFlag(progress), "--end-of-options", remote, "HEAD"], progress, ct);
    }

    /// <summary>Pushes the current branch and sets it to track the pushed branch. Returns as <see cref="PushAsync"/> does.</summary>
    public async Task<int> PublishAsync(string remote, IProgress<GitProgress>? progress = null, CancellationToken ct = default)
    {
        ValidateRemoteName(remote);
        var branch = await this.RequireBranchAsync("publish", ct);
        return await this.PushOrCountMissingAsync("Publish branch", remote, branch, ["push", "--set-upstream", .. ProgressFlag(progress), "--end-of-options", remote, "HEAD"], progress, ct);
    }

    /// <summary>
    /// git's "rejected (fetch first)" is a message, not an exit code, so the cause is read from the
    /// repository instead: fetch the remote branch and check whether it is an ancestor of HEAD. If
    /// it is (a hook, a protected branch, no access), the push failed for another reason.
    /// </summary>
    private async Task<int> PushOrCountMissingAsync(string title, string remote, string branch, string[] args, IProgress<GitProgress>? progress, CancellationToken ct)
    {
        try
        {
            await this.WriteAsync(title, args, ct: ct, progress: progress);
            return 0;
        }
        catch (GitCommandException)
        {
            int missing;
            try
            {
                await this.WriteAsync("Fetch", ["fetch", "--end-of-options", remote, "refs/heads/" + branch], ct: ct);
                var count = await this.TryReadAsync(["rev-list", "--count", "FETCH_HEAD", "--not", "HEAD", "--"], ct: ct);
                missing = count.Success && int.TryParse(count.StdoutText.Trim(), out var n) ? n : 0;
            }
            catch (GitCommandException)
            {
                missing = 0; // The branch is not on the remote, or the remote cannot be reached: the push error stands.
            }
            if (missing > 0) return missing;
            throw;
        }
    }

    /// <summary>
    /// Pulls into the current branch from <paramref name="remote"/>: its
    /// upstream branch when the upstream is on that remote, otherwise the
    /// branch of the same name. pull.rebase and pull.ff are honoured.
    /// </summary>
    /// <summary>
    /// Pulls the current branch. Returns true when the pull merged with conflicts and the merge is
    /// paused (MERGE_HEAD and unmerged files), so the caller can open the conflict dialog.
    /// </summary>
    public async Task<bool> PullAsync(string remote, IProgress<GitProgress>? progress = null, CancellationToken ct = default)
    {
        ValidateRemoteName(remote);
        var branch = await this.RequireBranchAsync("pull", ct);
        var merge = await this.TryReadAsync(["config", "--get", $"branch.{branch}.merge"], ct: ct);
        var upstreamRemote = await this.TryReadAsync(["config", "--get", $"branch.{branch}.remote"], ct: ct);
        var source = merge.Success && upstreamRemote.StdoutText.Trim() == remote ? merge.StdoutText.Trim() : "refs/heads/" + branch;
        // Since git 2.27 a pull on diverged branches fails ("Need to specify how to reconcile
        // divergent branches") unless pull.rebase or pull.ff is set. Without either, merge, which
        // is what git did before; a configured choice is the user's.
        var reconcile = await this.HasPullConfigAsync(branch, ct) ? [] : new[] { "--no-rebase" };
        try
        {
            await this.WriteAsync("Pull", ["pull", "--no-edit", .. reconcile, .. ProgressFlag(progress), "--end-of-options", remote, source], ct: ct, progress: progress);
            return false;
        }
        catch (GitCommandException)
        {
            if (await this.StateFileExistsAsync("MERGE_HEAD", ct) && (await this.ConflictedFilesAsync(ct)).Count > 0) return true;
            throw;
        }
    }

    private async Task<bool> HasPullConfigAsync(string branch, CancellationToken ct)
    {
        foreach (var key in (string[])["pull.rebase", "pull.ff", $"branch.{branch}.rebase"])
        {
            if ((await this.TryReadAsync(["config", "--get", key], ct: ct)).Success) return true;
        }
        return false;
    }

    /// <summary>Adds a remote, then checks that it is reachable; an unreachable remote is removed again.</summary>
    public async Task AddRemoteAsync(string name, string url, CancellationToken ct = default)
    {
        ValidateRemoteName(name);
        await this.WriteAsync("Add remote", ["remote", "add", "--", name, url], ct: ct);
        var check = await this.TryReadAsync(["ls-remote", "--heads", "--end-of-options", name], ct: ct);
        if (check.Success) return;
        await this.TryWriteAsync(["remote", "remove", "--", name], ct: ct);
        check.EnsureSuccess("Reach remote");
    }

    public async Task RemoveRemoteAsync(string name, CancellationToken ct = default)
    {
        ValidateRemoteName(name);
        await this.WriteAsync("Remove remote", ["remote", "remove", "--", name], ct: ct);
    }

    private static string[] ProgressFlag(IProgress<GitProgress>? progress) => progress is null ? [] : ["--progress"];

    private async Task<string> RequireBranchAsync(string action, CancellationToken ct) =>
        await this.CurrentBranchAsync(ct) ?? throw new GitOperationException(GitErrorCode.DetachedHead, $"Cannot {action} while browsing history.");

    // ── Stashes ─────────────────────────────────────────────────────

    /// <summary>Stashes only the staged changes (git 2.35+); unstaged changes stay in the working tree.</summary>
    public async Task StashStagedAsync(string message, CancellationToken ct = default)
    {
        if (!this.Version.SupportsStashStaged)
        {
            throw new GitOperationException(GitErrorCode.Unsupported, $"Stashing staged changes needs git 2.35 or newer (found {this.Version}).");
        }
        List<string> args = ["stash", "push", "--quiet", "--staged"];
        if (message.Length > 0) args.AddRange(["--message", message]);
        await this.WriteAsync("Stash staged changes", args, ct: ct);
    }

    /// <summary>Stash entries, newest first.</summary>
    public async Task<IReadOnlyList<StashEntry>> ListStashesAsync(CancellationToken ct = default)
    {
        if (!await this.RefExistsAsync("refs/stash", ct)) return [];
        // One NUL-terminated record per entry with five NUL-separated fields.
        var result = await this.ReadAsync("List stashes", ["log", "--walk-reflogs", "--format=%H%x00%an%x00%at%x00%s%x00", "refs/stash", "--"], ct: ct);
        var entries = new List<StashEntry>();
        var records = new Records(result.Stdout, 0);
        while (true)
        {
            if (!records.Next(out var hash)) break;
            hash = hash.TrimStart((byte)'\n');
            if (!records.Next(out var author) || !records.Next(out var time) || !records.Next(out var subject)) break;
            if (Oid.TryParseHex(hash, out var oid)) entries.Add(new StashEntry(entries.Count, oid, Utf8(subject), Utf8(author), UnixTime(time)));
        }
        return entries;
    }

    /// <summary>Applies a stash entry and keeps it. Conflicts are a result, not an error: the files are left to resolve.</summary>
    public Task<StashApplyResult> StashApplyAsync(int index, CancellationToken ct = default) =>
        this.StashApplyCoreAsync("apply", index, ct);

    /// <summary>Applies a stash entry and drops it; on conflicts git keeps the entry.</summary>
    public Task<StashApplyResult> StashPopAsync(int index, CancellationToken ct = default) =>
        this.StashApplyCoreAsync("pop", index, ct);

    private async Task<StashApplyResult> StashApplyCoreAsync(string verb, int index, CancellationToken ct)
    {
        var result = await this.TryWriteAsync(["stash", verb, "--quiet", StashRef(index)], ct: ct);
        if (result.Success) return StashApplyResult.Applied;
        // Exit 1 with unmerged entries: applied with conflicts (and, for pop, not dropped). Anything
        // else (local changes in the way, a bad index) changed nothing and is an error.
        if ((await this.ConflictedFilesAsync(ct)).Count > 0) return StashApplyResult.Conflicts;
        result.EnsureSuccess(verb == "pop" ? "Pop stash" : "Apply stash");
        return StashApplyResult.Applied;
    }

    public Task StashDropAsync(int index, CancellationToken ct = default) =>
        this.WriteAsync("Drop stash", ["stash", "drop", "--quiet", StashRef(index)], ct: ct);

    /// <summary>
    /// Files in a stash entry: changes against the commit it was made on, then the untracked files
    /// it holds (<c>stash -u</c>, e.g. changes left on a branch, #52). Those live in a third,
    /// parentless commit that <c>stash^1..stash</c> does not show.
    /// </summary>
    public async Task<IReadOnlyList<FileChange>> ListStashFilesAsync(int index, CancellationToken ct = default) => await this.ListStashFilesAsync((await this.StashOidAsync(index, ct)).ToHex(), ct);

    /// <summary>
    /// <see cref="ListStashFilesAsync(int, CancellationToken)"/> by the entry's commit: a view that
    /// shows an entry keeps showing it when a new stash moves it to another index.
    /// </summary>
    public Task<IReadOnlyList<FileChange>> ListStashFilesAsync(Oid stash, CancellationToken ct = default) => this.ListStashFilesAsync(stash.ToHex(), ct);

    public async Task<FileDiff> DiffStashFileAsync(int index, GitPath path, CancellationToken ct = default) => await this.DiffStashFileAsync((await this.StashOidAsync(index, ct)).ToHex(), path, ct);

    public Task<FileDiff> DiffStashFileAsync(Oid stash, GitPath path, CancellationToken ct = default) => this.DiffStashFileAsync(stash.ToHex(), path, ct);

    public async Task<IReadOnlyList<FileStats>> StashFileStatsAsync(int index, CancellationToken ct = default) => await this.StashFileStatsAsync((await this.StashOidAsync(index, ct)).ToHex(), ct);

    public Task<IReadOnlyList<FileStats>> StashFileStatsAsync(Oid stash, CancellationToken ct = default) => this.StashFileStatsAsync(stash.ToHex(), ct);

    private async Task<IReadOnlyList<FileChange>> ListStashFilesAsync(string stash, CancellationToken ct)
    {
        var result = await this.ReadAsync("List stash files", ["diff-tree", "-r", "--name-status", "-z", "-M", .. StashRange(stash)], ct: ct);
        var files = ParseNameStatus(result.Stdout);
        if (await this.HasStashUntrackedAsync(stash, ct))
        {
            var untracked = await this.ReadAsync("List stashed untracked files", ["diff-tree", "-r", "--root", "--no-commit-id", "--name-status", "-z", "--end-of-options", stash + "^3"], ct: ct);
            files.AddRange(ParseNameStatus(untracked.Stdout).Select(f => f with { Status = FileStatus.Untracked }));
        }
        return await this.MarkLfsAsync(files, stash, ct);
    }

    private async Task<FileDiff> DiffStashFileAsync(string stash, GitPath path, CancellationToken ct)
    {
        var (spec, literal) = path.ToPathspec();
        var result = await this.ReadAsync("Diff stash file", ["diff-tree", "-r", .. DiffParser.PatchArgs, .. StashRange(stash), "--", spec], ct: ct, literalPathspecs: literal);
        if (PickDiff(DiffParser.Parse(result.Stdout), path) is { } diff) return diff;
        if (await this.HasStashUntrackedAsync(stash, ct))
        {
            var untracked = await this.ReadAsync("Diff stashed untracked file", ["diff-tree", "-r", "--root", "--no-commit-id", .. DiffParser.PatchArgs, "--end-of-options", stash + "^3", "--", spec], ct: ct, literalPathspecs: literal);
            if (PickDiff(DiffParser.Parse(untracked.Stdout), path) is { } added) return added;
        }
        return FileDiff.Empty(path);
    }

    private async Task<IReadOnlyList<FileStats>> StashFileStatsAsync(string stash, CancellationToken ct)
    {
        var result = await this.ReadAsync("Read stash stats", ["diff-tree", "-r", "--numstat", "-z", .. StashRange(stash)], ct: ct);
        var stats = ParseNumstat(result.Stdout);
        if (await this.HasStashUntrackedAsync(stash, ct))
        {
            var untracked = await this.ReadAsync("Read stashed untracked stats", ["diff-tree", "-r", "--root", "--no-commit-id", "--numstat", "-z", "--end-of-options", stash + "^3"], ct: ct);
            stats.AddRange(ParseNumstat(untracked.Stdout));
        }
        return stats;
    }

    /// <remarks>
    /// <paramref name="stash"/> is a commit id: git 2.56 resolves <c>stash@{0}^3^{commit}</c> to the stash
    /// itself when it has no third parent (the reflog selector swallows the suffix), so the index
    /// overloads resolve the entry first.
    /// </remarks>
    private async Task<bool> HasStashUntrackedAsync(string stash, CancellationToken ct) =>
        await this.ResolveAsync(stash + "^3", ct) is not null;

    private async Task<Oid> StashOidAsync(int index, CancellationToken ct)
    {
        var result = await this.TryReadAsync(["rev-parse", "--verify", "--quiet", "--end-of-options", StashRef(index)], ct: ct);
        return result.Success && Oid.TryParseHex(result.StdoutText.Trim(), out var oid)
            ? oid
            : throw new GitOperationException(GitErrorCode.NotFound, $"There is no stash entry {StashRef(index)}.");
    }

    public Task<byte[]?> ShowFileAtStashAsync(int index, GitPath path, CancellationToken ct = default) =>
        this.ShowFileAsync(StashRef(index), path, ct);

    private static string StashRef(int index) =>
        index >= 0 ? $"stash@{{{index}}}" : throw new ArgumentOutOfRangeException(nameof(index));

    // A stash commit's first parent is the commit it was made on; diffing
    // against it is what "git stash show" does, without stash.showPatch &co.
    private static string[] StashRange(string stash) => ["--end-of-options", stash + "^1", stash];

    internal static byte[] Lines(IEnumerable<string> lines) => Encoding.UTF8.GetBytes(string.Concat(lines.Select(l => l + "\n")));
}
