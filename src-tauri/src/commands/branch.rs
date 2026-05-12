use std::path::Path;

use crate::error::AppError;
use crate::vcs::traits::VcsProvider;

/// Return the name of the current branch for the repository at `repo_path`.
#[tauri::command]
#[specta::specta]
pub fn get_current_branch(
    repo_path: String,
    vcs: tauri::State<'_, Box<dyn VcsProvider>>,
) -> Result<String, AppError> {
    vcs.current_branch(Path::new(&repo_path))
}
