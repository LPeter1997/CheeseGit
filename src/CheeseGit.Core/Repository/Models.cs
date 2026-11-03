using CheeseGit.Core.Diff;
using CheeseGit.Core.Git;

namespace CheeseGit.Core.Repository;

public enum DiffArea
{
    Staged,
    Unstaged,
}

public enum DiffSide
{
    Old,
    New,
}

/// <summary>
/// A file version's size, and its bytes unless it was too large to read (null). For a Git LFS file
/// they are the real content's, from the local LFS store, and <see cref="Lfs"/> is the pointer.
/// </summary>
public sealed record FileContent(long Size, byte[]? Bytes)
{
    /// <summary>The LFS pointer this version is stored as, or null for a regular file.</summary>
    public LfsPointer? Lfs { get; init; }

    /// <summary>An LFS file whose content is not in the local store (never downloaded): <see cref="Bytes"/> is null.</summary>
    public bool IsMissing { get; init; }
}

/// <summary>
/// Where HEAD is. <see cref="Branch"/> is the checked-out branch, or when
/// browsing history (detached HEAD) the branch whose history is most likely
/// being explored (the most recently committed branch containing HEAD).
/// </summary>
public sealed record HeadState(string? Branch, bool BrowsingHistory, Oid? Commit)
{
    public bool IsUnborn => this.Commit is null;
}

public enum RefKind
{
    LocalBranch,
    RemoteBranch,
    Tag,
    Other,
}

/// <summary>A ref with its full name. <see cref="Target"/> is the commit (annotated tags are peeled).</summary>
public sealed record RefInfo(string FullName, Oid Target, RefKind Kind)
{
    /// <summary>"main", "origin/main", "v1.0".</summary>
    public string ShortName => this.Kind switch
    {
        RefKind.LocalBranch => this.FullName["refs/heads/".Length..],
        RefKind.RemoteBranch => this.FullName["refs/remotes/".Length..],
        RefKind.Tag => this.FullName["refs/tags/".Length..],
        _ => this.FullName,
    };
}

public sealed record BranchInfo(string Name, bool IsCurrent, Oid Target, DateTimeOffset LastCommitDate, string? Upstream)
{
    public string FullName => "refs/heads/" + this.Name;
}

/// <summary>A branch that exists only on a remote (no local branch of the same name).</summary>
public sealed record RemoteBranchInfo(string Remote, string Name, Oid Target, DateTimeOffset LastCommitDate)
{
    public string FullName => $"refs/remotes/{this.Remote}/{this.Name}";
}

public sealed record RemoteInfo(string Name, string Url);

/// <summary>How far the current branch is ahead of and behind <see cref="Upstream"/> ("origin/main").</summary>
public sealed record TrackingStatus(int Ahead, int Behind, string Upstream);

public sealed record BranchDeleteInfo(bool ExistsOnRemote, string? RemoteName, string? RemoteBranchName);

public sealed record RestoredCommitMessage(string Summary, string Description);

public sealed record StashEntry(int Index, Oid Oid, string Message, string Author, DateTimeOffset When)
{
    public string Ref => $"stash@{{{this.Index}}}";
}

public enum StashApplyResult
{
    Applied,

    /// <summary>Applied with conflicts left in the working tree; the entry was kept (also by pop).</summary>
    Conflicts,
}

public sealed record MergeConflictInfo(string IncomingBranch, IReadOnlyList<GitPath> ConflictedFiles);

public abstract record MergeResult
{
    public sealed record Success(int CommitsMerged) : MergeResult;

    public sealed record AlreadyUpToDate : MergeResult;

    public sealed record Conflict(MergeConflictInfo Info) : MergeResult;
}

public abstract record RevertResult
{
    public sealed record Success : RevertResult;

    public sealed record Conflict(MergeConflictInfo Info) : RevertResult;
}

public abstract record CherryPickResult
{
    /// <summary>Every pick is on the branch; <paramref name="Picked"/> commits were added (empty picks are skipped).</summary>
    public sealed record Success(int Picked) : CherryPickResult;

    /// <summary>The sequence paused on a commit with conflicts; the conflict dialog continues it.</summary>
    public sealed record Conflict(MergeConflictInfo Info) : CherryPickResult;
}

public enum OperationKind
{
    None,
    Merge,
    Revert,
    CherryPick,
    Rebase,
}

/// <summary>
/// An operation git has paused (usually on conflicts), read from the
/// repository's state files, never from command output.
/// </summary>
public sealed record OperationState(OperationKind Kind, string Incoming, int ConflictCount)
{
    public static readonly OperationState None = new(OperationKind.None, "", 0);
}

