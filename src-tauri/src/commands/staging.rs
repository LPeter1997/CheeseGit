use std::sync::Arc;
use std::path::Path;

use crate::error::AppError;
use crate::vcs::traits::VcsProvider;
use crate::vcs::types::{FileDiff, LineSelection, RepoStatus};

/// Return the staged and unstaged file changes for the repository.
#[tauri::command]
#[specta::specta]
pub fn get_status(
    repo_path: String,
    vcs: tauri::State<'_, Arc<dyn VcsProvider>>,
) -> Result<RepoStatus, AppError> {
    vcs.status(Path::new(&repo_path))
}

/// Create a commit from the currently staged changes.
#[tauri::command]
#[specta::specta]
pub fn commit(
    repo_path: String,
    summary: String,
    description: String,
    vcs: tauri::State<'_, Arc<dyn VcsProvider>>,
) -> Result<(), AppError> {
    vcs.commit(Path::new(&repo_path), &summary, &description)
}

/// Stage the given files.
#[tauri::command]
#[specta::specta]
pub fn stage_files(
    repo_path: String,
    paths: Vec<String>,
    vcs: tauri::State<'_, Arc<dyn VcsProvider>>,
) -> Result<(), AppError> {
    let refs: Vec<&str> = paths.iter().map(|s| s.as_str()).collect();
    vcs.stage_files(Path::new(&repo_path), &refs)
}

/// Unstage the given files.
#[tauri::command]
#[specta::specta]
pub fn unstage_files(
    repo_path: String,
    paths: Vec<String>,
    vcs: tauri::State<'_, Arc<dyn VcsProvider>>,
) -> Result<(), AppError> {
    let refs: Vec<&str> = paths.iter().map(|s| s.as_str()).collect();
    vcs.unstage_files(Path::new(&repo_path), &refs)
}

/// Stage specific lines from a file's unstaged diff.
#[tauri::command]
#[specta::specta]
pub fn stage_lines(
    repo_path: String,
    file_path: String,
    diff: FileDiff,
    selections: Vec<LineSelection>,
    vcs: tauri::State<'_, Arc<dyn VcsProvider>>,
) -> Result<(), AppError> {
    vcs.stage_lines(Path::new(&repo_path), &file_path, &diff, &selections)
}

/// Unstage specific lines from a file's staged diff.
#[tauri::command]
#[specta::specta]
pub fn unstage_lines(
    repo_path: String,
    file_path: String,
    diff: FileDiff,
    selections: Vec<LineSelection>,
    vcs: tauri::State<'_, Arc<dyn VcsProvider>>,
) -> Result<(), AppError> {
    vcs.unstage_lines(Path::new(&repo_path), &file_path, &diff, &selections)
}
