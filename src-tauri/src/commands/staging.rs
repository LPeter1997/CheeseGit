use std::path::Path;
use std::sync::Arc;

use crate::error::AppError;
use crate::vcs::traits::VcsProvider;
use crate::vcs::types::{DiffArea, FileDiff, LineSelection, RepoStatus};

use super::spawn_blocking;

/// Return the staged and unstaged file changes for the repository.
#[tauri::command]
#[specta::specta]
pub async fn get_status(
    repo_path: String,
    vcs: tauri::State<'_, Arc<dyn VcsProvider>>,
) -> Result<RepoStatus, AppError> {
    let vcs = vcs.inner().clone();
    spawn_blocking(move || vcs.status(Path::new(&repo_path))).await
}

/// Create a commit from the currently staged changes.
#[tauri::command]
#[specta::specta]
pub async fn commit(
    repo_path: String,
    summary: String,
    description: String,
    allow_empty: bool,
    vcs: tauri::State<'_, Arc<dyn VcsProvider>>,
) -> Result<(), AppError> {
    let vcs = vcs.inner().clone();
    spawn_blocking(move || {
        vcs.commit(Path::new(&repo_path), &summary, &description, allow_empty)
    })
    .await
}

/// Stage the given files.
#[tauri::command]
#[specta::specta]
pub async fn stage_files(
    repo_path: String,
    paths: Vec<String>,
    vcs: tauri::State<'_, Arc<dyn VcsProvider>>,
) -> Result<(), AppError> {
    let vcs = vcs.inner().clone();
    spawn_blocking(move || {
        let refs: Vec<&str> = paths.iter().map(|s| s.as_str()).collect();
        vcs.stage_files(Path::new(&repo_path), &refs)
    })
    .await
}

/// Unstage the given files.
#[tauri::command]
#[specta::specta]
pub async fn unstage_files(
    repo_path: String,
    paths: Vec<String>,
    vcs: tauri::State<'_, Arc<dyn VcsProvider>>,
) -> Result<(), AppError> {
    let vcs = vcs.inner().clone();
    spawn_blocking(move || {
        let refs: Vec<&str> = paths.iter().map(|s| s.as_str()).collect();
        vcs.unstage_files(Path::new(&repo_path), &refs)
    })
    .await
}

/// Stage specific lines from a file's unstaged diff.
#[tauri::command]
#[specta::specta]
pub async fn stage_lines(
    repo_path: String,
    file_path: String,
    diff: FileDiff,
    selections: Vec<LineSelection>,
    vcs: tauri::State<'_, Arc<dyn VcsProvider>>,
) -> Result<(), AppError> {
    let vcs = vcs.inner().clone();
    spawn_blocking(move || {
        vcs.stage_lines(Path::new(&repo_path), &file_path, &diff, &selections)
    })
    .await
}

/// Unstage specific lines from a file's staged diff.
#[tauri::command]
#[specta::specta]
pub async fn unstage_lines(
    repo_path: String,
    file_path: String,
    diff: FileDiff,
    selections: Vec<LineSelection>,
    vcs: tauri::State<'_, Arc<dyn VcsProvider>>,
) -> Result<(), AppError> {
    let vcs = vcs.inner().clone();
    spawn_blocking(move || {
        vcs.unstage_lines(Path::new(&repo_path), &file_path, &diff, &selections)
    })
    .await
}

/// Discard unstaged changes for the given files.
#[tauri::command]
#[specta::specta]
pub async fn discard_unstaged_files(
    repo_path: String,
    paths: Vec<String>,
    vcs: tauri::State<'_, Arc<dyn VcsProvider>>,
) -> Result<(), AppError> {
    let vcs = vcs.inner().clone();
    spawn_blocking(move || {
        let refs: Vec<&str> = paths.iter().map(|s| s.as_str()).collect();
        vcs.discard_unstaged_files(Path::new(&repo_path), &refs)
    })
    .await
}

/// Discard staged changes for the given files.
#[tauri::command]
#[specta::specta]
pub async fn discard_staged_files(
    repo_path: String,
    paths: Vec<String>,
    vcs: tauri::State<'_, Arc<dyn VcsProvider>>,
) -> Result<(), AppError> {
    let vcs = vcs.inner().clone();
    spawn_blocking(move || {
        let refs: Vec<&str> = paths.iter().map(|s| s.as_str()).collect();
        vcs.discard_staged_files(Path::new(&repo_path), &refs)
    })
    .await
}

/// Discard specific lines from a file's diff.
#[tauri::command]
#[specta::specta]
pub async fn discard_lines(
    repo_path: String,
    file_path: String,
    diff: FileDiff,
    selections: Vec<LineSelection>,
    area: DiffArea,
    vcs: tauri::State<'_, Arc<dyn VcsProvider>>,
) -> Result<(), AppError> {
    let vcs = vcs.inner().clone();
    spawn_blocking(move || {
        vcs.discard_lines(
            Path::new(&repo_path),
            &file_path,
            &diff,
            &selections,
            area,
        )
    })
    .await
}
