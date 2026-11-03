using CheeseGit.Core.Git;

namespace CheeseGit.Core.Repository;

/// <summary>Status, HEAD, refs, branches and remotes (queries and branch mutations).</summary>
public sealed partial class GitRepository
{
    public async Task<RepoStatus> StatusAsync(CancellationToken ct = default)
    {
        var result = await this.ReadAsync("Read status", StatusParser.Args, ct: ct);
        var status = StatusParser.Parse(result.Stdout);
        var lfs = await this.LfsPathsAsync(status.Entries.Where(e => e.Kind != EntryKind.Ignored && !e.IsSubmodule).Select(e => e.Path).ToList(), ct: ct);
        return lfs.Count == 0 ? status : status with { Entries = status.Entries.Select(e => lfs.Contains(e.Path) ? e with { IsLfs = true } : e).ToList() };
    }

    public async Task<HeadState> HeadAsync(CancellationToken ct = default)
    {
        var head = await this.ResolveAsync("HEAD", ct);
        var symbolic = await this.TryReadAsync(["symbolic-ref", "--quiet", "HEAD"], ct: ct);
        if (symbolic.Success)
        {
            var full = symbolic.StdoutText.Trim();
            return new HeadState(full.StartsWith("refs/heads/", StringComparison.Ordinal) ? full["refs/heads/".Length..] : full, false, head);
        }

        // Detached: the most recently committed branch containing HEAD is the
        // one whose history is being browsed.
        var contains = await this.TryReadAsync(
            ["for-each-ref", "--contains", "HEAD", "--sort=-committerdate", "--count=1", "--format=%(refname)", "refs/heads/"], ct: ct);
        var branch = contains.Success ? contains.StdoutText.Trim() : "";
        return new HeadState(branch.Length > 0 ? branch["refs/heads/".Length..] : null, true, head);
    }

    /// <summary>The checked-out branch's short name, or null when HEAD is detached.</summary>
    public async Task<string?> CurrentBranchAsync(CancellationToken ct = default)
    {
        var symbolic = await this.TryReadAsync(["symbolic-ref", "--quiet", "--short", "HEAD"], ct: ct);
        return symbolic.Success ? symbolic.StdoutText.Trim() : null;
    }

    /// <summary>Every branch and tag, with tags peeled to their commit.</summary>
    public async Task<IReadOnlyList<RefInfo>> ListRefsAsync(CancellationToken ct = default)
    {
        var result = await this.ReadAsync(
            "List refs",
            ["for-each-ref", "--format=%(refname)%00%(objectname)%00%(*objectname)%00%(objecttype)", "refs/heads/", "refs/remotes/", "refs/tags/"],
            ct: ct);
        var refs = new List<RefInfo>();
        var lines = new Records(result.Stdout, (byte)'\n');
        while (lines.Next(out var line))
        {
            var f = Fields(line, 4);
            if (f is null) continue;
            var name = Utf8(f[0]);
            if (name.EndsWith("/HEAD", StringComparison.Ordinal) && name.StartsWith("refs/remotes/", StringComparison.Ordinal)) continue;
            // Peel annotated tags; skip tags of trees or blobs.
            var target = f[2].Length > 0 ? f[2] : f[1];
            if (Utf8(f[3]) is "tree" or "blob" || !Oid.TryParseHex(target, out var oid)) continue;
            var kind = name.StartsWith("refs/heads/", StringComparison.Ordinal) ? RefKind.LocalBranch
                : name.StartsWith("refs/remotes/", StringComparison.Ordinal) ? RefKind.RemoteBranch
                : name.StartsWith("refs/tags/", StringComparison.Ordinal) ? RefKind.Tag
                : RefKind.Other;
            refs.Add(new RefInfo(name, oid, kind));
        }
        return refs;
    }

    /// <summary>Local branches, most recently committed first.</summary>
    public async Task<IReadOnlyList<BranchInfo>> ListBranchesAsync(CancellationToken ct = default)
    {
        var result = await this.ReadAsync(
            "List branches",
            ["for-each-ref", "--sort=-committerdate", "--format=%(refname)%00%(HEAD)%00%(objectname)%00%(committerdate:unix)%00%(upstream:short)", "refs/heads/"],
            ct: ct);
        var branches = new List<BranchInfo>();
        var lines = new Records(result.Stdout, (byte)'\n');
        while (lines.Next(out var line))
        {
            var f = Fields(line, 5);
            if (f is null || !Oid.TryParseHex(f[2], out var oid)) continue;
            var upstream = Utf8(f[4]);
            branches.Add(new BranchInfo(Utf8(f[0])["refs/heads/".Length..], f[1].SequenceEqual("*"u8), oid, UnixTime(f[3]), upstream.Length > 0 ? upstream : null));
        }
        return branches;
    }

