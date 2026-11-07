using System.Text;
using CheeseGit.Core.Git;

namespace CheeseGit.Core.Repository;

/// <summary>
/// Merge, revert, cherry-pick and conflict resolution. Outcomes come from
/// exit codes, HEAD, the state files git leaves (MERGE_HEAD, REVERT_HEAD,
/// ...) and unmerged index entries; never from "CONFLICT" in git's output,
/// which the old app scraped.
/// </summary>
public sealed partial class GitRepository
{
    /// <summary>
    /// Merges a branch ("feature", "origin/feature" or a full ref) into the
    /// current branch.
    /// </summary>
    public async Task<MergeResult> MergeAsync(string branch, CancellationToken ct = default)
    {
        await this.RequireBranchAsync("merge", ct);
        var source = await this.ResolveBranchRefAsync(branch, ct);
        var before = await this.ResolveAsync("HEAD", ct);
        var message = await this.MergeMessageAsync(source, branch, ct);
        var result = await this.TryWriteAsync(["merge", "--no-edit", "-m", message, "--end-of-options", source], ct: ct);
        var after = await this.ResolveAsync("HEAD", ct);

        if (result.Success)
        {
            if (before == after) return new MergeResult.AlreadyUpToDate();
            return new MergeResult.Success(await this.CountCommitsAsync(before, (await this.ResolveAsync(source, ct))!.Value, ct));
        }
        if (await this.StateFileExistsAsync("MERGE_HEAD", ct) && (await this.ConflictedFilesAsync(ct)).Count > 0)
        {
            return new MergeResult.Conflict(new MergeConflictInfo(ShortBranchName(source), await this.ConflictedFilesAsync(ct)));
        }
        result.EnsureSuccess("Merge");
        throw new InvalidOperationException("unreachable");
    }

    /// <summary>
    /// The message <c>git merge</c> would write for <paramref name="branch"/>. git names the merge
    /// after its argument, and the argument is a full ref name (never ambiguous), which gave
    /// "Merge branch 'refs/heads/x'". <c>fmt-merge-msg</c> is what <c>git merge</c> uses inside, so
    /// merge.log, merge.suppressDest and the "into …" part stay git's.
    /// </summary>
    private async Task<string> MergeMessageAsync(string source, string branch, CancellationToken ct)
    {
        var oid = await this.ResolveAsync(source, ct) ?? throw new GitOperationException(GitErrorCode.NotFound, $"Branch '{branch}' does not exist.");
        var what = source.StartsWith("refs/heads/", StringComparison.Ordinal) ? $"branch '{ShortBranchName(source)}'"
            : source.StartsWith("refs/remotes/", StringComparison.Ordinal) ? $"remote-tracking branch '{ShortBranchName(source)}'"
            : $"commit '{branch}'";
        var result = await this.ReadAsync("Merge message", ["fmt-merge-msg"], Encoding.UTF8.GetBytes($"{oid.ToHex()}\t\t{what} of .\n"), ct);
        return result.StdoutText.Trim();
    }

    public async Task MergeAbortAsync(CancellationToken ct = default)
    {
        if (!await this.StateFileExistsAsync("MERGE_HEAD", ct)) throw new GitOperationException(GitErrorCode.NotInProgress, "No merge is in progress.");
        await this.WriteAsync("Abort merge", ["merge", "--abort"], ct: ct);
    }

    /// <summary>Creates the merge commit once every conflict is resolved. Returns the number of commits merged in.</summary>
    public async Task<int> MergeContinueAsync(string message, CancellationToken ct = default)
    {
        if (!await this.StateFileExistsAsync("MERGE_HEAD", ct)) throw new GitOperationException(GitErrorCode.NotInProgress, "No merge is in progress.");
        var before = await this.ResolveAsync("HEAD", ct);
        var incoming = await this.ResolveAsync("MERGE_HEAD", ct);
        await this.WriteAsync("Complete merge", ["commit", "-q", "-F", "-"], Encoding.UTF8.GetBytes(message), ct);
        return incoming is { } i ? await this.CountCommitsAsync(before, i, ct) : 0;
    }

