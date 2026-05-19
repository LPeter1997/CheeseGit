use std::path::Path;
use std::sync::Arc;

use crate::error::AppError;
use crate::vcs::traits::VcsProvider;
use crate::vcs::types::{BranchGraphData, CommitInfo, FileDiff, StatusEntry};

/// Return the commit log for the current branch, most recent first.
#[tauri::command]
#[specta::specta]
pub async fn get_commit_log(
    repo_path: String,
    limit: u32,
    vcs: tauri::State<'_, Arc<dyn VcsProvider>>,
) -> Result<Vec<CommitInfo>, AppError> {
    let vcs = vcs.inner().clone();
    tokio::task::spawn_blocking(move || vcs.commit_log(Path::new(&repo_path), limit))
        .await
        .map_err(|e| AppError::Other(format!("task join error: {e}")))?
}

/// Return diffs for all files changed in a specific commit.
#[tauri::command]
#[specta::specta]
pub async fn get_commit_diff(
    repo_path: String,
    hash: String,
    vcs: tauri::State<'_, Arc<dyn VcsProvider>>,
) -> Result<Vec<FileDiff>, AppError> {
    let vcs = vcs.inner().clone();
    tokio::task::spawn_blocking(move || vcs.diff_commit(Path::new(&repo_path), &hash))
        .await
        .map_err(|e| AppError::Other(format!("task join error: {e}")))?
}

/// Return the contents of a file at a specific commit revision.
#[tauri::command]
#[specta::specta]
pub async fn get_file_at_commit(
    repo_path: String,
    hash: String,
    file_path: String,
    vcs: tauri::State<'_, Arc<dyn VcsProvider>>,
) -> Result<String, AppError> {
    let vcs = vcs.inner().clone();
    tokio::task::spawn_blocking(move || {
        vcs.show_file_at_commit(Path::new(&repo_path), &hash, &file_path)
    })
    .await
    .map_err(|e| AppError::Other(format!("task join error: {e}")))?
}

/// Return the list of files changed in a specific commit.
#[tauri::command]
#[specta::specta]
pub async fn list_commit_files(
    repo_path: String,
    hash: String,
    vcs: tauri::State<'_, Arc<dyn VcsProvider>>,
) -> Result<Vec<StatusEntry>, AppError> {
    let vcs = vcs.inner().clone();
    tokio::task::spawn_blocking(move || vcs.list_commit_files(Path::new(&repo_path), &hash))
        .await
        .map_err(|e| AppError::Other(format!("task join error: {e}")))?
}

/// Return the diff for a single file within a specific commit.
#[tauri::command]
#[specta::specta]
pub async fn get_commit_file_diff(
    repo_path: String,
    hash: String,
    file_path: String,
    vcs: tauri::State<'_, Arc<dyn VcsProvider>>,
) -> Result<FileDiff, AppError> {
    let vcs = vcs.inner().clone();
    tokio::task::spawn_blocking(move || {
        vcs.diff_commit_file(Path::new(&repo_path), &hash, &file_path)
    })
    .await
    .map_err(|e| AppError::Other(format!("task join error: {e}")))?
}

/// Return the branch graph data for visualization.
#[tauri::command]
#[specta::specta]
pub async fn get_branch_graph(
    repo_path: String,
    branches: Vec<String>,
    remote: Option<String>,
    max_commits: Option<u32>,
    vcs: tauri::State<'_, Arc<dyn VcsProvider>>,
) -> Result<BranchGraphData, AppError> {
    let vcs = vcs.inner().clone();
    tokio::task::spawn_blocking(move || {
        let branch_refs: Vec<&str> = branches.iter().map(|s| s.as_str()).collect();
        vcs.branch_graph(
            Path::new(&repo_path),
            &branch_refs,
            remote.as_deref(),
            max_commits,
        )
    })
    .await
    .map_err(|e| AppError::Other(format!("task join error: {e}")))?
}
