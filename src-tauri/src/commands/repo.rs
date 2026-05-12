use std::path::Path;

use crate::error::AppError;
use crate::vcs::traits::VcsProvider;
use crate::vcs::types::RepoInfo;

/// Validate that `path` is a git repository and return its metadata.
#[tauri::command]
#[specta::specta]
pub fn open_repository(
    path: String,
    vcs: tauri::State<'_, Box<dyn VcsProvider>>,
) -> Result<RepoInfo, AppError> {
    let repo_path = Path::new(&path);

    if !repo_path.exists() {
        return Err(AppError::Io(format!("Path does not exist: {path}")));
    }

    if !repo_path.is_dir() {
        return Err(AppError::Io(format!("Path is not a directory: {path}")));
    }

    vcs.open_repository(repo_path)
}
