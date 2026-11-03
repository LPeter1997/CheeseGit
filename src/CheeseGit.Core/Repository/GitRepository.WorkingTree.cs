using System.Text;
using CheeseGit.Core.Diff;
using CheeseGit.Core.Git;

namespace CheeseGit.Core.Repository;

/// <summary>Diffs, staging, discarding and committing.</summary>
public sealed partial class GitRepository
{
    // Larger untracked files are not read just to count their lines.
    private const long MaxUntrackedLineCountBytes = 8 << 20;

    private static readonly string[] PathspecStdin = ["--pathspec-from-file=-", "--pathspec-file-nul"];

    /// <summary>
    /// The diff of one file in the index (staged) or working tree (unstaged).
    /// An untracked file diffs as a new file. Returns an empty diff when the
    /// file has no changes in that area.
    /// </summary>
    public async Task<FileDiff> DiffFileAsync(GitPath path, DiffArea area, CancellationToken ct = default)
    {
        var (spec, literal) = path.ToPathspec();
        var result = await this.ReadAsync(
            "Diff file",
            area == DiffArea.Staged ? ["diff", .. DiffParser.PatchArgs, "--cached", "--", spec] : ["diff", .. DiffParser.PatchArgs, "--", spec],
            ct: ct,
            literalPathspecs: literal);
        // An unmerged path has no single index version: git prints a combined diff ("diff --cc")
        // or "* Unmerged path", which showed as an empty diff and hid the conflict markers. The
        // working tree against HEAD ("ours") shows them as added lines.
        if (area == DiffArea.Unstaged && IsUnmergedOutput(result.Stdout))
        {
            var ours = await this.ReadAsync("Diff conflicted file", ["diff", .. DiffParser.PatchArgs, "HEAD", "--", spec], ct: ct, literalPathspecs: literal);
            return (PickDiff(DiffParser.Parse(ours.Stdout), path) ?? FileDiff.Empty(path)) with { IsConflicted = true };
        }

        var diff = PickDiff(DiffParser.Parse(result.Stdout), path);
        if (diff is not null) return diff;

        if (area == DiffArea.Unstaged && File.Exists(path.ToFullPath(this.Root)) && !await this.IsTrackedAsync(path, ct))
        {
            // --no-index exits with 1 when the files differ.
            var untracked = await this.TryReadAsync(["diff", .. DiffParser.PatchArgs, "--no-index", "--", "/dev/null", path.ToString()], ct: ct);
            if (untracked.ExitCode is 0 or 1) return DiffParser.Parse(untracked.Stdout).FirstOrDefault() ?? FileDiff.Empty(path);
            untracked.EnsureSuccess("Diff untracked file");
        }
        return FileDiff.Empty(path);
    }

    /// <summary>All file diffs of a commit against its first parent (or the empty tree for a root commit).</summary>
    public async Task<IReadOnlyList<FileDiff>> DiffCommitAsync(Oid commit, CancellationToken ct = default)
    {
        var result = await this.ReadAsync("Diff commit", [.. DiffTreeArgs(commit), .. DiffParser.PatchArgs, "-M"], ct: ct);
        return DiffParser.Parse(result.Stdout);
    }

    public async Task<FileDiff> DiffCommitFileAsync(Oid commit, GitPath path, CancellationToken ct = default)
    {
        var (spec, literal) = path.ToPathspec();
        var result = await this.ReadAsync("Diff commit file", [.. DiffTreeArgs(commit), .. DiffParser.PatchArgs, "--", spec], ct: ct, literalPathspecs: literal);
        return PickDiff(DiffParser.Parse(result.Stdout), path) ?? FileDiff.Empty(path);
    }

    /// <summary>Files changed by a commit against its first parent.</summary>
    public async Task<IReadOnlyList<FileChange>> ListCommitFilesAsync(Oid commit, CancellationToken ct = default)
    {
        var result = await this.ReadAsync("List commit files", [.. DiffTreeArgs(commit), "--name-status", "-z", "-M"], ct: ct);
        return await this.MarkLfsAsync(ParseNameStatus(result.Stdout), commit.ToHex(), ct);
    }

    public async Task<IReadOnlyList<FileStats>> CommitFileStatsAsync(Oid commit, CancellationToken ct = default)
    {
        var result = await this.ReadAsync("Read commit stats", [.. DiffTreeArgs(commit), "--numstat", "-z"], ct: ct);
        return ParseNumstat(result.Stdout);
    }

