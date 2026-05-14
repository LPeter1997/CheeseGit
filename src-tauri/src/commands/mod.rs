mod branch;
mod command_log;
mod diff;
mod history;
mod repo;
mod staging;

pub use branch::{create_branch, get_current_branch, list_branches, switch_branch};
pub use command_log::get_command_log;
pub use diff::{get_file_diff, read_file_contents};
pub use history::{get_commit_diff, get_commit_file_diff, get_commit_log, get_file_at_commit, list_commit_files};
pub use repo::open_repository;
pub use staging::{commit, get_status, stage_files, unstage_files};
