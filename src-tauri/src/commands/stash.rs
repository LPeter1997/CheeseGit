use std::path::Path;
use std::sync::Arc;

use crate::error::AppError;
use crate::vcs::traits::VcsProvider;
use crate::vcs::types::{FileDiff, FileStats, StashEntry, StatusEntry};

use super::spawn_blocking;

/// Stash the currently staged changes with a message.
#[tauri::command]
#[specta::specta]
pub async fn stash_staged(
    repo_path: String,
    message: String,
    vcs: tauri::State<'_, Arc<dyn VcsProvider>>,
) -> Result<(), AppError> {
    let vcs = vcs.inner().clone();
    spawn_blocking(move || vcs.stash_staged(Path::new(&repo_path), &message)).await
}

/// List all stash entries.
#[tauri::command]
#[specta::specta]
pub async fn list_stashes(
    repo_path: String,
    vcs: tauri::State<'_, Arc<dyn VcsProvider>>,
) -> Result<Vec<StashEntry>, AppError> {
    let vcs = vcs.inner().clone();
    spawn_blocking(move || vcs.list_stashes(Path::new(&repo_path))).await
}

/// Apply a stash entry without removing it.
#[tauri::command]
#[specta::specta]
pub async fn stash_apply(
    repo_path: String,
    index: u32,
    vcs: tauri::State<'_, Arc<dyn VcsProvider>>,
) -> Result<(), AppError> {
    let vcs = vcs.inner().clone();
    spawn_blocking(move || vcs.stash_apply(Path::new(&repo_path), index)).await
}

/// Apply a stash entry and remove it.
#[tauri::command]
#[specta::specta]
pub async fn stash_pop(
    repo_path: String,
    index: u32,
    vcs: tauri::State<'_, Arc<dyn VcsProvider>>,
) -> Result<(), AppError> {
    let vcs = vcs.inner().clone();
    spawn_blocking(move || vcs.stash_pop(Path::new(&repo_path), index)).await
}

/// Drop a stash entry.
#[tauri::command]
#[specta::specta]
pub async fn stash_drop(
    repo_path: String,
    index: u32,
    vcs: tauri::State<'_, Arc<dyn VcsProvider>>,
) -> Result<(), AppError> {
    let vcs = vcs.inner().clone();
    spawn_blocking(move || vcs.stash_drop(Path::new(&repo_path), index)).await
}

/// List files changed in a stash entry.
#[tauri::command]
#[specta::specta]
pub async fn list_stash_files(
    repo_path: String,
    index: u32,
    vcs: tauri::State<'_, Arc<dyn VcsProvider>>,
) -> Result<Vec<StatusEntry>, AppError> {
    let vcs = vcs.inner().clone();
    spawn_blocking(move || vcs.list_stash_files(Path::new(&repo_path), index)).await
}

/// Get the diff for a single file in a stash entry.
#[tauri::command]
#[specta::specta]
pub async fn diff_stash_file(
    repo_path: String,
    index: u32,
    file_path: String,
    vcs: tauri::State<'_, Arc<dyn VcsProvider>>,
) -> Result<FileDiff, AppError> {
    let vcs = vcs.inner().clone();
    spawn_blocking(move || vcs.diff_stash_file(Path::new(&repo_path), index, &file_path)).await
}

/// Get per-file stats for a stash entry.
#[tauri::command]
#[specta::specta]
pub async fn stash_file_stats(
    repo_path: String,
    index: u32,
    vcs: tauri::State<'_, Arc<dyn VcsProvider>>,
) -> Result<Vec<FileStats>, AppError> {
    let vcs = vcs.inner().clone();
    spawn_blocking(move || vcs.stash_file_stats(Path::new(&repo_path), index)).await
}

/// Get the contents of a file at a stash entry's revision.
#[tauri::command]
#[specta::specta]
pub async fn show_file_at_stash(
    repo_path: String,
    index: u32,
    file_path: String,
    vcs: tauri::State<'_, Arc<dyn VcsProvider>>,
) -> Result<String, AppError> {
    let vcs = vcs.inner().clone();
    spawn_blocking(move || vcs.show_file_at_stash(Path::new(&repo_path), index, &file_path)).await
}
