using CheeseGit.Core.Git;

namespace CheeseGit.Core.Repository;

/// <summary>The independently refreshable parts of a <see cref="RepoSnapshot"/>.</summary>
[Flags]
public enum SnapshotParts
{
    None = 0,
    Status = 1 << 0,
    Head = 1 << 1,
    Refs = 1 << 2,
    Operation = 1 << 3,
    Stashes = 1 << 4,
    Remotes = 1 << 5,
    All = Status | Head | Refs | Operation | Stashes | Remotes,
}

/// <summary>
/// Immutable state of a repository as last read. Owned by
/// <see cref="RepositorySession"/>; the UI only ever reads it, so there is no
/// second copy of the state to keep consistent (old app; docs/architecture.md, State and refresh).
/// </summary>
public sealed record RepoSnapshot(
    HeadState Head,
    RepoStatus Status,
    IReadOnlyList<RefInfo> Refs,
    OperationState Operation,
    IReadOnlyList<StashEntry> Stashes,
    IReadOnlyList<RemoteInfo> Remotes)
{
    /// <summary>Incremented on every published change.</summary>
    public long Version { get; init; }

    /// <summary>
    /// The parts whose content differs. Lists are compared element-wise, so a
    /// refresh that reads the same state again publishes nothing.
    /// </summary>
    public SnapshotParts DifferencesFrom(RepoSnapshot other)
    {
        var parts = SnapshotParts.None;
        if (!SameStatus(this.Status, other.Status)) parts |= SnapshotParts.Status;
        if (this.Head != other.Head) parts |= SnapshotParts.Head;
        if (!this.Refs.SequenceEqual(other.Refs)) parts |= SnapshotParts.Refs;
        if (this.Operation != other.Operation) parts |= SnapshotParts.Operation;
        if (!this.Stashes.SequenceEqual(other.Stashes)) parts |= SnapshotParts.Stashes;
        if (!this.Remotes.SequenceEqual(other.Remotes)) parts |= SnapshotParts.Remotes;
        return parts;
    }

    private static bool SameStatus(RepoStatus a, RepoStatus b) =>
        a.Branch == b.Branch && a.StashCount == b.StashCount && a.Entries.SequenceEqual(b.Entries);
}

/// <summary>A published snapshot and the parts that changed since the previous one.</summary>
public sealed record SnapshotChange(RepoSnapshot Snapshot, SnapshotParts Changed);
