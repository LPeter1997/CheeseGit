use std::path::Path;

use crate::command_log::CommandLog;
use crate::error::AppError;
use crate::vcs::git::cli;
use crate::vcs::traits::VcsProvider;
use crate::vcs::types::{
    BranchInfo, CommitInfo, DiffArea, DiffHunk, DiffLine, DiffLineKind, FileStatus, FileDiff,
    RepoInfo, RepoStatus, StatusEntry,
};

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

    fn list_branches(&self, repo_path: &Path) -> Result<Vec<BranchInfo>, AppError> {
        // Use for-each-ref to get branches sorted by most recent commit date.
        let format = "%(refname:short)%00%(HEAD)%00%(committerdate:iso-strict)";
        let output = cli::run_git(
            repo_path,
            &[
                "for-each-ref",
                "--sort=-committerdate",
                &format!("--format={format}"),
                "refs/heads/",
            ],
            &self.log,
        )?;

        if output.exit_code != 0 {
            return Err(AppError::Git(format!(
                "Failed to list branches: {}",
                output.stderr.trim()
            )));
        }

        let mut branches = Vec::new();
        for line in output.stdout.lines() {
            if line.is_empty() {
                continue;
            }
            let parts: Vec<&str> = line.split('\0').collect();
            if parts.len() < 3 {
                continue;
            }
            branches.push(BranchInfo {
                name: parts[0].to_string(),
                is_current: parts[1].trim() == "*",
                last_commit_date: parts[2].to_string(),
            });
        }

        Ok(branches)
    }

    fn switch_branch(&self, repo_path: &Path, branch_name: &str) -> Result<(), AppError> {
        let output = cli::run_git(repo_path, &["switch", branch_name], &self.log)?;

        if output.exit_code != 0 {
            return Err(AppError::Git(format!(
                "Failed to switch branch: {}",
                output.stderr.trim()
            )));
        }

        Ok(())
    }

    fn create_branch(&self, repo_path: &Path, branch_name: &str) -> Result<(), AppError> {
        let output = cli::run_git(repo_path, &["switch", "-c", branch_name], &self.log)?;

        if output.exit_code != 0 {
            return Err(AppError::Git(format!(
                "Failed to create branch: {}",
                output.stderr.trim()
            )));
        }

        Ok(())
    }

    fn status(&self, repo_path: &Path) -> Result<RepoStatus, AppError> {
        let output = cli::run_git(
            repo_path,
            &["status", "--porcelain=v1", "-z", "--untracked-files=all"],
            &self.log,
        )?;

        if output.exit_code != 0 {
            return Err(AppError::Git(format!(
                "Failed to get status: {}",
                output.stderr.trim()
            )));
        }

        let mut staged = Vec::new();
        let mut unstaged = Vec::new();

        // -z uses NUL as delimiter. Split on NUL, skip empties.
        let entries: Vec<&str> = output.stdout.split('\0').collect();
        let mut i = 0;
        while i < entries.len() {
            let entry = entries[i];
            if entry.len() < 4 {
                i += 1;
                continue;
            }

            let index_status = entry.as_bytes()[0];
            let worktree_status = entry.as_bytes()[1];
            let path = entry[3..].to_string();

            // Renames/copies have an extra NUL-separated field (the old path) — skip it.
            if index_status == b'R' || index_status == b'C' {
                i += 1; // skip the "from" path
            }

            if index_status != b' ' && index_status != b'?' {
                staged.push(StatusEntry {
                    path: path.clone(),
                    status: parse_status_char(index_status),
                });
            }

            if worktree_status != b' ' {
                unstaged.push(StatusEntry {
                    path: path.clone(),
                    status: if index_status == b'?' {
                        FileStatus::Untracked
                    } else {
                        parse_status_char(worktree_status)
                    },
                });
            }

            i += 1;
        }

        Ok(RepoStatus { staged, unstaged })
    }

    fn commit(&self, repo_path: &Path, summary: &str, description: &str) -> Result<(), AppError> {
        let mut args = vec!["commit", "-m", summary];
        if !description.is_empty() {
            args.push("-m");
            args.push(description);
        }
        let output = cli::run_git(repo_path, &args, &self.log)?;

        if output.exit_code != 0 {
            return Err(AppError::Git(format!(
                "Failed to commit: {}",
                output.stderr.trim()
            )));
        }

        Ok(())
    }

    fn stage_files(&self, repo_path: &Path, paths: &[&str]) -> Result<(), AppError> {
        let mut args = vec!["add", "--"];
        args.extend(paths);
        let output = cli::run_git(repo_path, &args, &self.log)?;

        if output.exit_code != 0 {
            return Err(AppError::Git(format!(
                "Failed to stage files: {}",
                output.stderr.trim()
            )));
        }

        Ok(())
    }

    fn unstage_files(&self, repo_path: &Path, paths: &[&str]) -> Result<(), AppError> {
        let mut args = vec!["reset", "HEAD", "--"];
        args.extend(paths);
        let output = cli::run_git(repo_path, &args, &self.log)?;

        if output.exit_code != 0 {
            return Err(AppError::Git(format!(
                "Failed to unstage files: {}",
                output.stderr.trim()
            )));
        }

        Ok(())
    }

    fn diff_file(
        &self,
        repo_path: &Path,
        file_path: &str,
        area: DiffArea,
    ) -> Result<FileDiff, AppError> {
        let mut args = vec!["diff"];
        if matches!(area, DiffArea::Staged) {
            args.push("--cached");
        }
        args.push("--");
        args.push(file_path);

        let output = cli::run_git(repo_path, &args, &self.log)?;

        if output.exit_code != 0 {
            return Err(AppError::Git(format!(
                "Failed to get diff: {}",
                output.stderr.trim()
            )));
        }

        let hunks = parse_unified_diff(&output.stdout);

        Ok(FileDiff {
            path: file_path.to_string(),
            hunks,
        })
    }
}