    /// <summary>
    /// Line totals per commit for many commits with one git process (the
    /// history view asks for visible rows only; eager stats for every commit
    /// made the old app slow).
    /// </summary>
    public async Task<IReadOnlyDictionary<Oid, CommitStats>> CommitStatsAsync(IEnumerable<Oid> commits, CancellationToken ct = default)
    {
        var stdin = Encoding.ASCII.GetBytes(string.Concat(commits.Select(c => c.ToHex() + "\n")));
        var stats = new Dictionary<Oid, CommitStats>();
        if (stdin.Length == 0) return stats;
        var result = await this.ReadAsync(
            "Read commit stats",
            ["diff-tree", "--stdin", "-r", "--root", "--diff-merges=first-parent", "--no-renames", "--numstat", "-z"],
            stdin,
            ct);

        // "<oid>\0" starts a commit; "<add>\t<del>\t<path>\0" records follow.
        var records = new Records(result.Stdout, 0);
        Oid? current = null;
        int ins = 0, del = 0, files = 0;
        while (records.Next(out var record))
        {
            if (record.IndexOf((byte)'\t') < 0 && Oid.TryParseHex(record, out var oid))
            {
                if (current is { } c) stats[c] = new CommitStats(ins, del, files);
                (current, ins, del, files) = (oid, 0, 0, 0);
                continue;
            }
            if (ParseNumstatCounts(record, out var a, out var d, out _))
            {
                ins += a;
                del += d;
                files++;
            }
        }
        if (current is { } last) stats[last] = new CommitStats(ins, del, files);
        foreach (var c in commits) stats.TryAdd(c, new CommitStats(0, 0, 0)); // empty commits print nothing
        return stats;
    }

    /// <summary>Per-file line counts of staged or unstaged changes. Unstaged includes untracked files (all lines added).</summary>
    /// <param name="untracked">
    /// The untracked files to count, when the caller already has a status
    /// (the session snapshot). Null reads the status again. Every file is read
    /// to count its lines, so callers cap this for huge untracked sets.
    /// </param>
    public async Task<IReadOnlyList<FileStats>> DiffStatsAsync(DiffArea area, IEnumerable<GitPath>? untracked = null, CancellationToken ct = default)
    {
        // --diff-filter=u: during a conflict, numstat lists each unmerged path
        // twice in the worktree (a 0/0 placeholder, then the diff against
        // ours) and as a fake 0/0 change in the index. Excluding them keeps
        // one real entry per path.
        List<string> args = ["diff", "--numstat", "-z", "--no-renames", "--no-ext-diff", "--diff-filter=u"];
        if (area == DiffArea.Staged) args.Add("--cached");
        var result = await this.ReadAsync("Read diff stats", args, ct: ct);
        var stats = ParseNumstat(result.Stdout).ToList();
        if (area == DiffArea.Unstaged)
        {
            untracked ??= (await this.StatusAsync(ct)).Entries.Where(e => e.Kind == EntryKind.Untracked).Select(e => e.Path);
            var paths = untracked.ToList();
            stats.AddRange(await Task.Run(() => paths.Select(this.CountUntracked).ToList(), ct));
        }
        return stats;
    }

    /// <summary>
    /// A file's raw content at a revision ("HEAD", a commit id, "stash@{0}", ""
    /// for the index), or null if it does not exist there. No filters are applied.
    /// </summary>
    public Task<byte[]?> ShowFileAsync(string revision, GitPath path, CancellationToken ct = default)
    {
        // cat-file --batch -z needs git 2.36; the minimum is 2.34.
        if (path.Bytes.Contains((byte)'\n')) throw new GitOperationException(GitErrorCode.Unsupported, "Paths containing a newline cannot be read from history.");
        return Task.Run(() => this.CatFile.Read(revision, path) is { Type: "blob" } blob ? blob.Content : null, ct);
    }

