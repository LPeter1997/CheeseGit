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