    /// <summary>Which operation is paused, read from the git dir.</summary>
    public async Task<OperationState> OperationStateAsync(CancellationToken ct = default)
    {
        foreach (var (file, kind) in new[] { ("MERGE_HEAD", OperationKind.Merge), ("REVERT_HEAD", OperationKind.Revert), ("CHERRY_PICK_HEAD", OperationKind.CherryPick) })
        {
            var path = await this.GitPathAsync(file, ct);
            if (!File.Exists(path)) continue;
            var conflicts = (await this.ConflictedFilesAsync(ct)).Count;
            return new OperationState(kind, await this.DescribeIncomingAsync(path, kind, ct), conflicts);
        }
        foreach (var name in (string[])["rebase-merge", "rebase-apply"])
        {
            var dir = await this.GitPathAsync(name, ct);
            if (!Directory.Exists(dir)) continue;
            // head-name is the branch being rebased ("detached HEAD" when there is none).
            var headName = Path.Combine(dir, "head-name");
            var branch = File.Exists(headName) ? (await File.ReadAllTextAsync(headName, ct)).Trim() : "";
            branch = branch.StartsWith("refs/heads/", StringComparison.Ordinal) ? branch["refs/heads/".Length..] : "";
            return new OperationState(OperationKind.Rebase, branch, (await this.ConflictedFilesAsync(ct)).Count);
        }
        return OperationState.None;
    }

    /// <summary>Stops a paused rebase and puts the branch back where it was.</summary>
    public async Task RebaseAbortAsync(CancellationToken ct = default)
    {
        if ((await this.OperationStateAsync(ct)).Kind != OperationKind.Rebase) throw new GitOperationException(GitErrorCode.NotInProgress, "No rebase is in progress.");
        await this.WriteAsync("Abort rebase", ["rebase", "--abort"], ct: ct);
    }

    /// <summary>
    /// Continues a paused rebase once its conflicts are staged (GIT_EDITOR is ":", so commit
    /// messages stay as they were). A commit whose resolution left nothing to commit is skipped,
    /// as cherry-pick does; git would stop with "No changes - did you forget to use 'git add'?".
    /// </summary>
    public async Task RebaseContinueAsync(CancellationToken ct = default)
    {
        if ((await this.OperationStateAsync(ct)).Kind != OperationKind.Rebase) throw new GitOperationException(GitErrorCode.NotInProgress, "No rebase is in progress.");
        if ((await this.ConflictedFilesAsync(ct)).Count > 0) throw new GitOperationException(GitErrorCode.UnresolvedConflicts, "Resolve and stage every conflicted file first.");
        var result = await this.TryWriteAsync(["rebase", "--continue"], ct: ct);
        if (result.Success) return;
        var nothingStaged = (await this.TryReadAsync(["diff", "--cached", "--quiet", "HEAD", "--"], ct: ct)).ExitCode == 0;
        var stillPaused = (await this.OperationStateAsync(ct)) is { Kind: OperationKind.Rebase, ConflictCount: 0 };
        if (nothingStaged && stillPaused)
        {
            result = await this.TryWriteAsync(["rebase", "--skip"], ct: ct);
            if (result.Success) return;
        }
        // Stopped on a later commit's conflicts: the paused state says so; anything else is an error.
        if ((await this.OperationStateAsync(ct)) is { Kind: OperationKind.Rebase, ConflictCount: > 0 }) return;
        result.EnsureSuccess("Continue rebase");
    }

    /// <summary>The paused merge or revert and its conflicted files; empty when none is in progress.</summary>
    public async Task<MergeConflictInfo> MergeConflictsAsync(CancellationToken ct = default)
    {
        var state = await this.OperationStateAsync(ct);
        if (state.Kind is not (OperationKind.Merge or OperationKind.Revert)) return new MergeConflictInfo("", []);
        return new MergeConflictInfo(state.Incoming, await this.ConflictedFilesAsync(ct));
    }

