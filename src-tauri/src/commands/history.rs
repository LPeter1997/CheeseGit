use std::path::Path;

use crate::error::AppError;
use crate::vcs::traits::VcsProvider;
use crate::vcs::types::{CommitInfo, FileDiff, StatusEntry};

/// Return the commit log for the current branch, most recent first.
#[tauri::command]
#[specta::specta]
pub fn get_commit_log(
    repo_path: String,
    limit: u32,
    vcs: tauri::State<'_, Box<dyn VcsProvider>>,
) -> Result<Vec<CommitInfo>, AppError> {
    vcs.commit_log(Path::new(&repo_path), limit)
}

/// Return diffs for all files changed in a specific commit.
#[tauri::command]
#[specta::specta]
pub fn get_commit_diff(
    repo_path: String,
    hash: String,
    vcs: tauri::State<'_, Box<dyn VcsProvider>>,
) -> Result<Vec<FileDiff>, AppError> {
    vcs.diff_commit(Path::new(&repo_path), &hash)
}

/// Return the contents of a file at a specific commit revision.
#[tauri::command]
#[specta::specta]
pub fn get_file_at_commit(
    repo_path: String,
    hash: String,
    file_path: String,
    vcs: tauri::State<'_, Box<dyn VcsProvider>>,
) -> Result<String, AppError> {
    vcs.show_file_at_commit(Path::new(&repo_path), &hash, &file_path)
}

/// Return the list of files changed in a specific commit.
#[tauri::command]
#[specta::specta]
pub fn list_commit_files(
    repo_path: String,
    hash: String,
    vcs: tauri::State<'_, Box<dyn VcsProvider>>,
) -> Result<Vec<StatusEntry>, AppError> {
    vcs.list_commit_files(Path::new(&repo_path), &hash)
}

/// Return the diff for a single file within a specific commit.
#[tauri::command]
#[specta::specta]
pub fn get_commit_file_diff(
    repo_path: String,
    hash: String,
    file_path: String,
    vcs: tauri::State<'_, Box<dyn VcsProvider>>,
) -> Result<FileDiff, AppError> {
    vcs.diff_commit_file(Path::new(&repo_path), &hash, &file_path)
}
