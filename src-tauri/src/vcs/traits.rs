use std::path::Path;

use crate::error::AppError;
use crate::vcs::types::{BranchInfo, CommitInfo, RepoInfo, RepoStatus};

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

    /// Return all local branches, ordered by most recent commit date (descending).
    fn list_branches(&self, repo_path: &Path) -> Result<Vec<BranchInfo>, AppError>;

    /// Switch to the given branch.
    fn switch_branch(&self, repo_path: &Path, branch_name: &str) -> Result<(), AppError>;

    /// Create a new branch from the current HEAD and switch to it.
    fn create_branch(&self, repo_path: &Path, branch_name: &str) -> Result<(), AppError>;

    /// Return the staged and unstaged file changes.
    fn status(&self, repo_path: &Path) -> Result<RepoStatus, AppError>;

    /// Create a commit from the currently staged changes.
    fn commit(&self, repo_path: &Path, summary: &str, description: &str) -> Result<(), AppError>;

    /// Stage the given files (add to index).
    fn stage_files(&self, repo_path: &Path, paths: &[&str]) -> Result<(), AppError>;

    /// Unstage the given files (reset from index).
    fn unstage_files(&self, repo_path: &Path, paths: &[&str]) -> Result<(), AppError>;
}
