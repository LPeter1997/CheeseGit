use std::sync::Arc;
use std::path::Path;

use crate::error::AppError;
use crate::vcs::traits::VcsProvider;
use crate::vcs::types::BranchInfo;

/// Return the name of the current branch for the repository at `repo_path`.
#[tauri::command]
#[specta::specta]
pub fn get_current_branch(
    repo_path: String,
    vcs: tauri::State<'_, Arc<dyn VcsProvider>>,
) -> Result<String, AppError> {
    vcs.current_branch(Path::new(&repo_path))
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
