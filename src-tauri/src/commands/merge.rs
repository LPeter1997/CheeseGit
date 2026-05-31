use std::path::Path;
use std::sync::Arc;

use crate::error::AppError;
use crate::vcs::traits::VcsProvider;
use crate::vcs::types::{ConflictResolution, FileConflictInfo, MergeConflictInfo, MergeResult, MergeStateInfo};

use super::spawn_blocking;

/// Check whether a merge or revert is currently in progress.
#[tauri::command]
#[specta::specta]
pub async fn check_merge_state(
    repo_path: String,
    vcs: tauri::State<'_, Arc<dyn VcsProvider>>,
) -> Result<MergeStateInfo, AppError> {
    let vcs = vcs.inner().clone();
    spawn_blocking(move || vcs.check_merge_state(Path::new(&repo_path))).await
}

/// Merge the given branch into the current branch.
#[tauri::command]
#[specta::specta]
pub async fn merge_branch(
    repo_path: String,
    branch_name: String,
    vcs: tauri::State<'_, Arc<dyn VcsProvider>>,
) -> Result<MergeResult, AppError> {
    let vcs = vcs.inner().clone();
    spawn_blocking(move || vcs.merge_branch(Path::new(&repo_path), &branch_name)).await
}

/// Abort an in-progress merge.
#[tauri::command]
#[specta::specta]
pub async fn merge_abort(
    repo_path: String,
    vcs: tauri::State<'_, Arc<dyn VcsProvider>>,
) -> Result<(), AppError> {
    let vcs = vcs.inner().clone();
    spawn_blocking(move || vcs.merge_abort(Path::new(&repo_path))).await
}

/// Return the current merge conflict state.
#[tauri::command]
#[specta::specta]
pub async fn get_merge_conflicts(
    repo_path: String,
    vcs: tauri::State<'_, Arc<dyn VcsProvider>>,
) -> Result<MergeConflictInfo, AppError> {
    let vcs = vcs.inner().clone();
    spawn_blocking(move || vcs.merge_conflicts(Path::new(&repo_path))).await
}

/// Resolve a conflicted file using the given strategy.
#[tauri::command]
#[specta::specta]
pub async fn resolve_conflict(
    repo_path: String,
    file_path: String,
    resolution: ConflictResolution,
    vcs: tauri::State<'_, Arc<dyn VcsProvider>>,
) -> Result<(), AppError> {
    let vcs = vcs.inner().clone();
    spawn_blocking(move || vcs.resolve_conflict(Path::new(&repo_path), &file_path, resolution))
        .await
}

/// Open a conflicted file in an external merge tool.
#[tauri::command]
#[specta::specta]
pub async fn open_in_merge_tool(
    repo_path: String,
    file_path: String,
    vcs: tauri::State<'_, Arc<dyn VcsProvider>>,
) -> Result<(), AppError> {
    let vcs = vcs.inner().clone();
    spawn_blocking(move || vcs.open_in_merge_tool(Path::new(&repo_path), &file_path)).await
}

/// Finalize the merge after all conflicts are resolved.
/// Returns the number of commits merged.
#[tauri::command]
#[specta::specta]
pub async fn merge_continue(
    repo_path: String,
    message: String,
    vcs: tauri::State<'_, Arc<dyn VcsProvider>>,
) -> Result<u32, AppError> {
    let vcs = vcs.inner().clone();
    spawn_blocking(move || vcs.merge_continue(Path::new(&repo_path), &message)).await
}

/// Get per-file conflict counts.
#[tauri::command]
#[specta::specta]
pub async fn get_conflict_counts(
    repo_path: String,
    vcs: tauri::State<'_, Arc<dyn VcsProvider>>,
) -> Result<Vec<FileConflictInfo>, AppError> {
    let vcs = vcs.inner().clone();
    spawn_blocking(move || vcs.conflict_counts(Path::new(&repo_path))).await
}