    /// <summary>Remote branches that have no local branch of the same name, most recent first.</summary>
    public async Task<IReadOnlyList<RemoteBranchInfo>> ListRemoteBranchesAsync(CancellationToken ct = default)
    {
        var remotes = (await this.ListRemotesAsync(ct)).Select(r => r.Name).OrderByDescending(n => n.Length).ToList();
        var locals = (await this.ListBranchesAsync(ct)).Select(b => b.Name).ToHashSet(StringComparer.Ordinal);
        var result = await this.ReadAsync(
            "List remote branches",
            ["for-each-ref", "--sort=-committerdate", "--format=%(refname)%00%(objectname)%00%(committerdate:unix)", "refs/remotes/"],
            ct: ct);
        var branches = new List<RemoteBranchInfo>();
        var lines = new Records(result.Stdout, (byte)'\n');
        while (lines.Next(out var line))
        {
            var f = Fields(line, 3);
            if (f is null || !Oid.TryParseHex(f[1], out var oid)) continue;
            var rest = Utf8(f[0])["refs/remotes/".Length..];
            // Remote names may contain '/', so match the longest configured remote.
            var remote = remotes.FirstOrDefault(r => rest.StartsWith(r + "/", StringComparison.Ordinal));
            if (remote is null) continue;
            var name = rest[(remote.Length + 1)..];
            if (name == "HEAD" || locals.Contains(name)) continue;
            branches.Add(new RemoteBranchInfo(remote, name, oid, UnixTime(f[2])));
        }
        return branches;
    }

    /// <summary>Configured remotes, "origin" first, then in config order.</summary>
    public async Task<IReadOnlyList<RemoteInfo>> ListRemotesAsync(CancellationToken ct = default)
    {
        // config -z: "key\nvalue\0". Remote names may contain dots, so strip
        // the known prefix and suffix instead of splitting.
        var result = await this.TryReadAsync(["config", "-z", "--get-regexp", @"^remote\..*\.url$"], ct: ct);
        var remotes = new List<RemoteInfo>();
        if (!result.Success) return remotes; // exit 1: no remotes
        var records = new Records(result.Stdout, 0);
        while (records.Next(out var record))
        {
            var nl = record.IndexOf((byte)'\n');
            if (nl < 0) continue;
            var key = Utf8(record[..nl]);
            var name = key["remote.".Length..^".url".Length];
            if (remotes.All(r => r.Name != name)) remotes.Add(new RemoteInfo(name, Utf8(record[(nl + 1)..])));
        }
        return [.. remotes.OrderBy(r => r.Name == "origin" ? 0 : 1)];
    }

    /// <summary>Ahead/behind of the current branch against its upstream, or null without one.</summary>
    public async Task<TrackingStatus?> TrackingStatusAsync(CancellationToken ct = default)
    {
        var branch = (await this.StatusAsync(ct)).Branch;
        return branch is { Upstream: { } upstream, Ahead: { } ahead, Behind: { } behind } ? new TrackingStatus(ahead, behind, upstream) : null;
    }

    /// <summary>
    /// Ahead/behind of the current branch against the branch of the same name
    /// on <paramref name="remote"/>, or null when that branch does not exist
    /// there (the branch is unpublished).
    /// </summary>
    public async Task<TrackingStatus?> RemoteBranchStatusAsync(string remote, CancellationToken ct = default)
    {
        var branch = await this.CurrentBranchAsync(ct);
        if (branch is null) return null;
        var remoteRef = $"refs/remotes/{remote}/{branch}";
        if (!await this.RefExistsAsync(remoteRef, ct)) return null;
        var counts = await this.ReadAsync("Count ahead/behind", ["rev-list", "--left-right", "--count", "--end-of-options", $"HEAD...{remoteRef}"], ct: ct);
        var parts = counts.StdoutText.Trim().Split('\t');
        return new TrackingStatus(int.Parse(parts[0]), int.Parse(parts[1]), $"{remote}/{branch}");
    }

    public async Task<BranchDeleteInfo> BranchDeleteInfoAsync(string branch, CancellationToken ct = default)
    {
        var result = await this.ReadAsync(
            "Read upstream",
            ["for-each-ref", "--format=%(upstream:remotename)%00%(upstream:remoteref)", "refs/heads/" + branch],
            ct: ct);
        var f = Fields(result.Stdout.AsSpan().TrimEnd((byte)'\n'), 2);
        var remote = f is null ? "" : Utf8(f[0]);
        var remoteRef = f is null ? "" : Utf8(f[1]);
        if (remote.Length == 0 || !remoteRef.StartsWith("refs/heads/", StringComparison.Ordinal)) return new BranchDeleteInfo(false, null, null);

        // Asks the remote itself: the local remote-tracking ref may be stale.
        var ls = await this.TryReadAsync(["ls-remote", "--heads", "--end-of-options", remote, remoteRef], ct: ct);
        return new BranchDeleteInfo(ls.Success && ls.Stdout.Length > 0, remote, remoteRef["refs/heads/".Length..]);
    }

