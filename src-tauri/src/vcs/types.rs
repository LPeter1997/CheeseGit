use serde::{Deserialize, Serialize};
use specta::Type;

/// Describes the current HEAD position in VCS-agnostic terms.
#[derive(Debug, Clone, Serialize, Deserialize, Type)]
pub struct HeadState {
    /// The branch name relevant to the current state.
    /// When `browsing_history` is false, this is the checked-out branch.
    /// When `browsing_history` is true, this is the branch whose history
    /// is being explored (best-effort; may be None if indeterminate).
    pub branch: Option<String>,
    /// Whether the user is browsing history (checked out a specific commit
    /// rather than being on a branch tip). The UI should disable mutating
    /// operations like commit and sync when this is true.
    pub browsing_history: bool,
}

/// Basic metadata about an opened repository.
#[derive(Debug, Clone, Serialize, Deserialize, Type)]
pub struct RepoInfo {
    /// Human-readable name (typically the folder name).
    pub name: String,
    /// Absolute path to the repository root.
    pub path: String,
}

/// A single commit in the repository history.
#[derive(Debug, Clone, Serialize, Deserialize, Type)]
pub struct CommitInfo {
    /// Full commit hash.
    pub hash: String,
    /// Short (abbreviated) commit hash.
    pub short_hash: String,
    /// First line of the commit message.
    pub summary: String,
    /// Author name.
    pub author: String,
    /// ISO 8601 timestamp.
    pub timestamp: String,
}

/// A branch in the repository.
#[derive(Debug, Clone, Serialize, Deserialize, Type)]
pub struct BranchInfo {
    /// Branch name (e.g. "main").
    pub name: String,
    /// Whether this is the currently checked-out branch.
    pub is_current: bool,
    /// ISO 8601 timestamp of the most recent commit on this branch.
    pub last_commit_date: String,
}

/// The kind of change a file has undergone.
#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize, Type)]
pub enum FileStatus {
    Added,
    Modified,
    Deleted,
    Renamed,
    Copied,
    Untracked,
    Unknown,
}

/// A changed file in the working tree or index.
#[derive(Debug, Clone, Serialize, Deserialize, Type)]
pub struct StatusEntry {
    /// Relative path of the file.
    pub path: String,
    /// Kind of change.
    pub status: FileStatus,
}

/// The full working-tree status of a repository.
#[derive(Debug, Clone, Serialize, Deserialize, Type)]
pub struct RepoStatus {
    /// Files with staged (index) changes.
    pub staged: Vec<StatusEntry>,
    /// Files with unstaged (worktree) changes.
    pub unstaged: Vec<StatusEntry>,
}

/// Commit message fields restored after undoing the latest commit.
#[derive(Debug, Clone, Serialize, Deserialize, Type)]
pub struct RestoredCommitMessage {
    /// First line of the reverted commit message.
    pub summary: String,
    /// Remaining commit message body (without the summary line).
    pub description: String,
}

/// Whether to diff staged (cached) or unstaged (worktree) changes.
#[derive(Debug, Clone, Copy, Serialize, Deserialize, Type)]
pub enum DiffArea {
    Staged,
    Unstaged,
}

/// The type of a line in a diff hunk.
#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize, Type)]
pub enum DiffLineKind {
    Context,
    Addition,
    Deletion,
}

/// A highlighted span within a diff line, marking an inline change.
#[derive(Debug, Clone, Serialize, Deserialize, Type)]
pub struct InlineHighlight {
    /// Byte offset into the line content where the highlight starts.
    pub start: u32,
    /// Length in bytes of the highlighted span.
    pub length: u32,
}

/// A single line within a diff hunk.
#[derive(Debug, Clone, Serialize, Deserialize, Type)]
pub struct DiffLine {
    /// The kind of change.
    pub kind: DiffLineKind,
    /// The line content (without the leading +/-/space).
    pub content: String,
    /// Line number in the old (a) side, if applicable.
    pub old_lineno: Option<u32>,
    /// Line number in the new (b) side, if applicable.
    pub new_lineno: Option<u32>,
    /// Inline change highlights within this line.
    /// Empty when the entire line is added/deleted or for context lines.
    pub highlights: Vec<InlineHighlight>,
}

/// A contiguous hunk of changes.
#[derive(Debug, Clone, Serialize, Deserialize, Type)]
pub struct DiffHunk {
    /// The hunk header (e.g. "@@ -1,3 +1,5 @@").
    pub header: String,
    /// Starting line in the old file.
    pub old_start: u32,
    /// Starting line in the new file.
    pub new_start: u32,
    /// Lines in this hunk.
    pub lines: Vec<DiffLine>,
}

/// The complete diff output for a single file.
#[derive(Debug, Clone, Serialize, Deserialize, Type)]
pub struct FileDiff {
    /// The relative path of the file.
    pub path: String,
    /// The hunks that make up this diff.
    pub hunks: Vec<DiffHunk>,
}

