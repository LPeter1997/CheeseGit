mod branch;
mod command_log;
mod desktop_entry;
mod diff;
mod history;
mod merge;
mod remote;
mod repo;
mod revert;
mod staging;
mod stash;
mod state;
mod watcher;

use crate::error::AppError;

/// Run a blocking closure on the Tokio blocking thread pool.
/// Wraps the JoinError into an AppError automatically.
pub(crate) async fn spawn_blocking<F, T>(f: F) -> Result<T, AppError>
where
    F: FnOnce() -> Result<T, AppError> + Send + 'static,
    T: Send + 'static,
{
    tokio::task::spawn_blocking(f)
        .await
        .map_err(|e| AppError::Other(format!("task join error: {e}")))?
}

pub use branch::{
    checkout_commit, create_branch, delete_branch, delete_remote_branch, get_branch_delete_info,
    get_current_branch, get_head_state, list_branches, switch_branch,
};
pub use command_log::get_command_log;
pub use diff::{get_commit_file_stats, get_diff_stats, get_file_diff, read_file_contents};
pub use history::{
    get_branch_graph, get_commit_diff, get_commit_file_diff, get_commit_log, get_file_at_commit,
    list_commit_files,
};
pub use remote::{fetch, get_remote_branch_status, get_tracking_status, list_remotes, publish_branch, pull, push, ssh_add_key};
pub use repo::{
    check_path_exists, clone_repository, init_repository, open_repository, validate_repo_path,
};
pub use staging::{
    commit, discard_lines, discard_staged_files, discard_unstaged_files, get_status, stage_files,
    stage_lines, unstage_files, unstage_lines,
};
pub use stash::{
    diff_stash_file, list_stash_files, list_stashes, show_file_at_stash, stash_apply,
    stash_drop, stash_file_stats, stash_pop, stash_staged,
};
pub use state::{get_app_state, save_app_state};
pub use desktop_entry::{check_desktop_entry_status, register_desktop_entry};
pub use merge::{get_conflict_counts, get_merge_conflicts, merge_abort, merge_branch, merge_continue, open_in_merge_tool, resolve_conflict};
pub use revert::{revert_abort, revert_commit, revert_continue};
pub use watcher::{watch_repo, unwatch_repo};
