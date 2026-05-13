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
