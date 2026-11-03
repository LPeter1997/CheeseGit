namespace CheeseGit.Core.Repository;

/// <summary>What happened to local changes around a branch switch (#52).</summary>
/// <param name="LeftChanges">The changes were stashed for the branch that was left.</param>
/// <param name="RestoredChanges">Changes left earlier on the new branch were put back.</param>
/// <param name="WaitingChanges">Changes left on the new branch are still in the stash: the working tree was not clean, or applying them failed.</param>
/// <param name="RestoreFailed">Applying the left changes failed (they stay in the stash).</param>
public sealed record SwitchOutcome(bool LeftChanges, bool RestoredChanges, bool WaitingChanges, bool RestoreFailed)
{
    public static readonly SwitchOutcome Nothing = new(false, false, false, false);
}

/// <summary>Switching branches with local changes: bring them along, or leave them on the branch (#52).</summary>
public sealed partial class GitRepository
{
    /// <summary>The stash message that marks changes left on a branch; git prefixes it with "On &lt;branch&gt;: ".</summary>
    public const string LeftChangesMessage = "CheeseGit: changes left on ";

    /// <summary>
    /// Runs <paramref name="switchOperation"/> (any switch to a branch). With
    /// <paramref name="leaveChanges"/>, local changes (untracked files too) are stashed first, marked
    /// with the branch they were left on; if the switch fails they are put back. After the switch,
    /// changes left earlier on the new branch come back when the working tree is clean.
    /// </summary>
    public async Task<SwitchOutcome> SwitchKeepingChangesAsync(bool leaveChanges, Func<CancellationToken, Task> switchOperation, CancellationToken ct = default)
    {
        var from = await this.CurrentBranchAsync(ct);
        var left = false;
        if (leaveChanges && from is not null)
        {
            // Whether a stash was made is read from refs/stash, not from git's "No local changes" message.
            var before = await this.StashTipAsync(ct);
            // Not GIT_LITERAL_PATHSPECS: under it, git 2.56 stores untracked files in the stash but
            // leaves them in the tree, so they would follow the switch and then block the restore.
            // This command takes no pathspecs, so the variable protects nothing here.
            await this.WriteAsync(
                "Leave changes",
                ["stash", "push", "--quiet", "--include-untracked", "--message", LeftChangesMessage + from],
                ct: ct,
                literalPathspecs: false);
            left = await this.StashTipAsync(ct) is { } after && after != before;
        }

        try
        {
            await switchOperation(ct);
        }
        catch when (left)
        {
            // Not cancellable: the changes must not stay hidden in the stash.
            await this.TryWriteAsync(["stash", "pop", "--quiet", "--index"], ct: CancellationToken.None);
            throw;
        }

        var to = await this.CurrentBranchAsync(ct);
        if (to is null || to == from) return new SwitchOutcome(left, false, false, false);
        var entry = (await this.ListStashesAsync(ct)).FirstOrDefault(s => s.Message.EndsWith(": " + LeftChangesMessage + to, StringComparison.Ordinal));
        if (entry is null) return new SwitchOutcome(left, false, false, false);

        // Restoring over other changes could mix the two or conflict; then they wait in the stash.
        var status = await this.ReadAsync("Read status", ["status", "--porcelain=v2", "-z", "--untracked-files=all", "--ignored=no"], ct: ct);
        if (HasEntries(status.Stdout)) return new SwitchOutcome(left, false, true, false);
        var apply = await this.TryWriteAsync(["stash", "apply", "--quiet", "--index", entry.Ref], ct: ct);
        if (!apply.Success) return new SwitchOutcome(left, false, true, true);
        await this.WriteAsync("Drop stash", ["stash", "drop", "--quiet", entry.Ref], ct: ct);
        return new SwitchOutcome(left, true, false, false);
    }

    /// <summary>Porcelain v2 records other than "# ..." headers (status.showStash=true adds "# stash N").</summary>
    private static bool HasEntries(byte[] porcelain)
    {
        var records = new Records(porcelain, 0);
        while (records.Next(out var record))
        {
            if (record.Length > 0 && record[0] != (byte)'#') return true;
        }
        return false;
    }

    private async Task<string?> StashTipAsync(CancellationToken ct)
    {
        var tip = await this.TryReadAsync(["rev-parse", "--quiet", "--verify", "refs/stash"], ct: ct);
        return tip.Success ? tip.StdoutText.Trim() : null;
    }
}
