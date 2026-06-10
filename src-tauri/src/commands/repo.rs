use std::path::Path;
use std::sync::Arc;

use crate::error::AppError;
use crate::vcs::traits::VcsProvider;
use crate::vcs::types::RepoInfo;

use super::spawn_blocking;

/// Validate that `path` is a git repository and return its metadata.
#[tauri::command]
#[specta::specta]
pub async fn open_repository(
    path: String,
    vcs: tauri::State<'_, Arc<dyn VcsProvider>>,
) -> Result<RepoInfo, AppError> {
    let vcs = vcs.inner().clone();
    spawn_blocking(move || {
        let repo_path = Path::new(&path);

        if !repo_path.exists() {
            return Err(AppError::Io(format!("Path does not exist: {path}")));
        }

        if !repo_path.is_dir() {
            return Err(AppError::Io(format!("Path is not a directory: {path}")));
        }

        vcs.open_repository(repo_path)
    })
    .await
}

/// Initialize a new git repository in `parent_folder/name`.
#[tauri::command]
#[specta::specta]
pub async fn init_repository(
    parent_folder: String,
    name: String,
    vcs: tauri::State<'_, Arc<dyn VcsProvider>>,
) -> Result<RepoInfo, AppError> {
    let vcs = vcs.inner().clone();
    spawn_blocking(move || {
        let parent = Path::new(&parent_folder);
        if !parent.is_dir() {
            return Err(AppError::Io(format!(
                "Parent folder does not exist: {parent_folder}"
            )));
        }
        let repo_path = parent.join(&name);
        if repo_path.exists() {
            // Allow empty directories (git init works in them).
            let is_empty_dir = repo_path.is_dir()
                && std::fs::read_dir(&repo_path)
                    .map(|mut e| e.next().is_none())
                    .unwrap_or(false);
            if !is_empty_dir {
                return Err(AppError::Io(format!(
                    "'{}' already exists in the selected folder",
                    name
                )));
            }
        }
        vcs.init_repository(&repo_path)
    })
    .await
}

/// Clone a remote repository into `parent_folder`.
#[tauri::command]
#[specta::specta]
pub async fn clone_repository(
    url: String,
    parent_folder: String,
    vcs: tauri::State<'_, Arc<dyn VcsProvider>>,
) -> Result<RepoInfo, AppError> {
    let vcs = vcs.inner().clone();
    spawn_blocking(move || {
        let parent = Path::new(&parent_folder);
        if !parent.is_dir() {
            return Err(AppError::Io(format!(
                "Parent folder does not exist: {parent_folder}"
            )));
        }
        vcs.clone_repository(&url, parent)
    })
    .await
}

/// Check whether a directory path exists on disk.
#[tauri::command]
#[specta::specta]
pub fn check_path_exists(path: String) -> bool {
    Path::new(&path).is_dir()
}

/// Validate that `parent_folder/name` is a usable target for a new repository.
/// Returns Ok if the parent exists and the target either doesn't exist or is an
/// empty directory.  Returns a descriptive error otherwise.
#[tauri::command]
#[specta::specta]
pub fn validate_repo_path(parent_folder: String, name: String) -> Result<(), AppError> {
    let parent = Path::new(&parent_folder);
    if !parent.is_dir() {
        return Err(AppError::Io(format!(
            "Parent folder does not exist: {parent_folder}"
        )));
    }
    let target = parent.join(&name);
    if target.exists() {
        let is_empty_dir = target.is_dir()
            && std::fs::read_dir(&target)
                .map(|mut e| e.next().is_none())
                .unwrap_or(false);
        if !is_empty_dir {
            return Err(AppError::Io(format!(
                "'{}' already exists in the selected folder",
                name
            )));
        }
    }
    Ok(())
}
