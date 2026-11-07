using CheeseGit.Core.Git;
using System.Runtime.ExceptionServices;

namespace CheeseGit.Core.Repository;

/// <summary>What happened to local changes around a branch switch (#52).</summary>
/// <param name="LeftChanges">The changes were stashed for the branch that was left.</param>
/// <param name="RestoredChanges">Changes left earlier on the new branch were put back.</param>
/// <param name="WaitingChanges">Changes left on the new branch are still in the stash: the working tree was not clean, or applying them failed.</param>
/// <param name="RestoreFailed">Applying the left changes failed (they stay in the stash).</param>
/// <param name="Brought">How changes brought along fared when git refused to carry them itself.</param>
public sealed record SwitchOutcome(bool LeftChanges, bool RestoredChanges, bool WaitingChanges, bool RestoreFailed, BringOutcome Brought = BringOutcome.Direct)
{
    public static readonly SwitchOutcome Nothing = new(false, false, false, false);
}

/// <summary>Changes brought to the new branch (#52).</summary>
public enum BringOutcome
{
    /// <summary>git switched with them (or there were none).</summary>
    Direct,
    /// <summary>git refused, so they went through the stash and came back cleanly.</summary>
    Merged,
    /// <summary>As <see cref="Merged"/>, but the staged changes did not apply to the new branch: they came back unstaged.</summary>
    MergedUnstaged,
    /// <summary>They came back with conflicts to resolve; the stash entry is kept, as git does.</summary>
    Conflicts,
    /// <summary>They could not (all) be put back; the stash entry keeps them.</summary>
    Kept,
}

/// <summary>Switching branches with local changes: bring them along, or leave them on the branch (#52).</summary>
public sealed partial class GitRepository
{
    /// <summary>The stash message that marks changes left on a branch; git prefixes it with "On &lt;branch&gt;: ".</summary>
    public const string LeftChangesMessage = "CheeseGit: changes left on ";

    /// <summary>The stash message of changes on their way to another branch, kept when they could not all be put back.</summary>
    public const string BroughtChangesMessage = "CheeseGit: changes brought from ";

    /// <summary>
    /// Runs <paramref name="switchOperation"/> (any switch to a branch). With
    /// <paramref name="leaveChanges"/>, local changes (untracked files too) are stashed first, marked
    /// with the branch they were left on; if the switch fails they are put back. After the switch,
    /// changes left earlier on the new branch come back when the working tree is clean.
    /// Without it, the switch runs as git does; when git refuses because the changes touch files the
    /// new branch changes too, they go through the stash instead and are merged into the new branch.
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

        GitException? refused = null;
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
        catch (GitException e) when (!leaveChanges && from is not null)
        {
            refused = e;
        }

        var brought = BringOutcome.Direct;
        if (refused is not null)
        {
            // Whether local changes were in the way is judged from the repository (still on the old
            // branch, with changes), not from git's message. When something else failed, the retry
            // below fails the same way and the changes are put back.
            if (await this.CurrentBranchAsync(ct) != from || !await this.HasLocalChangesAsync(ct)) ExceptionDispatchInfo.Throw(refused);
            brought = await this.BringThroughStashAsync(from!, switchOperation, ct);
        }

        var to = await this.CurrentBranchAsync(ct);
        if (to is null || to == from) return new SwitchOutcome(left, false, false, false, brought);
        var entry = (await this.ListStashesAsync(ct)).FirstOrDefault(s => s.Message.EndsWith(": " + LeftChangesMessage + to, StringComparison.Ordinal));
        if (entry is null) return new SwitchOutcome(left, false, false, false, brought);

        // Restoring over other changes could mix the two or conflict; then they wait in the stash.
        if (await this.HasLocalChangesAsync(ct)) return new SwitchOutcome(left, false, true, false, brought);
        var apply = await this.TryWriteAsync(["stash", "apply", "--quiet", "--index", entry.Ref], ct: ct);
        if (!apply.Success) return new SwitchOutcome(left, false, true, true, brought);
        await this.WriteAsync("Drop stash", ["stash", "drop", "--quiet", entry.Ref], ct: ct);
        return new SwitchOutcome(left, true, false, false, brought);
    }

    /// <summary>
    /// git refuses to switch when local changes touch files that differ on the new branch ("would be
    /// overwritten by checkout"): a user moving a fix to the right branch hit exactly that. Stash
    /// everything (untracked files too, which can block a switch as well), switch, then pop: git
    /// merges the changes into the new branch, leaving conflicts to resolve where they clash.
    /// </summary>
    private async Task<BringOutcome> BringThroughStashAsync(string from, Func<CancellationToken, Task> switchOperation, CancellationToken ct)
    {
        var before = await this.StashTipAsync(ct);
        // Not GIT_LITERAL_PATHSPECS: see the stash in SwitchKeepingChangesAsync.
        await this.WriteAsync(
            "Bring changes",
            ["stash", "push", "--quiet", "--include-untracked", "--message", BroughtChangesMessage + from],
            ct: ct,
            literalPathspecs: false);
        if (await this.StashTipAsync(ct) is not { } stash || stash == before)
        {
            // Nothing was stashed (only ignored changes?): let git report why it refuses.
            await switchOperation(ct);
            return BringOutcome.Direct;
        }

        try
        {
            await switchOperation(ct);
        }
        catch
        {
            await this.TryWriteAsync(["stash", "pop", "--quiet", "--index"], ct: CancellationToken.None);
            throw;
        }

        // From here on not cancellable: the changes are in the stash until this finishes.
        var none = CancellationToken.None;
        if ((await this.TryWriteAsync(["stash", "pop", "--quiet", "--index"], ct: none)).Success) return BringOutcome.Merged;
        if ((await this.ConflictedFilesAsync(none)).Count > 0) return BringOutcome.Conflicts;
        if (await this.HasLocalChangesAsync(none)) return BringOutcome.Kept;
        // --index changes nothing when the staged changes do not apply to the new branch; without it the
        // whole change is merged into the working tree.
        if ((await this.TryWriteAsync(["stash", "pop", "--quiet"], ct: none)).Success) return BringOutcome.MergedUnstaged;
        return (await this.ConflictedFilesAsync(none)).Count > 0 ? BringOutcome.Conflicts : BringOutcome.Kept;
    }

    /// <summary>Tracked or untracked changes in the working tree or index (ignored files do not count).</summary>
    private async Task<bool> HasLocalChangesAsync(CancellationToken ct)
    {
        var status = await this.ReadAsync("Read status", ["status", "--porcelain=v2", "-z", "--untracked-files=all", "--ignored=no"], ct: ct);
        return HasEntries(status.Stdout);
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
