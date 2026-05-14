use std::sync::Arc;
use std::path::Path;

use crate::error::AppError;
use crate::vcs::traits::VcsProvider;
use crate::vcs::types::{DiffArea, FileDiff};

/// Read the contents of a file in the repository working tree.
#[tauri::command]
#[specta::specta]
pub fn read_file_contents(repo_path: String, relative_path: String) -> Result<String, AppError> {
    let repo = Path::new(&repo_path);
    let full_path = repo.join(&relative_path);

    // Ensure the resolved path stays within the repository root.
    let canonical_repo = repo
        .canonicalize()
        .map_err(|e| AppError::Io(format!("Cannot resolve repo path: {e}")))?;
    let canonical_file = full_path
        .canonicalize()
        .map_err(|e| AppError::Io(format!("Cannot resolve file path: {e}")))?;

    if !canonical_file.starts_with(&canonical_repo) {
        return Err(AppError::Io(
            "Path traversal: file is outside the repository".into(),
        ));
    }

    std::fs::read_to_string(&canonical_file)
        .map_err(|e| AppError::Io(format!("Failed to read file: {e}")))
}

/// Return the diff for a single file, either staged or unstaged.
#[tauri::command]
#[specta::specta]
pub fn get_file_diff(
    repo_path: String,
    relative_path: String,
    area: DiffArea,
    vcs: tauri::State<'_, Arc<dyn VcsProvider>>,
) -> Result<FileDiff, AppError> {
    let repo = Path::new(&repo_path);
    vcs.diff_file(repo, &relative_path, area)
}
