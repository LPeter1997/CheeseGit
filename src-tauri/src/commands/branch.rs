use std::path::Path;
use std::sync::Arc;

use crate::error::AppError;
use crate::vcs::traits::VcsProvider;
use crate::vcs::types::{BranchDeleteInfo, BranchInfo, HeadState, RemoteBranchInfo};

use super::spawn_blocking;

/// Return the name of the current branch for the repository at `repo_path`.
#[tauri::command]
#[specta::specta]
pub async fn get_current_branch(
    repo_path: String,
    vcs: tauri::State<'_, Arc<dyn VcsProvider>>,
) -> Result<String, AppError> {
    let vcs = vcs.inner().clone();
    spawn_blocking(move || vcs.current_branch(Path::new(&repo_path))).await
}

/// Return all local branches, ordered by most recent commit date.
#[tauri::command]
#[specta::specta]
pub async fn list_branches(
    repo_path: String,
    vcs: tauri::State<'_, Arc<dyn VcsProvider>>,
) -> Result<Vec<BranchInfo>, AppError> {
    let vcs = vcs.inner().clone();
    spawn_blocking(move || vcs.list_branches(Path::new(&repo_path))).await
}

/// Return remote-only branches (those with no local tracking branch).
#[tauri::command]
#[specta::specta]
pub async fn list_remote_branches(
    repo_path: String,
    vcs: tauri::State<'_, Arc<dyn VcsProvider>>,
) -> Result<Vec<RemoteBranchInfo>, AppError> {
    let vcs = vcs.inner().clone();
    spawn_blocking(move || vcs.list_remote_branches(Path::new(&repo_path))).await
}

/// Switch to the given branch.
#[tauri::command]
#[specta::specta]
pub async fn switch_branch(
    repo_path: String,
    branch_name: String,
    vcs: tauri::State<'_, Arc<dyn VcsProvider>>,
) -> Result<(), AppError> {
    let vcs = vcs.inner().clone();
    spawn_blocking(move || vcs.switch_branch(Path::new(&repo_path), &branch_name)).await
}

/// Create a new branch from HEAD and switch to it.
#[tauri::command]
#[specta::specta]
pub async fn create_branch(
    repo_path: String,
    branch_name: String,
    vcs: tauri::State<'_, Arc<dyn VcsProvider>>,
) -> Result<(), AppError> {
    let vcs = vcs.inner().clone();
    spawn_blocking(move || vcs.create_branch(Path::new(&repo_path), &branch_name)).await
}

/// Delete a local branch. If `force` is true, force-delete even if unmerged.
#[tauri::command]
#[specta::specta]
pub async fn delete_branch(
    repo_path: String,
    branch_name: String,
    force: bool,
    vcs: tauri::State<'_, Arc<dyn VcsProvider>>,
) -> Result<(), AppError> {
    let vcs = vcs.inner().clone();
    spawn_blocking(move || vcs.delete_branch(Path::new(&repo_path), &branch_name, force)).await
}

/// Delete a branch on the remote.
#[tauri::command]
#[specta::specta]
pub async fn delete_remote_branch(
    repo_path: String,
    remote: String,
    branch_name: String,
    vcs: tauri::State<'_, Arc<dyn VcsProvider>>,
) -> Result<(), AppError> {
    let vcs = vcs.inner().clone();
    spawn_blocking(move || {
        vcs.delete_remote_branch(Path::new(&repo_path), &remote, &branch_name)
    })
    .await
}

/// Return information needed to decide how to handle deletion of a branch.
#[tauri::command]
#[specta::specta]
pub async fn get_branch_delete_info(
    repo_path: String,
    branch_name: String,
    vcs: tauri::State<'_, Arc<dyn VcsProvider>>,
) -> Result<BranchDeleteInfo, AppError> {
    let vcs = vcs.inner().clone();
    spawn_blocking(move || vcs.branch_delete_info(Path::new(&repo_path), &branch_name)).await
}

/// Return the full HEAD state: current branch, and whether we are browsing history.
#[tauri::command]
#[specta::specta]
pub async fn get_head_state(
    repo_path: String,
    vcs: tauri::State<'_, Arc<dyn VcsProvider>>,
) -> Result<HeadState, AppError> {
    let vcs = vcs.inner().clone();
    spawn_blocking(move || vcs.head_state(Path::new(&repo_path))).await
}

/// Checkout a specific commit by hash (enters history-browsing mode).
#[tauri::command]
#[specta::specta]
pub async fn checkout_commit(
    repo_path: String,
    hash: String,
    vcs: tauri::State<'_, Arc<dyn VcsProvider>>,
) -> Result<(), AppError> {
    let vcs = vcs.inner().clone();
    spawn_blocking(move || vcs.checkout_commit(Path::new(&repo_path), &hash)).await
}
