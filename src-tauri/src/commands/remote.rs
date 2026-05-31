use std::path::Path;
use std::sync::Arc;

use crate::error::AppError;
use crate::vcs::traits::VcsProvider;
use crate::vcs::types::{BranchTrackingStatus, RemoteInfo};

use super::spawn_blocking;

/// Return all configured remotes for the repository.
#[tauri::command]
#[specta::specta]
pub async fn list_remotes(
    repo_path: String,
    vcs: tauri::State<'_, Arc<dyn VcsProvider>>,
) -> Result<Vec<RemoteInfo>, AppError> {
    let vcs = vcs.inner().clone();
    spawn_blocking(move || vcs.list_remotes(Path::new(&repo_path))).await
}

/// Return the ahead/behind status of the current branch relative to its upstream.
#[tauri::command]
#[specta::specta]
pub async fn get_tracking_status(
    repo_path: String,
    vcs: tauri::State<'_, Arc<dyn VcsProvider>>,
) -> Result<Option<BranchTrackingStatus>, AppError> {
    let vcs = vcs.inner().clone();
    spawn_blocking(move || vcs.branch_tracking_status(Path::new(&repo_path))).await
}

/// Return the ahead/behind status of the current branch relative to a specific remote.
/// Returns None if the branch does not exist on that remote (i.e. not published).
#[tauri::command]
#[specta::specta]
pub async fn get_remote_branch_status(
    repo_path: String,
    remote: String,
    vcs: tauri::State<'_, Arc<dyn VcsProvider>>,
) -> Result<Option<BranchTrackingStatus>, AppError> {
    let vcs = vcs.inner().clone();
    spawn_blocking(move || vcs.remote_branch_status(Path::new(&repo_path), &remote)).await
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
    spawn_blocking(move || vcs.push(Path::new(&repo_path), &remote)).await
}

/// Publish the current branch to the specified remote (push with --set-upstream).
#[tauri::command]
#[specta::specta]
pub async fn publish_branch(
    repo_path: String,
    remote: String,
    vcs: tauri::State<'_, Arc<dyn VcsProvider>>,
) -> Result<(), AppError> {
    let vcs = vcs.inner().clone();
    spawn_blocking(move || vcs.publish_branch(Path::new(&repo_path), &remote)).await
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
    spawn_blocking(move || vcs.pull(Path::new(&repo_path), &remote)).await
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
    spawn_blocking(move || vcs.fetch(Path::new(&repo_path), &remote)).await
}

/// Cache an SSH passphrase so subsequent git remote operations can use it
/// via SSH_ASKPASS. The passphrase is held in-memory only.
#[tauri::command]
#[specta::specta]
pub async fn ssh_add_key(passphrase: String) -> Result<(), AppError> {
    crate::vcs::git::cli::set_ssh_passphrase(Some(passphrase));
    Ok(())
}

/// Add a new remote with the given name and URL.
/// Validates the remote is reachable before completing.
#[tauri::command]
#[specta::specta]
pub async fn add_remote(
    repo_path: String,
    name: String,
    url: String,
    vcs: tauri::State<'_, Arc<dyn VcsProvider>>,
) -> Result<(), AppError> {
    let vcs = vcs.inner().clone();
    spawn_blocking(move || vcs.add_remote(Path::new(&repo_path), &name, &url)).await
}

/// Remove an existing remote by name.
#[tauri::command]
#[specta::specta]
pub async fn remove_remote(
    repo_path: String,
    name: String,
    vcs: tauri::State<'_, Arc<dyn VcsProvider>>,
) -> Result<(), AppError> {
    let vcs = vcs.inner().clone();
    spawn_blocking(move || vcs.remove_remote(Path::new(&repo_path), &name)).await
}