    /// <summary>
    /// Commits reachable from the branch and from no other branch or remote-tracking branch:
    /// what a forced delete would leave reachable only from the reflog.
    /// </summary>
    public async Task<int> UniqueCommitCountAsync(string branch, CancellationToken ct = default)
    {
        await this.ValidateBranchNameAsync(branch, ct);
        // --exclude patterns are relative to --branches ("x", not "refs/heads/x"); branch names cannot hold glob characters.
        var result = await this.ReadAsync(
            "Count unmerged commits",
            ["rev-list", "--count", "refs/heads/" + branch, "--not", $"--exclude={branch}", "--branches", "--remotes", "--"],
            ct: ct);
        return int.TryParse(result.StdoutText.Trim(), out var count) ? count : 0;
    }

    public async Task SwitchBranchAsync(string branch, CancellationToken ct = default)
    {
        await this.ValidateBranchNameAsync(branch, ct);
        // --guess: a branch that exists only on one remote is created and tracked (the branch list offers those).
        await this.WriteAsync("Switch branch", ["switch", "-q", "--guess", "--end-of-options", branch], ct: ct);
    }

    /// <summary>
    /// Creates a local branch tracking <paramref name="remote"/>/<paramref name="branch"/> and switches to it.
    /// Unlike <c>switch --guess</c>, this works when several remotes have the branch.
    /// </summary>
    public async Task CheckoutRemoteBranchAsync(string remote, string branch, CancellationToken ct = default)
    {
        ValidateRemoteName(remote);
        await this.ValidateBranchNameAsync(branch, ct);
        await this.WriteAsync("Check out remote branch", ["switch", "-q", "--track", "-c", branch, "--end-of-options", $"refs/remotes/{remote}/{branch}"], ct: ct);
    }

    /// <summary>Creates <paramref name="branch"/> at <paramref name="startPoint"/> (default HEAD) and switches to it.</summary>
    public async Task CreateBranchAsync(string branch, string? startPoint = null, CancellationToken ct = default)
    {
        await this.ValidateBranchNameAsync(branch, ct);
        List<string> args = ["switch", "-q", "--no-track", "-c", branch];
        if (startPoint is not null) args.AddRange(["--end-of-options", startPoint]);
        await this.WriteAsync("Create branch", args, ct: ct);
    }

    public async Task DeleteBranchAsync(string branch, bool force, CancellationToken ct = default)
    {
        await this.ValidateBranchNameAsync(branch, ct);
        var result = await this.TryWriteAsync(["branch", force ? "-D" : "-d", "--end-of-options", branch], ct: ct);
        if (result.Success) return;
        // Classify by state: an existing branch whose tip is not merged is
        // the one failure the UI handles (by offering to force).
        if (!force && await this.RefExistsAsync("refs/heads/" + branch, ct)
            && !(await this.TryReadAsync(["merge-base", "--is-ancestor", "--end-of-options", "refs/heads/" + branch, "HEAD"], ct: ct)).Success)
        {
            throw new GitOperationException(GitErrorCode.NotFullyMerged, $"Branch '{branch}' is not fully merged.", new GitCommandException("Delete branch", result));
        }
        result.EnsureSuccess("Delete branch");
    }

    public async Task DeleteRemoteBranchAsync(string remote, string branch, CancellationToken ct = default)
    {
        ValidateRemoteName(remote);
        await this.ValidateBranchNameAsync(branch, ct);
        await this.WriteAsync("Delete remote branch", ["push", "--end-of-options", remote, ":refs/heads/" + branch], ct: ct);
    }

    /// <summary>Checks out a commit without a branch (history browsing). Fails if local changes would be overwritten.</summary>
    public async Task CheckoutCommitAsync(Oid commit, CancellationToken ct = default)
    {
        await this.WriteAsync("Check out commit", ["switch", "-q", "--detach", "--end-of-options", commit.ToHex()], ct: ct);
    }

    /// <summary>Splits a NUL-separated record into exactly <paramref name="count"/> fields, or null.</summary>
    internal static byte[][]? Fields(ReadOnlySpan<byte> line, int count)
    {
        var fields = new byte[count][];
        for (var i = 0; i < count; i++)
        {
            var nul = line.IndexOf((byte)0);
            if (nul < 0 && i < count - 1) return null;
            fields[i] = (nul < 0 ? line : line[..nul]).ToArray();
            line = nul < 0 ? [] : line[(nul + 1)..];
        }
        return fields;
    }

    internal static DateTimeOffset UnixTime(ReadOnlySpan<byte> text) =>
        long.TryParse(text, out var seconds) ? DateTimeOffset.FromUnixTimeSeconds(seconds) : DateTimeOffset.UnixEpoch;
}
