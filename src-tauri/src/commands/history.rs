use std::path::Path;

use crate::error::AppError;
use crate::vcs::traits::VcsProvider;
use crate::vcs::types::CommitInfo;

/// Return the commit log for the current branch, most recent first.
#[tauri::command]
#[specta::specta]
pub fn get_commit_log(
    repo_path: String,
    limit: u32,
    vcs: tauri::State<'_, Box<dyn VcsProvider>>,
) -> Result<Vec<CommitInfo>, AppError> {
    vcs.commit_log(Path::new(&repo_path), limit)
}
