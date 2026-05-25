use std::path::Path;
use std::sync::Arc;

use crate::error::AppError;
use crate::vcs::traits::VcsProvider;
use crate::vcs::types::RevertResult;

use super::spawn_blocking;

/// Revert the given commit.
#[tauri::command]
#[specta::specta]
pub async fn revert_commit(
    repo_path: String,
    hash: String,
    vcs: tauri::State<'_, Arc<dyn VcsProvider>>,
) -> Result<RevertResult, AppError> {
    let vcs = vcs.inner().clone();
    spawn_blocking(move || vcs.revert_commit(Path::new(&repo_path), &hash)).await
}

/// Abort an in-progress revert.
#[tauri::command]
#[specta::specta]
pub async fn revert_abort(
    repo_path: String,
    vcs: tauri::State<'_, Arc<dyn VcsProvider>>,
) -> Result<(), AppError> {
    let vcs = vcs.inner().clone();
    spawn_blocking(move || vcs.revert_abort(Path::new(&repo_path))).await
}

/// Continue a revert after resolving conflicts.
#[tauri::command]
#[specta::specta]
pub async fn revert_continue(
    repo_path: String,
    vcs: tauri::State<'_, Arc<dyn VcsProvider>>,
) -> Result<(), AppError> {
    let vcs = vcs.inner().clone();
    spawn_blocking(move || vcs.revert_continue(Path::new(&repo_path))).await
}