    /// <summary>Paths with unmerged index entries.</summary>
    public async Task<IReadOnlyList<GitPath>> ConflictedFilesAsync(CancellationToken ct = default)
    {
        // "<mode> <oid> <stage>\t<path>\0" for every stage of every unmerged path.
        var result = await this.ReadAsync("List conflicts", ["ls-files", "--unmerged", "-z"], ct: ct);
        var paths = new List<GitPath>();
        var records = new Records(result.Stdout, 0);
        while (records.Next(out var record))
        {
            var tab = record.IndexOf((byte)'\t');
            if (tab < 0) continue;
            var path = GitPath.FromBytes(record[(tab + 1)..]);
            if (paths.Count == 0 || paths[^1] != path) paths.Add(path);
        }
        return paths;
    }

    /// <summary>
    /// Number of conflict regions (lines starting with "&lt;&lt;&lt;&lt;&lt;&lt;&lt; ") per conflicted file, and
    /// whether the file is an LFS or binary file (judged by its current and incoming versions).
    /// </summary>
    public async Task<IReadOnlyList<FileConflictInfo>> ConflictCountsAsync(CancellationToken ct = default)
    {
        var infos = new List<FileConflictInfo>();
        foreach (var (path, stages) in await this.UnmergedStagesAsync(ct))
        {
            var (lfs, binary) = await Task.Run(() => this.ClassifyStages(stages), ct);
            var count = 0;
            var full = path.ToFullPath(this.Root);
            if (!lfs && File.Exists(full))
            {
                foreach (var line in File.ReadLines(full))
                {
                    if (line.StartsWith("<<<<<<< ", StringComparison.Ordinal) || line == "<<<<<<<") count++;
                }
            }
            infos.Add(new FileConflictInfo(path, count) { IsLfs = lfs, IsBinary = binary });
        }
        return infos;
    }

    /// <summary>Each unmerged path with its stage blobs (1 base, 2 current, 3 incoming), in index order.</summary>
    private async Task<List<(GitPath Path, Dictionary<int, Oid> Stages)>> UnmergedStagesAsync(CancellationToken ct)
    {
        // "<mode> <oid> <stage>\t<path>\0" for every stage of every unmerged path.
        var result = await this.ReadAsync("List conflicts", ["ls-files", "--unmerged", "-z"], ct: ct);
        var entries = new List<(GitPath, Dictionary<int, Oid>)>();
        var records = new Records(result.Stdout, 0);
        while (records.Next(out var record))
        {
            var tab = record.IndexOf((byte)'\t');
            if (tab < 0) continue;
            var fields = record[..tab];
            var path = GitPath.FromBytes(record[(tab + 1)..]);
            if (entries.Count == 0 || entries[^1].Item1 != path) entries.Add((path, []));
            var first = fields.IndexOf((byte)' ');
            var second = fields.LastIndexOf((byte)' ');
            if (first < 0 || second <= first) continue;
            if (Oid.TryParseHex(fields[(first + 1)..second], out var oid) && fields[(second + 1)..] is [var stage] && stage is >= (byte)'1' and <= (byte)'3')
            {
                entries[^1].Item2[stage - '0'] = oid;
            }
        }
        return entries;
    }

    // Versions up to this size are read for git's binary test (a NUL in the first 8000 bytes); the
    // reader keeps all of an object or none of it, and a 500 MB asset must not be held in memory.
    private const int ClassifyBytes = 4 << 20;

    private (bool Lfs, bool Binary) ClassifyStages(Dictionary<int, Oid> stages)
    {
        bool lfs = false, binary = false;
        foreach (var stage in (int[])[2, 3])
        {
            if (!stages.TryGetValue(stage, out var oid) || this.CatFile.Read(oid, ClassifyBytes) is not ({ Type: "blob" } obj, var size)) continue;
            // Larger: not read. A conflicted text file of over 4 MB is rare, and binary is the safe guess:
            // it only takes away "Both" and merge tools, which cannot merge a binary file anyway.
            if (size > ClassifyBytes) binary = true;
            else if (LfsPointer.TryParse(obj.Content) is not null) lfs = true;
            else if (obj.Content.AsSpan(0, Math.Min(obj.Content.Length, 8000)).IndexOf((byte)0) >= 0) binary = true;
        }
        return (lfs, binary);
    }

