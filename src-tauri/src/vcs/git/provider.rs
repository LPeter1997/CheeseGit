use std::path::Path;

use crate::command_log::CommandLog;
use crate::error::AppError;
use crate::vcs::git::cli;
use crate::vcs::traits::VcsProvider;
use crate::vcs::types::{CommitInfo, RepoInfo};

pub struct GitProvider {
    log: CommandLog,
}

impl GitProvider {
    pub fn new(log: CommandLog) -> Self {
        Self { log }
    }
}

impl VcsProvider for GitProvider {
    fn open_repository(&self, path: &Path) -> Result<RepoInfo, AppError> {
        let output = cli::run_git(path, &["rev-parse", "--show-toplevel"], &self.log)?;

        if output.exit_code != 0 {
            return Err(AppError::Git(format!(
                "Not a git repository: {}",
                path.display()
            )));
        }

        let root = output.stdout.trim().to_string();
        let name = Path::new(&root)
            .file_name()
            .map(|n| n.to_string_lossy().into_owned())
            .unwrap_or_else(|| root.clone());

        Ok(RepoInfo {
            name,
            path: root,
        })
    }

    fn current_branch(&self, repo_path: &Path) -> Result<String, AppError> {
        let output = cli::run_git(repo_path, &["rev-parse", "--abbrev-ref", "HEAD"], &self.log)?;

        if output.exit_code != 0 {
            return Err(AppError::Git(format!(
                "Failed to get current branch: {}",
                output.stderr.trim()
            )));
        }

        Ok(output.stdout.trim().to_string())
    }

    fn commit_log(&self, repo_path: &Path, limit: u32) -> Result<Vec<CommitInfo>, AppError> {
        // Use a NUL-delimited format for reliable parsing.
        let format = "%H%x00%h%x00%s%x00%an%x00%aI";
        let limit_arg = format!("-{limit}");
        let output = cli::run_git(
            repo_path,
            &["log", &limit_arg, &format!("--format={format}")],
            &self.log,
        )?;

        if output.exit_code != 0 {
            return Err(AppError::Git(format!(
                "Failed to get commit log: {}",
                output.stderr.trim()
            )));
        }

        let mut commits = Vec::new();
        for line in output.stdout.lines() {
            if line.is_empty() {
                continue;
            }
            let parts: Vec<&str> = line.split('\0').collect();
            if parts.len() < 5 {
                continue;
            }
            commits.push(CommitInfo {
                hash: parts[0].to_string(),
                short_hash: parts[1].to_string(),
                summary: parts[2].to_string(),
                author: parts[3].to_string(),
                timestamp: parts[4].to_string(),
            });
        }

        Ok(commits)
    }
}