    /// <summary>
    /// One side of a file's diff as raw bytes, for showing what a text diff cannot (an image).
    /// The old side, and the new side of the index or a commit, are read by their blob id;
    /// <paramref name="newFromWorkingTree"/> reads the new side from disk (unstaged and untracked
    /// changes, whose blob is not in the object database). Null when the side does not exist;
    /// <see cref="FileContent.Bytes"/> is null when it is larger than <paramref name="maxBytes"/>.
    /// A Git LFS pointer is resolved to the real content in the local LFS store (see
    /// <see cref="FileContent.Lfs"/>); one that was never downloaded comes back missing.
    /// </summary>
    public Task<FileContent?> ReadDiffSideAsync(FileDiff diff, DiffSide side, bool newFromWorkingTree, long maxBytes, CancellationToken ct = default) =>
        Task.Run(async () =>
        {
            // Read at least a pointer's worth, so an LFS pointer is seen whatever the caller's limit.
            var limit = Math.Max(maxBytes, LfsPointer.MaxSize);
            FileContent content;
            if (side == DiffSide.New && newFromWorkingTree)
            {
                if (diff.Change == DiffChange.Deleted) return null;
                var file = new FileInfo(diff.Path.ToFullPath(this.Root));
                if (!file.Exists) return null;
                // Usually the real file (LFS checks it out); a pointer when git-lfs is missing or skipped the checkout.
                content = new FileContent(file.Length, file.Length <= limit ? await File.ReadAllBytesAsync(file.FullName, ct) : null);
            }
            else
            {
                if ((side == DiffSide.Old ? diff.OldBlob : diff.NewBlob) is not { } blob) return null;
                if (this.CatFile.Read(blob, limit) is not ({ Type: "blob" } obj, var size)) return null;
                content = new FileContent(size, size <= limit ? obj.Content : null);
            }
            if (content.Bytes is { } bytes && LfsPointer.TryParse(bytes) is { } pointer) return await this.ReadLfsObjectAsync(pointer, maxBytes, ct);
            return content.Size > maxBytes ? content with { Bytes = null } : content;
        }, ct);

    public async Task StageAsync(IEnumerable<GitPath> paths, CancellationToken ct = default)
    {
        var list = paths.ToList();
        if (list.Count == 0) return;
        if (list.Count > PathspecCover.Threshold) list = Cover(list, await this.StatusAsync(ct));
        // "add <path>" also stages deletions of tracked files.
        await this.WriteAsync("Stage files", ["add", .. PathspecStdin], GitPath.ToNulSeparated(list), ct);
    }

    /// <summary>See <see cref="PathspecCover"/>.</summary>
    private static List<GitPath> Cover(List<GitPath> paths, RepoStatus status) =>
        PathspecCover.Cover(paths, status.Entries.SelectMany(e => e.OriginalPath is { } from ? [e.Path, from] : new[] { e.Path }));

    public async Task UnstageAsync(IEnumerable<GitPath> paths, CancellationToken ct = default)
    {
        var list = paths.ToList();
        if (list.Count == 0) return;
        // A staged rename must be unstaged together with its source path.
        var status = await this.StatusAsync(ct);
        var set = list.ToHashSet();
        foreach (var e in status.Entries)
        {
            if (e.OriginalPath is { } original && set.Contains(e.Path) && set.Add(original)) list.Add(original);
        }
        if (list.Count > PathspecCover.Threshold) list = Cover(list, status);
        var stdin = GitPath.ToNulSeparated(list);
        if (status.Branch.IsUnborn)
        {
            // No HEAD to reset to: unstaging means removing from the index.
            await this.WriteAsync("Unstage files", ["rm", "-q", "--cached", "-r", "--ignore-unmatch", .. PathspecStdin], stdin, ct);
            return;
        }
        await this.WriteAsync("Unstage files", ["reset", "-q", .. PathspecStdin], stdin, ct);
    }

    /// <summary>
    /// Discards working-tree changes: tracked files go back to their index
    /// state, untracked files are deleted.
    /// </summary>
    public async Task DiscardUnstagedAsync(IEnumerable<GitPath> paths, CancellationToken ct = default)
    {
        var list = paths.ToList();
        if (list.Count == 0) return;
        var status = await this.StatusAsync(ct);
        var untracked = status.Entries.Where(e => e.Kind == EntryKind.Untracked).Select(e => e.Path).ToHashSet();
        var tracked = list.Where(p => !untracked.Contains(p)).ToList();
        if (tracked.Count > PathspecCover.Threshold) tracked = Cover(tracked, status);
        if (tracked.Count > 0)
        {
            await this.WriteAsync("Discard changes", ["checkout", "-q", .. PathspecStdin], GitPath.ToNulSeparated(tracked), ct);
        }
        foreach (var path in list.Where(untracked.Contains))
        {
            var full = path.ToFullPath(this.Root);
            if (File.Exists(full)) File.Delete(full);
        }
    }