    /// <summary>
    /// Resolves one conflicted file and stages the result. "Accept both" uses
    /// <c>git merge-file --union</c> on the three index stages, so it works
    /// with every conflict style (including diff3/zdiff3 base sections) and
    /// never looks at markers in the working tree.
    /// </summary>
    public async Task ResolveConflictAsync(GitPath path, ConflictResolution resolution, CancellationToken ct = default)
    {
        var stdinPath = GitPath.ToNulSeparated([path]);
        switch (resolution)
        {
            case ConflictResolution.AcceptCurrent:
            case ConflictResolution.AcceptIncoming:
                var side = resolution == ConflictResolution.AcceptCurrent ? "--ours" : "--theirs";
                var checkout = await this.TryWriteAsync(["checkout", "-q", side, "--pathspec-from-file=-", "--pathspec-file-nul"], stdinPath, ct);
                if (checkout.Success)
                {
                    await this.StageAsync([path], ct);
                    return;
                }
                // The chosen side deleted the file: resolve as a deletion.
                var stages = await this.ConflictStagesAsync(path, ct);
                if ((resolution == ConflictResolution.AcceptCurrent ? stages.Ours : stages.Theirs) is null)
                {
                    await this.WriteAsync("Resolve conflict", ["rm", "-q", "--cached", "--ignore-unmatch", "--pathspec-from-file=-", "--pathspec-file-nul"], stdinPath, ct);
                    var full = path.ToFullPath(this.Root);
                    if (File.Exists(full)) File.Delete(full);
                    return;
                }
                checkout.EnsureSuccess("Resolve conflict");
                return;

            case ConflictResolution.AcceptBoth:
                // A union of two LFS pointers is a broken pointer, not both files.
                var both = await this.ConflictStagesAsync(path, ct);
                if (LfsPointer.TryParse(both.Ours ?? []) is not null || LfsPointer.TryParse(both.Theirs ?? []) is not null)
                {
                    throw new GitOperationException(GitErrorCode.Unsupported, $"{path} is a Git LFS file: keep the current or the incoming version.");
                }
                await this.ResolveUnionAsync(path, ct);
                return;

            case ConflictResolution.KeepWorkingTree:
                // Staging drops the conflict stages; a file the user deleted is staged as deleted.
                await this.StageAsync([path], ct);
                return;
        }
    }

    /// <summary>The three index versions of a conflicted file; null for a side that has none (added or deleted there).</summary>
    /// <remarks>
    /// By blob id, not <c>:N:path</c>: the persistent cat-file reads the index once, at its first
    /// such lookup, so a later conflict would come back missing or with an earlier one's content.
    /// </remarks>
    public async Task<ConflictStages> ConflictStagesAsync(GitPath path, CancellationToken ct = default)
    {
        var entries = await this.UnmergedStagesAsync(ct);
        var blobs = entries.Find(e => e.Path == path).Stages ?? [];
        return await Task.Run(() =>
        {
            byte[]? Read(int stage) => blobs.TryGetValue(stage, out var oid) ? this.CatFile.Read(oid)?.Content : null;
            return new ConflictStages(Read(1), Read(2), Read(3));
        }, ct);
    }

    private async Task ResolveUnionAsync(GitPath path, CancellationToken ct)
    {
        var stages = await this.ConflictStagesAsync(path, ct);
        var mode = await this.StageModeAsync(path, ct) ?? "100644";

        var temp = Directory.CreateTempSubdirectory("cheesegit-merge-");
        try
        {
            string Write(string name, byte[]? content)
            {
                var file = Path.Combine(temp.FullName, name);
                File.WriteAllBytes(file, content ?? []);
                return file;
            }
            var (ours, @base, theirs) = (Write("ours", stages.Ours), Write("base", stages.Base), Write("theirs", stages.Theirs));
            // Exit code = number of conflicts (none with --union); negative on error.
            var merged = await this.TryReadAsync(["merge-file", "-p", "--union", ours, @base, theirs], ct: ct);
            if (merged.ExitCode < 0 || merged.ExitCode > 127) merged.EnsureSuccess("Merge both sides");

            // Store the result as a blob in repository form, stage it (which
            // drops the conflict stages), then check it out so the working
            // tree gets the user's filters (autocrlf, ...) applied.
            var blob = await this.WriteAsync("Store resolution", ["hash-object", "-w", "--stdin", "--no-filters"], merged.Stdout, ct);
            byte[] entry = [.. Encoding.ASCII.GetBytes($"{mode} {blob.StdoutText.Trim()}\t"), .. path.Bytes, 0];
            await this.WriteAsync("Stage resolution", ["update-index", "-z", "--index-info"], entry, ct);
            await this.WriteAsync("Write resolution", ["checkout-index", "-f", "-z", "--stdin"], GitPath.ToNulSeparated([path]), ct);
        }
        finally
        {
            temp.Delete(recursive: true);
        }
    }

