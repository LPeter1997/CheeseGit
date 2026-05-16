mod branch;
mod command_log;
mod diff;
mod history;
mod remote;
mod repo;
mod staging;
mod state;

pub use branch::{create_branch, get_current_branch, list_branches, switch_branch};
pub use command_log::get_command_log;
pub use diff::{get_file_diff, read_file_contents};
pub use history::{get_branch_graph, get_commit_diff, get_commit_file_diff, get_commit_log, get_file_at_commit, list_commit_files};
pub use remote::{fetch, get_tracking_status, list_remotes, publish_branch, pull, push};
pub use repo::open_repository;
pub use staging::{commit, get_status, stage_files, stage_lines, unstage_files, unstage_lines};
pub use state::{get_app_state, save_app_state};
