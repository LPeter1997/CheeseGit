use std::sync::Arc;
use std::path::Path;

use crate::error::AppError;
use crate::vcs::traits::VcsProvider;
use crate::vcs::types::{BranchTrackingStatus, RemoteInfo};

/// Return all configured remotes for the repository.
#[tauri::command]
#[specta::specta]
pub fn list_remotes(
    repo_path: String,
    vcs: tauri::State<'_, Arc<dyn VcsProvider>>,
) -> Result<Vec<RemoteInfo>, AppError> {
    vcs.list_remotes(Path::new(&repo_path))
}

/// Return the ahead/behind status of the current branch relative to its upstream.
#[tauri::command]
#[specta::specta]
pub fn get_tracking_status(
    repo_path: String,
    vcs: tauri::State<'_, Arc<dyn VcsProvider>>,
) -> Result<Option<BranchTrackingStatus>, AppError> {
    vcs.branch_tracking_status(Path::new(&repo_path))
}

/// Push the current branch to the specified remote.
#[tauri::command]
#[specta::specta]
pub async fn push(
    repo_path: String,
    remote: String,
    vcs: tauri::State<'_, Arc<dyn VcsProvider>>,
) -> Result<(), AppError> {
    let vcs = vcs.inner().clone();
    let repo_path = repo_path.clone();
    let remote = remote.clone();
    tokio::task::spawn_blocking(move || vcs.push(Path::new(&repo_path), &remote))
        .await
        .map_err(|e| AppError::Other(format!("task join error: {e}")))?
}

/// Pull from the specified remote into the current branch.
#[tauri::command]
#[specta::specta]
pub async fn pull(
    repo_path: String,
    remote: String,
    vcs: tauri::State<'_, Arc<dyn VcsProvider>>,
) -> Result<(), AppError> {
    let vcs = vcs.inner().clone();
    let repo_path = repo_path.clone();
    let remote = remote.clone();
    tokio::task::spawn_blocking(move || vcs.pull(Path::new(&repo_path), &remote))
        .await
        .map_err(|e| AppError::Other(format!("task join error: {e}")))?
}

/// Fetch from the specified remote.
#[tauri::command]
#[specta::specta]
pub async fn fetch(
    repo_path: String,
    remote: String,
    vcs: tauri::State<'_, Arc<dyn VcsProvider>>,
) -> Result<(), AppError> {
    let vcs = vcs.inner().clone();
    let repo_path = repo_path.clone();
    let remote = remote.clone();
    tokio::task::spawn_blocking(move || vcs.fetch(Path::new(&repo_path), &remote))
        .await
        .map_err(|e| AppError::Other(format!("task join error: {e}")))?
}