    private async Task<string?> StageModeAsync(GitPath path, CancellationToken ct)
    {
        var (spec, literal) = path.ToPathspec();
        var result = await this.ReadAsync("Read conflict stages", ["ls-files", "--unmerged", "-z", "--", spec], ct: ct, literalPathspecs: literal);
        var records = new Records(result.Stdout, 0);
        string? mode = null;
        while (records.Next(out var record))
        {
            var tab = record.IndexOf((byte)'\t');
            if (tab < 0 || !record[(tab + 1)..].SequenceEqual(path.Bytes)) continue;
            var fields = Utf8(record[..tab]).Split(' ');
            if (fields.Length < 3) continue;
            mode ??= fields[0];
            if (fields[2] == "2") mode = fields[0]; // prefer our side's mode
        }
        return mode;
    }

    /// <summary>Reverts a commit (a merge commit relative to its first parent).</summary>
    public async Task<RevertResult> RevertAsync(Oid commit, CancellationToken ct = default)
    {
        await this.RequireBranchAsync("revert", ct);
        var info = this.GetCommit(commit) ?? throw new GitOperationException(GitErrorCode.NotFound, $"Commit {commit.ToShortHex()} does not exist.");
        List<string> args = ["revert", "--no-edit"];
        if (info.Parents.Count > 1) args.AddRange(["-m", "1"]);
        args.AddRange(["--end-of-options", commit.ToHex()]);
        var result = await this.TryWriteAsync(args, ct: ct);
        if (result.Success) return new RevertResult.Success();
        if (await this.StateFileExistsAsync("REVERT_HEAD", ct) && (await this.ConflictedFilesAsync(ct)).Count > 0)
        {
            return new RevertResult.Conflict(new MergeConflictInfo(commit.ToShortHex(), await this.ConflictedFilesAsync(ct)));
        }
        result.EnsureSuccess("Revert");
        throw new InvalidOperationException("unreachable");
    }

    public async Task RevertAbortAsync(CancellationToken ct = default)
    {
        if (!await this.StateFileExistsAsync("REVERT_HEAD", ct)) throw new GitOperationException(GitErrorCode.NotInProgress, "No revert is in progress.");
        await this.WriteAsync("Abort revert", ["revert", "--abort"], ct: ct);
    }

    /// <summary>
    /// Finishes a paused revert. When the resolution leaves nothing to commit
    /// (every conflict resolved to the current version) the revert is skipped
    /// instead of failing; decided from the index, not from git's message.
    /// </summary>
    /// <param name="message">
    /// Replaces the message git prepared ("Revert …"). It goes through MERGE_MSG, which
    /// <c>revert --continue</c> commits (the editor is <c>:</c>), so the sequencer state stays git's.
    /// </param>
    public async Task RevertContinueAsync(string? message = null, CancellationToken ct = default)
    {
        if (!await this.StateFileExistsAsync("REVERT_HEAD", ct)) throw new GitOperationException(GitErrorCode.NotInProgress, "No revert is in progress.");
        if (!string.IsNullOrWhiteSpace(message)) await File.WriteAllTextAsync(await this.GitPathAsync("MERGE_MSG", ct), message.Trim() + "\n", ct);
        var conflicts = await this.ConflictedFilesAsync(ct);
        var unchanged = (await this.TryReadAsync(["diff", "--cached", "--quiet", "--no-ext-diff", "HEAD", "--"], ct: ct)).ExitCode == 0;
        if (conflicts.Count == 0 && unchanged)
        {
            await this.WriteAsync("Skip revert", ["revert", "--skip"], ct: ct);
            return;
        }
        await this.WriteAsync("Continue revert", ["revert", "--continue"], ct: ct);
    }