    /// <summary>Discards staged changes and the files' working-tree changes (a staged new file is deleted).</summary>
    public async Task DiscardStagedAsync(IEnumerable<GitPath> paths, CancellationToken ct = default)
    {
        var list = paths.ToList();
        await this.UnstageAsync(list, ct);
        await this.DiscardUnstagedAsync(list, ct);
    }

    /// <summary>Stages the selected lines of <paramref name="diff"/> (an unstaged diff from <see cref="DiffFileAsync"/>).</summary>
    public Task StageLinesAsync(FileDiff diff, IEnumerable<LineSelection> selections, CancellationToken ct = default) =>
        this.ApplyAsync("Stage lines", LinePatch(diff, selections, PatchBase.OldSide), ["--cached"], ct);

    /// <summary>Unstages the selected lines of <paramref name="diff"/> (a staged diff).</summary>
    public Task UnstageLinesAsync(FileDiff diff, IEnumerable<LineSelection> selections, CancellationToken ct = default) =>
        this.ApplyAsync("Unstage lines", LinePatch(diff, selections, PatchBase.NewSide), ["--cached", "--reverse"], ct);

    /// <summary>
    /// Discards the selected lines. For an unstaged diff the working tree
    /// changes; for a staged diff both the index and the working tree do.
    /// </summary>
    public async Task DiscardLinesAsync(FileDiff diff, IEnumerable<LineSelection> selections, DiffArea area, CancellationToken ct = default)
    {
        var patch = LinePatch(diff, selections, PatchBase.NewSide);
        if (area == DiffArea.Staged)
        {
            // Check both targets first so a failure leaves nothing half-applied.
            await this.ApplyAsync("Discard lines", patch, ["--cached", "--reverse", "--check"], ct);
            await this.ApplyAsync("Discard lines", patch, ["--reverse", "--check"], ct);
            await this.ApplyAsync("Discard lines", patch, ["--cached", "--reverse"], ct);
        }
        await this.ApplyAsync("Discard lines", patch, ["--reverse"], ct);
    }

    /// <summary>
    /// The partial patch of the selected lines. Refused for a diff between Git LFS pointers: a mix of
    /// two pointers' lines (the new oid with the old size) names no object, and the index would hold a
    /// broken pointer. Such a file is staged, unstaged or discarded whole.
    /// </summary>
    private static byte[]? LinePatch(FileDiff diff, IEnumerable<LineSelection> selections, PatchBase @base)
    {
        if (LfsPointer.IsPointerDiff(diff))
        {
            throw new GitOperationException(GitErrorCode.Unsupported, $"{diff.Path} is a Git LFS file: stage, unstage or discard the whole file instead of lines.");
        }
        return PartialPatch.Build(diff, selections, @base);
    }

    private async Task ApplyAsync(string operation, byte[]? patch, string[] args, CancellationToken ct)
    {
        if (patch is null) throw new GitOperationException(GitErrorCode.NothingSelected, "No changed lines are selected.");
        // --whitespace=nowarn: apply.whitespace=error in the user's config
        // must not reject their own lines. --unidiff-zero: diff.context=0.
        await this.WriteAsync(operation, ["apply", "--whitespace=nowarn", "--unidiff-zero", .. args], patch, ct);
    }

    /// <summary>Commits the index. Returns the new commit.</summary>
    public async Task<Oid> CommitAsync(string summary, string description = "", bool allowEmpty = false, CancellationToken ct = default)
    {
        var message = description.Length > 0 ? $"{summary}\n\n{description}" : summary;
        // The message goes through stdin: no quoting, no option injection.
        List<string> args = ["commit", "-q", "-F", "-"];
        if (allowEmpty) args.Add("--allow-empty");
        await this.WriteAsync("Commit", args, Encoding.UTF8.GetBytes(message), ct);
        return await this.ResolveAsync("HEAD", ct) ?? throw new GitOperationException(GitErrorCode.NotFound, "HEAD does not exist after committing.");
    }

