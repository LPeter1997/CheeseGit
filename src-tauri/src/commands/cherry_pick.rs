use std::path::Path;
use std::sync::Arc;

use crate::error::AppError;
use crate::vcs::traits::VcsProvider;

use super::spawn_blocking;

/// Cherry-pick selected commits onto an existing or newly-created target branch.
#[tauri::command]
#[specta::specta]
pub async fn cherry_pick_commits(
    repo_path: String,
    hashes: Vec<String>,
    target_branch: String,
    create_branch: bool,
    vcs: tauri::State<'_, Arc<dyn VcsProvider>>,
) -> Result<(), AppError> {
    let vcs = vcs.inner().clone();
    spawn_blocking(move || {
        vcs.cherry_pick_commits(
            Path::new(&repo_path),
            &hashes,
            &target_branch,
            create_branch,
        )
    })
    .await
}