    public async Task CherryPickAbortAsync(CancellationToken ct = default)
    {
        if (!await this.StateFileExistsAsync("CHERRY_PICK_HEAD", ct)) throw new GitOperationException(GitErrorCode.NotInProgress, "No cherry-pick is in progress.");
        await this.WriteAsync("Abort cherry-pick", ["cherry-pick", "--abort"], ct: ct);
    }

    /// <summary>Finishes a paused cherry-pick; one whose resolution leaves nothing to commit is skipped (as for revert).</summary>
    /// <summary>
    /// Commits the resolved pick and goes on with the rest of the sequence. Returns the conflict a
    /// later commit paused on, or null when every pick is done.
    /// </summary>
    public async Task<CherryPickResult.Conflict?> CherryPickContinueAsync(string? message = null, CancellationToken ct = default)
    {
        if (!await this.StateFileExistsAsync("CHERRY_PICK_HEAD", ct)) throw new GitOperationException(GitErrorCode.NotInProgress, "No cherry-pick is in progress.");
        if (!string.IsNullOrWhiteSpace(message)) await File.WriteAllTextAsync(await this.GitPathAsync("MERGE_MSG", ct), message.Trim() + "\n", ct);
        var conflicts = await this.ConflictedFilesAsync(ct);
        var unchanged = (await this.TryReadAsync(["diff", "--cached", "--quiet", "--no-ext-diff", "HEAD", "--"], ct: ct)).ExitCode == 0;
        if (conflicts.Count > 0) throw new GitOperationException(GitErrorCode.UnresolvedConflicts, "Resolve every conflicted file first.");
        // Resolved to the current version: nothing to commit, so this pick is skipped.
        var result = await this.TryWriteAsync(unchanged ? ["cherry-pick", "--skip"] : ["cherry-pick", "--continue"], ct: ct);
        return result.Success ? null : await this.SettleCherryPickAsync(result, ct);
    }

    /// <summary>Aborts the paused merge, revert or cherry-pick.</summary>
    public Task AbortOperationAsync(OperationKind kind, CancellationToken ct = default) => kind switch
    {
        OperationKind.Merge => this.MergeAbortAsync(ct),
        OperationKind.Revert => this.RevertAbortAsync(ct),
        OperationKind.CherryPick => this.CherryPickAbortAsync(ct),
        _ => throw new GitOperationException(GitErrorCode.Unsupported, $"Cannot abort a {kind} here."),
    };

    /// <summary>
    /// Commits the paused merge, revert or cherry-pick with <paramref name="message"/>. Returns false
    /// when a cherry-pick sequence paused again on a later commit's conflicts.
    /// </summary>
    public async Task<bool> ContinueOperationAsync(OperationKind kind, string message, CancellationToken ct = default)
    {
        switch (kind)
        {
            case OperationKind.Merge: await this.MergeContinueAsync(message, ct); return true;
            case OperationKind.Revert: await this.RevertContinueAsync(message, ct); return true;
            case OperationKind.CherryPick: return await this.CherryPickContinueAsync(message, ct) is null;
            default: throw new GitOperationException(GitErrorCode.Unsupported, $"Cannot continue a {kind} here.");
        }
    }

    /// <summary>
    /// The commit message git prepared for the paused operation (MERGE_MSG), without its comment
    /// lines ("# Conflicts: …"); null when there is none.
    /// </summary>
    public async Task<string?> PreparedMessageAsync(CancellationToken ct = default)
    {
        var path = await this.GitPathAsync("MERGE_MSG", ct);
        if (!File.Exists(path)) return null;
        var comment = (await this.TryReadAsync(["config", "--get", "core.commentChar"], ct: ct)).StdoutText.Trim();
        if (comment.Length != 1) comment = "#";
        var lines = (await File.ReadAllLinesAsync(path, ct)).Where(l => !l.StartsWith(comment, StringComparison.Ordinal));
        var message = string.Join('\n', lines).Trim();
        return message.Length > 0 ? message : null;
    }