public enum ConflictResolution
{
    AcceptCurrent,
    AcceptIncoming,
    AcceptBoth,

    /// <summary>
    /// Stage the working-tree file as it is: the user resolved it in an editor or merge tool. The
    /// old app ran "accept both" on such files, which here would rebuild them from the index
    /// stages and throw the user's edits away.
    /// </summary>
    KeepWorkingTree,
}

/// <summary>What merging a branch would do, worked out without touching the working tree (<c>git merge-tree</c>, 2.38+).</summary>
public abstract record MergePrediction
{
    /// <summary>The branch is already contained in HEAD.</summary>
    public sealed record UpToDate : MergePrediction;

    public sealed record Clean : MergePrediction;

    /// <summary>The files that would conflict (content, add/add, modify/delete, …).</summary>
    public sealed record Conflicts(IReadOnlyList<GitPath> Files) : MergePrediction;

    /// <summary>git is older than 2.38: no prediction.</summary>
    public sealed record Unknown : MergePrediction;
}

public sealed record ConflictStages(byte[]? Base, byte[]? Ours, byte[]? Theirs);

public sealed record FileConflictInfo(GitPath Path, int ConflictCount)
{
    /// <summary>
    /// A Git LFS file: git merged the two pointers as text (<c>merge=lfs</c> names no driver), so any
    /// markers sit in pointer lines and mean nothing (<see cref="ConflictCount"/> is 0). Choose a side.
    /// </summary>
    public bool IsLfs { get; init; }

    /// <summary>A binary file (a NUL in its first 8000 bytes, git's test): it cannot be merged line by line.</summary>
    public bool IsBinary { get; init; }

    /// <summary>"Both" (a line union) and merge tools only make sense for text.</summary>
    public bool CanMergeLines => !this.IsLfs && !this.IsBinary;
}

/// <summary>Which commits to load for the history view.</summary>
/// <param name="Branches">Full or short local branch names; empty means every local branch.</param>
/// <param name="Remote">The remote to compare against for local-only markers and whose default branch is added.</param>
/// <param name="MaxCommits">Cap on the number of commits loaded.</param>
public sealed record HistoryRequest(IReadOnlyList<string>? Branches = null, string? Remote = null, int? MaxCommits = null);

/// <summary>
/// The history view's data: topology in display order plus decorations.
/// Commit metadata is not included; read it lazily for visible rows with
/// <see cref="GitRepository.GetCommit"/>.
/// </summary>
public sealed class HistoryGraph(
    Graph.CommitDag dag,
    IReadOnlyList<string> branches,
    IReadOnlyDictionary<Oid, IReadOnlyList<RefInfo>> refsByCommit,
    IReadOnlySet<Oid> localOnly,
    string? defaultBranch = null)
{
    public Graph.CommitDag Dag { get; } = dag;

    /// <summary>
    /// The repository's main line (full ref name): the local branch the remote's default branch
    /// names, else that remote branch, else a local main or master. Its first-parent chain keeps
    /// one colour in the graph, whichever branch is checked out. Null when there is none.
    /// </summary>
    public string? DefaultBranch { get; } = defaultBranch;

    /// <summary>The full ref names whose history is shown.</summary>
    public IReadOnlyList<string> Branches { get; } = branches;

    public IReadOnlyDictionary<Oid, IReadOnlyList<RefInfo>> RefsByCommit { get; } = refsByCommit;

    /// <summary>The commit each of <see cref="Branches"/> points to.</summary>
    public IReadOnlyDictionary<string, Oid> Tips { get; } = refsByCommit.Values
        .SelectMany(refs => refs)
        .Where(r => branches.Contains(r.FullName))
        .ToDictionary(r => r.FullName, r => r.Target);

    /// <summary>Commits that exist locally but not on the selected remote.</summary>
    public IReadOnlySet<Oid> LocalOnly { get; } = localOnly;

    public IReadOnlyList<RefInfo> RefsOf(Oid oid) => this.RefsByCommit.TryGetValue(oid, out var refs) ? refs : [];
}

/// <summary>What the history search matches against, besides the commit id and its refs.</summary>
public sealed record CommitSummary(Oid Oid, string Author, string Summary);

/// <summary>Insertions/deletions of one commit against its first parent.</summary>
public sealed record CommitStats(int Insertions, int Deletions, int FilesChanged);

public sealed record CommitFileChanges(IReadOnlyList<FileChange> Files, IReadOnlyList<FileStats> Stats);
