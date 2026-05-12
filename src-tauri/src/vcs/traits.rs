use std::path::Path;

use crate::error::AppError;
use crate::vcs::types::{CommitInfo, RepoInfo};

/// Abstraction over a version control system.
///
/// All VCS operations go through this trait so that command handlers never
/// interact with a specific VCS backend directly.
pub trait VcsProvider: Send + Sync {
    /// Check whether `path` is the root of a valid repository.
    /// If valid, return metadata about it; otherwise return an error.
    fn open_repository(&self, path: &Path) -> Result<RepoInfo, AppError>;

    /// Return the name of the current branch (e.g. "main").
    fn current_branch(&self, repo_path: &Path) -> Result<String, AppError>;

    /// Return the commit log for the current branch, most recent first.
    /// `limit` caps the number of entries returned.
    fn commit_log(&self, repo_path: &Path, limit: u32) -> Result<Vec<CommitInfo>, AppError>;
}