    /// <summary>
    /// Switches to <paramref name="targetBranch"/> (creating it at HEAD when
    /// <paramref name="createBranch"/>) and cherry-picks <paramref name="commits"/>, oldest first, in
    /// one <c>git cherry-pick</c>: on a conflict git's sequencer pauses there, and continuing (the
    /// conflict dialog) picks the rest. Picks that turn out empty (the change is already on the
    /// branch) are skipped. The old app aborted the conflicting pick and showed git's error.
    /// </summary>
    public async Task<CherryPickResult> CherryPickAsync(IReadOnlyList<Oid> commits, string targetBranch, bool createBranch, CancellationToken ct = default)
    {
        if (commits.Count == 0) throw new GitOperationException(GitErrorCode.NothingSelected, "No commits selected for cherry-pick.");
        if (createBranch) await this.CreateBranchAsync(targetBranch, ct: ct);
        else if (await this.CurrentBranchAsync(ct) != targetBranch) await this.SwitchBranchAsync(targetBranch, ct);

        var before = await this.ResolveAsync("HEAD", ct);
        var result = await this.TryWriteAsync(["cherry-pick", "-x", "--end-of-options", .. commits.Select(c => c.ToHex())], ct: ct);
        if (!result.Success && await this.SettleCherryPickAsync(result, ct) is { } conflict) return conflict;
        return new CherryPickResult.Success(await this.CountCommitsAsync(before, ct));
    }

    /// <summary>
    /// After <c>cherry-pick</c> (or <c>--continue</c>/<c>--skip</c>) failed: skips picks that stopped
    /// only because they became empty, and returns the conflict the sequence paused on. Anything else
    /// (local changes in the way) leaves the earlier picks, clears the sequencer with <c>--quit</c>
    /// (else every later cherry-pick says one is in progress) and throws git's error.
    /// </summary>
    private async Task<CherryPickResult.Conflict?> SettleCherryPickAsync(GitResult result, CancellationToken ct)
    {
        while (!result.Success)
        {
            if (!await this.StateFileExistsAsync("CHERRY_PICK_HEAD", ct)) break;
            var conflicts = await this.ConflictedFilesAsync(ct);
            if (conflicts.Count > 0)
            {
                var head = await this.ResolveAsync("CHERRY_PICK_HEAD", ct);
                return new CherryPickResult.Conflict(new MergeConflictInfo(head?.ToShortHex() ?? "", conflicts));
            }
            if ((await this.TryReadAsync(["diff", "--cached", "--quiet", "--no-ext-diff", "HEAD", "--"], ct: ct)).ExitCode != 0) break;
            // Stopped with nothing to commit: git 2.45 has --empty=drop; this works from 2.34.
            result = await this.TryWriteAsync(["cherry-pick", "--skip"], ct: ct);
        }
        if (result.Success) return null;
        await this.TryWriteAsync(["cherry-pick", "--quit"], ct: ct);
        result.EnsureSuccess("Cherry-pick");
        throw new InvalidOperationException("unreachable");
    }

    private async Task<int> CountCommitsAsync(Oid? since, CancellationToken ct)
    {
        if (since is not { } oid) return 0;
        var count = await this.TryReadAsync(["rev-list", "--count", "--end-of-options", oid.ToHex() + "..HEAD"], ct: ct);
        return int.TryParse(count.StdoutText.Trim(), out var n) ? n : 0;
    }