fn parse_status_char(c: u8) -> FileStatus {
    match c {
        b'A' => FileStatus::Added,
        b'M' => FileStatus::Modified,
        b'D' => FileStatus::Deleted,
        b'R' => FileStatus::Renamed,
        b'C' => FileStatus::Copied,
        b'?' => FileStatus::Untracked,
        _ => FileStatus::Unknown,
    }
}

/// Parse a unified diff string into a list of hunks.
fn parse_unified_diff(raw: &str) -> Vec<DiffHunk> {
    let mut hunks = Vec::new();
    let mut current_hunk: Option<DiffHunk> = None;
    let mut old_line: u32 = 0;
    let mut new_line: u32 = 0;

    for line in raw.lines() {
        if let Some(rest) = line.strip_prefix("@@ ") {
            // Flush the previous hunk.
            if let Some(hunk) = current_hunk.take() {
                hunks.push(hunk);
            }

            // Parse "@@ -old_start,old_count +new_start,new_count @@"
            let (old_start, new_start) = parse_hunk_header(rest);
            old_line = old_start;
            new_line = new_start;

            current_hunk = Some(DiffHunk {
                header: format!("@@ {rest}"),
                old_start,
                new_start,
                lines: Vec::new(),
            });
            continue;
        }

        let Some(hunk) = current_hunk.as_mut() else {
            // Lines before the first hunk header (file header lines) — skip.
            continue;
        };

        if let Some(content) = line.strip_prefix('+') {
            hunk.lines.push(DiffLine {
                kind: DiffLineKind::Addition,
                content: content.to_string(),
                old_lineno: None,
                new_lineno: Some(new_line),
            });
            new_line += 1;
        } else if let Some(content) = line.strip_prefix('-') {
            hunk.lines.push(DiffLine {
                kind: DiffLineKind::Deletion,
                content: content.to_string(),
                old_lineno: Some(old_line),
                new_lineno: None,
            });
            old_line += 1;
        } else if line == "\\ No newline at end of file" {
            // Skip the "no newline" marker.
        } else {
            // Context line (starts with space or is empty for blank lines).
            let content = line.strip_prefix(' ').unwrap_or(line);
            hunk.lines.push(DiffLine {
                kind: DiffLineKind::Context,
                content: content.to_string(),
                old_lineno: Some(old_line),
                new_lineno: Some(new_line),
            });
            old_line += 1;
            new_line += 1;
        }
    }

    if let Some(hunk) = current_hunk {
        hunks.push(hunk);
    }

    hunks
}

/// Extract (old_start, new_start) from the remainder after "@@ ".
fn parse_hunk_header(header: &str) -> (u32, u32) {
    // Format: "-old_start,old_count +new_start,new_count @@ optional context"
    let mut old_start = 1u32;
    let mut new_start = 1u32;

    for part in header.split_whitespace() {
        if let Some(rest) = part.strip_prefix('-') {
            old_start = rest
                .split(',')
                .next()
                .and_then(|s| s.parse().ok())
                .unwrap_or(1);
        } else if let Some(rest) = part.strip_prefix('+') {
            new_start = rest
                .split(',')
                .next()
                .and_then(|s| s.parse().ok())
                .unwrap_or(1);
        } else if part == "@@" {
            break;
        }
    }

    (old_start, new_start)
}