    /// <summary>
    /// Moves the current branch back one commit, keeping that commit's changes
    /// staged, and returns its message for the commit form. Refused while
    /// browsing history, for the initial commit, and once the commit is on a
    /// configured remote.
    /// </summary>
    public async Task<RestoredCommitMessage> UndoLastCommitAsync(CancellationToken ct = default)
    {
        if (await this.CurrentBranchAsync(ct) is null)
        {
            throw new GitOperationException(GitErrorCode.DetachedHead, "Cannot undo a commit while browsing history.");
        }
        var head = await this.ResolveAsync("HEAD", ct) ?? throw new GitOperationException(GitErrorCode.InitialCommit, "There is no commit to undo.");
        var commit = this.GetCommit(head) ?? throw new GitOperationException(GitErrorCode.NotFound, "Cannot read HEAD.");
        if (commit.Parents.Count == 0) throw new GitOperationException(GitErrorCode.InitialCommit, "Cannot undo the initial commit.");

        // Only configured remotes count: refs of removed remotes may linger.
        var remotes = await this.ListRemotesAsync(ct);
        if (remotes.Count > 0)
        {
            var pushed = await this.ReadAsync(
                "Check whether the commit is pushed",
                ["for-each-ref", "--contains", "HEAD", "--count=1", "--format=%(refname)", .. remotes.Select(r => $"refs/remotes/{r.Name}/")],
                ct: ct);
            if (pushed.Stdout.Length > 0) throw new GitOperationException(GitErrorCode.AlreadyPushed, "Cannot undo because the latest commit is already pushed.");
        }

        await this.WriteAsync("Undo commit", ["reset", "-q", "--soft", commit.Parents[0].ToHex()], ct: ct);
        var message = commit.Message.TrimEnd('\n');
        var newline = message.IndexOf('\n');
        return newline < 0
            ? new RestoredCommitMessage(message.Trim(), "")
            : new RestoredCommitMessage(message[..newline].Trim(), message[(newline + 1)..].TrimStart('\n'));
    }

    /// <summary>Reads one commit's metadata (through the persistent cat-file process), or null.</summary>
    public CommitInfo? GetCommit(Oid oid) =>
        this.CatFile.Read(oid) is { Type: "commit" } obj ? CommitParser.Parse(oid, obj.Content) : null;

    /// <summary>The current branch's latest commits, newest first. Empty for an unborn branch.</summary>
    public async Task<IReadOnlyList<CommitInfo>> CommitLogAsync(int limit, CancellationToken ct = default)
    {
        if (await this.ResolveAsync("HEAD", ct) is null) return [];
        var result = await this.ReadAsync("Read log", ["rev-list", $"--max-count={limit}", "HEAD", "--"], ct: ct);
        var commits = new List<CommitInfo>();
        var lines = new Records(result.Stdout, (byte)'\n');
        while (lines.Next(out var line))
        {
            if (Oid.TryParseHex(line, out var oid) && this.GetCommit(oid) is { } commit) commits.Add(commit);
        }
        return commits;
    }

    internal async Task<bool> IsTrackedAsync(GitPath path, CancellationToken ct = default)
    {
        // ls-files has no --pathspec-from-file; one path on the command line is safe.
        var (spec, literal) = path.ToPathspec();
        var result = await this.ReadAsync("Check tracked", ["ls-files", "-z", "--cached", "--", spec], ct: ct, literalPathspecs: literal);
        var records = new Records(result.Stdout, 0);
        while (records.Next(out var record))
        {
            if (record.SequenceEqual(path.Bytes)) return true;
        }
        return false;
    }

    /// <summary>The diff for <paramref name="path"/>; a glob pathspec may have matched others too.</summary>
    private static bool IsUnmergedOutput(ReadOnlySpan<byte> stdout) =>
        stdout.StartsWith("diff --cc "u8) || stdout.StartsWith("* Unmerged path "u8);

    internal static FileDiff? PickDiff(IReadOnlyList<FileDiff> diffs, GitPath path) =>
        diffs.FirstOrDefault(d => d.Path == path) ?? diffs.FirstOrDefault(d => d.OldPath == path);