/// A selection of lines within a diff to stage/unstage.
/// Each entry is (hunk_index, line_index_within_hunk).
#[derive(Debug, Clone, Serialize, Deserialize, Type)]
pub struct LineSelection {
    pub hunk_index: u32,
    pub line_index: u32,
}

/// A configured remote for the repository.
#[derive(Debug, Clone, Serialize, Deserialize, Type)]
pub struct RemoteInfo {
    /// Remote name (e.g. "origin").
    pub name: String,
    /// Remote URL.
    pub url: String,
}

/// How far ahead/behind the local branch is relative to its upstream.
#[derive(Debug, Clone, Serialize, Deserialize, Type)]
pub struct BranchTrackingStatus {
    /// Number of commits the local branch is ahead of upstream.
    pub ahead: u32,
    /// Number of commits the local branch is behind upstream.
    pub behind: u32,
    /// The upstream reference (e.g. "origin/main").
    pub upstream: String,
}

/// A commit node in the branch graph visualization.
#[derive(Debug, Clone, Serialize, Deserialize, Type)]
pub struct GraphCommit {
    /// Full commit hash.
    pub hash: String,
    /// Short (abbreviated) commit hash.
    pub short_hash: String,
    /// First line of the commit message.
    pub summary: String,
    /// Author name.
    pub author: String,
    /// ISO 8601 timestamp.
    pub timestamp: String,
    /// Full hashes of parent commits.
    pub parents: Vec<String>,
    /// Branch/ref names pointing to this commit (e.g. "main", "origin/main").
    pub refs: Vec<String>,
    /// Total lines added in this commit (if available).
    pub insertions: Option<u32>,
    /// Total lines deleted in this commit (if available).
    pub deletions: Option<u32>,
}

/// Information needed to decide how to handle branch deletion.
#[derive(Debug, Clone, Serialize, Deserialize, Type)]
pub struct BranchDeleteInfo {
    /// Whether the branch actually exists on the remote right now.
    pub exists_on_remote: bool,
    /// The remote name (e.g. "origin"), if the branch is tracked.
    pub remote_name: Option<String>,
    /// The branch name on the remote (may differ from local name).
    pub remote_branch_name: Option<String>,
}

/// Per-file addition/deletion statistics from a diff.
#[derive(Debug, Clone, Serialize, Deserialize, Type)]
pub struct FileStats {
    /// Relative path of the file.
    pub path: String,
    /// Number of lines added.
    pub additions: u32,
    /// Number of lines deleted.
    pub deletions: u32,
}

/// Data for rendering a branch graph.
#[derive(Debug, Clone, Serialize, Deserialize, Type)]
pub struct BranchGraphData {
    /// Commits in topological order (most recent first).
    pub commits: Vec<GraphCommit>,
    /// Names of the branches included in this graph.
    pub branches: Vec<String>,
    /// Commit hashes that exist only locally (not pushed to the selected remote).
    pub local_only_commits: Vec<String>,
}

/// The result of a merge operation.
#[derive(Debug, Clone, Serialize, Deserialize, Type)]
pub enum MergeResult {
    /// Merge completed successfully (fast-forward or clean merge).
    Success,
    /// Merge has conflicts that need to be resolved.
    Conflict(MergeConflictInfo),
}

/// Information about a merge conflict.
#[derive(Debug, Clone, Serialize, Deserialize, Type)]
pub struct MergeConflictInfo {
    /// The branch being merged into the current branch.
    pub incoming_branch: String,
    /// List of files with conflicts.
    pub conflicted_files: Vec<String>,
}

/// How to resolve a conflicted file.
#[derive(Debug, Clone, Serialize, Deserialize, Type)]
pub enum ConflictResolution {
    /// Accept the current branch's version (ours).
    AcceptCurrent,
    /// Accept the incoming branch's version (theirs).
    AcceptIncoming,
    /// Accept both changes (concatenate).
    AcceptBoth,
}

/// Per-file conflict information including count of conflict markers.
#[derive(Debug, Clone, Serialize, Deserialize, Type)]
pub struct FileConflictInfo {
    /// The relative file path.
    pub path: String,
    /// Number of conflict regions (count of `<<<<<<<` markers).
    pub conflict_count: u32,
}

/// The result of a revert operation.
#[derive(Debug, Clone, Serialize, Deserialize, Type)]
pub enum RevertResult {
    /// Revert completed successfully (no conflicts).
    Success,
    /// Revert has conflicts that need to be resolved.
    Conflict(MergeConflictInfo),
}

/// A single stash entry.
#[derive(Debug, Clone, Serialize, Deserialize, Type)]
pub struct StashEntry {
    /// The stash index (0, 1, 2, …).
    pub index: u32,
    /// The stash ref (e.g. "stash@{0}").
    pub stash_ref: String,
    /// The stash message.
    pub message: String,
    /// ISO 8601 timestamp.
    pub timestamp: String,
    /// Author name.
    pub author: String,
    /// The commit hash of the stash commit.
    pub hash: String,
    /// Short (abbreviated) commit hash.
    pub short_hash: String,
}
