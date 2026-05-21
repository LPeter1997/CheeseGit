use std::path::Path;
use std::sync::Arc;

use crate::error::AppError;
use crate::vcs::traits::VcsProvider;
use crate::vcs::types::{BranchDeleteInfo, BranchInfo};

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
pub fn list_branches(
    repo_path: String,
    vcs: tauri::State<'_, Arc<dyn VcsProvider>>,
) -> Result<Vec<BranchInfo>, AppError> {
    vcs.list_branches(Path::new(&repo_path))
}

/// Switch to the given branch.
#[tauri::command]
#[specta::specta]
pub fn switch_branch(
    repo_path: String,
    branch_name: String,
    vcs: tauri::State<'_, Arc<dyn VcsProvider>>,
) -> Result<(), AppError> {
    vcs.switch_branch(Path::new(&repo_path), &branch_name)
}

/// Create a new branch from HEAD and switch to it.
#[tauri::command]
#[specta::specta]
pub fn create_branch(
    repo_path: String,
    branch_name: String,
    vcs: tauri::State<'_, Arc<dyn VcsProvider>>,
) -> Result<(), AppError> {
    vcs.create_branch(Path::new(&repo_path), &branch_name)
}

/// Delete a local branch. If `force` is true, force-delete even if unmerged.
#[tauri::command]
#[specta::specta]
pub fn delete_branch(
    repo_path: String,
    branch_name: String,
    force: bool,
    vcs: tauri::State<'_, Arc<dyn VcsProvider>>,
) -> Result<(), AppError> {
    vcs.delete_branch(Path::new(&repo_path), &branch_name, force)
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