    private FileStats CountUntracked(GitPath path)
    {
        try
        {
            var info = new FileInfo(path.ToFullPath(this.Root));
            if (!info.Exists || info.Length > MaxUntrackedLineCountBytes) return new FileStats(path, 0, 0);
            var bytes = File.ReadAllBytes(info.FullName);
            // git's binary heuristic: a NUL in the first 8000 bytes.
            if (bytes.AsSpan(0, Math.Min(bytes.Length, 8000)).IndexOf((byte)0) >= 0) return new FileStats(path, 0, 0, IsBinary: true);
            var lines = bytes.AsSpan().Count((byte)'\n') + (bytes.Length > 0 && bytes[^1] != (byte)'\n' ? 1 : 0);
            return new FileStats(path, lines, 0);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return new FileStats(path, 0, 0);
        }
    }

    private static string[] DiffTreeArgs(Oid commit) =>
        ["diff-tree", "-r", "--root", "--no-commit-id", "--diff-merges=first-parent", commit.ToHex()];

    /// <summary>Parses <c>--numstat -z</c>: "add\tdel\tpath\0", or "add\tdel\t\0old\0new\0" for renames.</summary>
    internal static List<FileStats> ParseNumstat(ReadOnlySpan<byte> output)
    {
        var stats = new List<FileStats>();
        var records = new Records(output, 0);
        while (records.Next(out var record))
        {
            if (!ParseNumstatCounts(record, out var add, out var del, out var path)) continue;
            var binary = record.StartsWith("-\t-\t"u8);
            if (path.IsEmpty)
            {
                records.Next(out _); // old path
                records.Next(out path);
            }
            stats.Add(new FileStats(GitPath.FromBytes(path), add, del, binary));
        }
        return stats;
    }

    private static bool ParseNumstatCounts(ReadOnlySpan<byte> record, out int add, out int del, out ReadOnlySpan<byte> path)
    {
        add = del = 0;
        path = default;
        var t1 = record.IndexOf((byte)'\t');
        if (t1 < 0) return false;
        var rest = record[(t1 + 1)..];
        var t2 = rest.IndexOf((byte)'\t');
        if (t2 < 0) return false;
        int.TryParse(record[..t1], out add); // "-" for binary files
        int.TryParse(rest[..t2], out del);
        path = rest[(t2 + 1)..];
        return true;
    }

    /// <summary>Parses <c>--name-status -z</c>: "M\0path\0", or "R100\0old\0new\0".</summary>
    internal static List<FileChange> ParseNameStatus(ReadOnlySpan<byte> output)
    {
        var changes = new List<FileChange>();
        var records = new Records(output, 0);
        while (records.Next(out var status))
        {
            if (status.IsEmpty || !records.Next(out var first)) continue;
            var kind = status[0];
            if (kind is (byte)'R' or (byte)'C')
            {
                records.Next(out var second);
                changes.Add(new FileChange(GitPath.FromBytes(second), kind == (byte)'R' ? FileStatus.Renamed : FileStatus.Copied, GitPath.FromBytes(first)));
                continue;
            }
            var fileStatus = kind switch
            {
                (byte)'A' => FileStatus.Added,
                (byte)'D' => FileStatus.Deleted,
                (byte)'T' => FileStatus.TypeChanged,
                (byte)'U' => FileStatus.Conflicted,
                _ => FileStatus.Modified,
            };
            changes.Add(new FileChange(GitPath.FromBytes(first), fileStatus));
        }
        return changes;
    }

    /// <summary>
    /// The subset of <paramref name="paths"/> that .gitignore rules exclude.
    /// Tracked files are never reported, and neither is a directory with
    /// tracked files in it ("dir/" form for directory-only patterns).
    /// </summary>
    public async Task<HashSet<GitPath>> CheckIgnoredAsync(IReadOnlyCollection<GitPath> paths, CancellationToken ct = default)
    {
        var ignored = new HashSet<GitPath>();
        // check-ignore runs without literal pathspecs, so a leading ":(top)"
        // would be parsed as magic. Such names are never reported as ignored.
        var query = paths.Where(p => p.Bytes is not [(byte)':', ..]).ToList();
        if (query.Count == 0) return ignored;
        var result = await this.TryReadAsync(["check-ignore", "-z", "--stdin"], GitPath.ToNulSeparated(query), ct);
        // Exit 1 means "nothing is ignored"; anything else but 0 is an error.
        if (result.ExitCode == 1) return ignored;
        result.EnsureSuccess("Check ignored paths");
        var records = new Records(result.Stdout, 0);
        while (records.Next(out var record)) ignored.Add(GitPath.FromBytes(record));
        return ignored;
    }
}