    /// <summary>
    /// Predicts merging <paramref name="branch"/> into HEAD with <c>git merge-tree --write-tree</c>,
    /// which merges in memory: the working tree, index and refs are untouched. Local changes are
    /// not considered (the real merge refuses to overwrite them).
    /// </summary>
    public async Task<MergePrediction> PredictMergeAsync(string branch, CancellationToken ct = default)
    {
        if (!this.Version.SupportsMergeTreeWriteTree) return new MergePrediction.Unknown();
        var source = await this.ResolveBranchRefAsync(branch, ct);
        if ((await this.TryReadAsync(["merge-base", "--is-ancestor", "--end-of-options", source, "HEAD"], ct: ct)).ExitCode == 0)
        {
            return new MergePrediction.UpToDate();
        }
        var result = await this.TryReadAsync(["merge-tree", "--write-tree", "-z", "--name-only", "--no-messages", "--end-of-options", "HEAD", source], ct: ct);
        // "<tree>\0<path>\0…": exit 0 is clean, 1 has conflicts. A bad revision also exits 1 but
        // prints no tree, so the tree decides.
        var records = new Records(result.Stdout, 0);
        if (result.ExitCode is not (0 or 1) || !records.Next(out var tree) || !Oid.TryParseHex(Utf8(tree), out _))
        {
            result.EnsureSuccess("Predict merge");
            throw new GitOperationException(GitErrorCode.NotFound, $"Cannot merge '{branch}'.");
        }
        if (result.ExitCode == 0) return new MergePrediction.Clean();
        var files = new List<GitPath>();
        while (records.Next(out var record))
        {
            if (record.IsEmpty) break;
            var path = GitPath.FromBytes(record);
            if (!files.Contains(path)) files.Add(path);
        }
        return new MergePrediction.Conflicts(files);
    }

    private async Task<bool> StateFileExistsAsync(string name, CancellationToken ct) => File.Exists(await this.GitPathAsync(name, ct));

    /// <summary>"feature" → refs/heads/feature, "origin/x" → refs/remotes/origin/x; full names and ids pass through.</summary>
    private async Task<string> ResolveBranchRefAsync(string name, CancellationToken ct)
    {
        if (name.StartsWith("refs/", StringComparison.Ordinal)) return name;
        if (await this.RefExistsAsync("refs/heads/" + name, ct)) return "refs/heads/" + name;
        if (await this.RefExistsAsync("refs/remotes/" + name, ct)) return "refs/remotes/" + name;
        if (await this.ResolveAsync(name, ct) is { } oid) return oid.ToHex();
        throw new GitOperationException(GitErrorCode.NotFound, $"Branch '{name}' does not exist.");
    }

    private static string ShortBranchName(string fullName) =>
        fullName.StartsWith("refs/heads/", StringComparison.Ordinal) ? fullName["refs/heads/".Length..]
        : fullName.StartsWith("refs/remotes/", StringComparison.Ordinal) ? fullName["refs/remotes/".Length..]
        : fullName;

    /// <summary>Names the incoming side of a paused operation: a branch at MERGE_HEAD, else the short commit id.</summary>
    private async Task<string> DescribeIncomingAsync(string headFile, OperationKind kind, CancellationToken ct)
    {
        var first = (await File.ReadAllLinesAsync(headFile, ct)).FirstOrDefault()?.Trim() ?? "";
        if (!Oid.TryParseHex(first, out var oid)) return "";
        if (kind == OperationKind.Merge)
        {
            var refs = await this.ReadAsync("Name merge head", ["for-each-ref", "--points-at", oid.ToHex(), "--format=%(refname)", "refs/heads/", "refs/remotes/"], ct: ct);
            var names = refs.StdoutText.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            var best = names.FirstOrDefault(n => n.StartsWith("refs/heads/", StringComparison.Ordinal)) ?? names.FirstOrDefault();
            if (best is not null) return ShortBranchName(best);
        }
        return oid.ToShortHex();
    }

    /// <summary>
    /// Commits brought in from <paramref name="incoming"/>. Counted against the
    /// merged side, not HEAD's movement, so the number is the same whether the
    /// merge fast-forwarded or (merge.ff=false) created a merge commit.
    /// </summary>
    private async Task<int> CountCommitsAsync(Oid? before, Oid incoming, CancellationToken ct)
    {
        var range = before is { } b ? $"{b.ToHex()}..{incoming.ToHex()}" : incoming.ToHex();
        var result = await this.ReadAsync("Count commits", ["rev-list", "--count", "--end-of-options", range], ct: ct);
        return int.Parse(result.StdoutText.Trim());
    }
}
