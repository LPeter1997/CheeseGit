use serde::{Deserialize, Serialize};
use specta::Type;

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
#[derive(Debug, Clone, Serialize, Deserialize, Type)]
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

/// Whether to diff staged (cached) or unstaged (worktree) changes.
#[derive(Debug, Clone, Copy, Serialize, Deserialize, Type)]
pub enum DiffArea {
    Staged,
    Unstaged,
}

/// The type of a line in a diff hunk.
#[derive(Debug, Clone, Serialize, Deserialize, Type)]
pub enum DiffLineKind {
    Context,
    Addition,
    Deletion,
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
