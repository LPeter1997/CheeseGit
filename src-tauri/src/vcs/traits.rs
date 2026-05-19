use std::path::Path;

use crate::error::AppError;
use crate::vcs::types::{
    BranchDeleteInfo, BranchGraphData, BranchInfo, BranchTrackingStatus, CommitInfo, DiffArea,
    FileDiff, LineSelection, RemoteInfo, RepoInfo, RepoStatus, StatusEntry,
};

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

    /// Delete a local branch. If `force` is true, use `-D` (force-delete even if unmerged).
    fn delete_branch(
        &self,
        repo_path: &Path,
        branch_name: &str,
        force: bool,
    ) -> Result<(), AppError>;

    /// Delete a branch on the remote.
    fn delete_remote_branch(
        &self,
        repo_path: &Path,
        remote: &str,
        branch_name: &str,
    ) -> Result<(), AppError>;

    /// Return information needed to decide how to handle deletion of a branch.
    fn branch_delete_info(
        &self,
        repo_path: &Path,
        branch_name: &str,
    ) -> Result<BranchDeleteInfo, AppError>;

    /// Return the staged and unstaged file changes.
    fn status(&self, repo_path: &Path) -> Result<RepoStatus, AppError>;

    /// Create a commit from the currently staged changes.
    /// If `allow_empty` is true, allow creating a commit with no staged changes.
    fn commit(
        &self,
        repo_path: &Path,
        summary: &str,
        description: &str,
        allow_empty: bool,
    ) -> Result<(), AppError>;

    /// Stage the given files (add to index).
    fn stage_files(&self, repo_path: &Path, paths: &[&str]) -> Result<(), AppError>;

    /// Unstage the given files (reset from index).
    fn unstage_files(&self, repo_path: &Path, paths: &[&str]) -> Result<(), AppError>;

    /// Return the diff for a single file.
    fn diff_file(
        &self,
        repo_path: &Path,
        file_path: &str,
        area: DiffArea,
    ) -> Result<FileDiff, AppError>;

    /// Return diffs for all files changed in a specific commit.
    fn diff_commit(&self, repo_path: &Path, hash: &str) -> Result<Vec<FileDiff>, AppError>;

    /// Return the list of files changed in a specific commit (names + statuses only).
    fn list_commit_files(&self, repo_path: &Path, hash: &str)
    -> Result<Vec<StatusEntry>, AppError>;

    /// Return the diff for a single file within a specific commit.
    fn diff_commit_file(
        &self,
        repo_path: &Path,
        hash: &str,
        file_path: &str,
    ) -> Result<FileDiff, AppError>;

    /// Return the contents of a file at a specific commit revision.
    fn show_file_at_commit(
        &self,
        repo_path: &Path,
        hash: &str,
        file_path: &str,
    ) -> Result<String, AppError>;

    /// Stage specific lines from a file's unstaged diff.
    fn stage_lines(
        &self,
        repo_path: &Path,
        file_path: &str,
        diff: &FileDiff,
        selections: &[LineSelection],
    ) -> Result<(), AppError>;

    /// Unstage specific lines from a file's staged diff.
    fn unstage_lines(
        &self,
        repo_path: &Path,
        file_path: &str,
        diff: &FileDiff,
        selections: &[LineSelection],
    ) -> Result<(), AppError>;

    /// Return all configured remotes.
    fn list_remotes(&self, repo_path: &Path) -> Result<Vec<RemoteInfo>, AppError>;

    /// Return how far ahead/behind the current branch is relative to its upstream.
    /// Returns `None` if there is no upstream configured.
    fn branch_tracking_status(
        &self,
        repo_path: &Path,
    ) -> Result<Option<BranchTrackingStatus>, AppError>;

    /// Return ahead/behind status of the current branch relative to a specific remote.
    /// Returns None if the branch does not exist on that remote.
    fn remote_branch_status(
        &self,
        repo_path: &Path,
        remote: &str,
    ) -> Result<Option<BranchTrackingStatus>, AppError>;

    /// Push the current branch to the given remote.
    fn push(&self, repo_path: &Path, remote: &str) -> Result<(), AppError>;

    /// Publish the current branch to the given remote (push with --set-upstream).
    fn publish_branch(&self, repo_path: &Path, remote: &str) -> Result<(), AppError>;

    /// Pull from the given remote into the current branch.
    fn pull(&self, repo_path: &Path, remote: &str) -> Result<(), AppError>;

    /// Fetch from the given remote.
    fn fetch(&self, repo_path: &Path, remote: &str) -> Result<(), AppError>;

    /// Return the branch graph data for the given branches.
    /// If `branches` is empty, include all local branches.
    /// If `remote` is provided, identify commits not pushed to that remote.
    /// If `max_commits` is provided, limit the number of commits returned.
    fn branch_graph(
        &self,
        repo_path: &Path,
        branches: &[&str],
        remote: Option<&str>,
        max_commits: Option<u32>,
    ) -> Result<BranchGraphData, AppError>;

    /// Initialize a new repository at the given path.
    fn init_repository(&self, path: &Path) -> Result<RepoInfo, AppError>;

    /// Clone a repository from `url` into `parent_folder`.
    fn clone_repository(&self, url: &str, parent_folder: &Path) -> Result<RepoInfo, AppError>;
}
