use std::path::Path;
use std::sync::Arc;

use tauri::AppHandle;

use crate::error::AppError;
use crate::watcher::RepoWatcherManager;

/// Start watching a repository for file changes.
#[tauri::command]
#[specta::specta]
pub async fn watch_repo(
    repo_path: String,
    watcher: tauri::State<'_, Arc<RepoWatcherManager>>,
    app_handle: AppHandle,
) -> Result<(), AppError> {
    watcher
        .start_watching(Path::new(&repo_path), app_handle)
        .map_err(AppError::Other)
}

/// Stop watching a repository for file changes.
#[tauri::command]
#[specta::specta]
pub async fn unwatch_repo(
    repo_path: String,
    watcher: tauri::State<'_, Arc<RepoWatcherManager>>,
) -> Result<(), AppError> {
    watcher
        .stop_watching(Path::new(&repo_path))
        .map_err(AppError::Other)
}
