use std::path::Path;

use crate::error::AppError;

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
