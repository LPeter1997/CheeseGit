use std::collections::HashSet;
use std::path::Path;
use std::process::Command;

use crate::command_log::CommandLog;
use crate::error::AppError;
use crate::vcs::git::cli;
use crate::vcs::traits::VcsProvider;
use crate::vcs::types::{
    BranchGraphData, BranchInfo, BranchTrackingStatus, CommitInfo, DiffArea, DiffHunk, DiffLine,
    DiffLineKind, FileDiff, FileStatus, GraphCommit, LineSelection, RemoteInfo, RepoInfo,
    RepoStatus, StatusEntry,
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
        let output = cli::run_git_background(repo_path, &["rev-parse", "--abbrev-ref", "HEAD"], &self.log)?;

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
        let output = cli::run_git_background(
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
        let output = cli::run_git_background(
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

    fn diff_commit(&self, repo_path: &Path, hash: &str) -> Result<Vec<FileDiff>, AppError> {
        // For the root commit (no parent), use --root flag with diff-tree.
        // For normal commits, diff against parent.
        let output = cli::run_git(
            repo_path,
            &["diff-tree", "-p", "--root", "--no-commit-id", hash],
            &self.log,
        )?;

        if output.exit_code != 0 {
            return Err(AppError::Git(format!(
                "Failed to get commit diff: {}",
                output.stderr.trim()
            )));
        }

        Ok(parse_multi_file_diff(&output.stdout))
    }

    fn list_commit_files(&self, repo_path: &Path, hash: &str) -> Result<Vec<StatusEntry>, AppError> {
        let output = cli::run_git(
            repo_path,
            &["diff-tree", "--no-commit-id", "--name-status", "-r", "--root", hash],
            &self.log,
        )?;

        if output.exit_code != 0 {
            return Err(AppError::Git(format!(
                "Failed to list commit files: {}",
                output.stderr.trim()
            )));
        }

        let mut entries = Vec::new();
        for line in output.stdout.lines() {
            if line.is_empty() {
                continue;
            }
            // Format: "M\tpath/to/file" or "A\tpath/to/file"
            let parts: Vec<&str> = line.splitn(2, '\t').collect();
            if parts.len() < 2 {
                continue;
            }
            let status_char = parts[0].as_bytes().first().copied().unwrap_or(b'?');
            entries.push(StatusEntry {
                path: parts[1].to_string(),
                status: parse_status_char(status_char),
            });
        }

        Ok(entries)
    }

    fn diff_commit_file(
        &self,
        repo_path: &Path,
        hash: &str,
        file_path: &str,
    ) -> Result<FileDiff, AppError> {
        let output = cli::run_git(
            repo_path,
            &["diff-tree", "-p", "--root", "--no-commit-id", hash, "--", file_path],
            &self.log,
        )?;

        if output.exit_code != 0 {
            return Err(AppError::Git(format!(
                "Failed to get commit file diff: {}",
                output.stderr.trim()
            )));
        }

        let hunks = parse_unified_diff(&output.stdout);
        Ok(FileDiff {
            path: file_path.to_string(),
            hunks,
        })
    }

    fn show_file_at_commit(
        &self,
        repo_path: &Path,
        hash: &str,
        file_path: &str,
    ) -> Result<String, AppError> {
        let rev_path = format!("{hash}:{file_path}");
        let output = cli::run_git(repo_path, &["show", &rev_path], &self.log)?;

        if output.exit_code != 0 {
            return Err(AppError::Git(format!(
                "Failed to read file at revision: {}",
                output.stderr.trim()
            )));
        }

        Ok(output.stdout)
    }

    fn stage_lines(
        &self,
        repo_path: &Path,
        file_path: &str,
        diff: &FileDiff,
        selections: &[LineSelection],
    ) -> Result<(), AppError> {
        let patch = build_partial_patch(file_path, diff, selections, false);
        if patch.is_empty() {
            return Ok(());
        }
        apply_patch(repo_path, &patch, false, &self.log)
    }

    fn unstage_lines(
        &self,
        repo_path: &Path,
        file_path: &str,
        diff: &FileDiff,
        selections: &[LineSelection],
    ) -> Result<(), AppError> {
        let patch = build_partial_patch(file_path, diff, selections, true);
        if patch.is_empty() {
            return Ok(());
        }
        apply_patch(repo_path, &patch, true, &self.log)
    }

    fn list_remotes(&self, repo_path: &Path) -> Result<Vec<RemoteInfo>, AppError> {
        let output = cli::run_git(repo_path, &["remote", "-v"], &self.log)?;

        if output.exit_code != 0 {
            return Err(AppError::Git(format!(
                "Failed to list remotes: {}",
                output.stderr.trim()
            )));
        }

        let mut remotes = Vec::new();
        let mut seen = std::collections::HashSet::new();

        for line in output.stdout.lines() {
            // Format: "origin\thttps://... (fetch)"
            let parts: Vec<&str> = line.split_whitespace().collect();
            if parts.len() >= 2 {
                let name = parts[0].to_string();
                if seen.insert(name.clone()) {
                    remotes.push(RemoteInfo {
                        name,
                        url: parts[1].to_string(),
                    });
                }
            }
        }

        Ok(remotes)
    }

    fn branch_tracking_status(
        &self,
        repo_path: &Path,
    ) -> Result<Option<BranchTrackingStatus>, AppError> {
        // Get the upstream ref for the current branch.
        let output = cli::run_git_background(
            repo_path,
            &["rev-parse", "--abbrev-ref", "--symbolic-full-name", "@{u}"],
            &self.log,
        )?;

        if output.exit_code != 0 {
            // No upstream configured.
            return Ok(None);
        }

        let upstream = output.stdout.trim().to_string();
        if upstream.is_empty() {
            return Ok(None);
        }

        // Get ahead/behind counts.
        let rev_range = format!("@{{u}}...HEAD");
        let output = cli::run_git_background(
            repo_path,
            &["rev-list", "--left-right", "--count", &rev_range],
            &self.log,
        )?;

        if output.exit_code != 0 {
            return Ok(Some(BranchTrackingStatus {
                ahead: 0,
                behind: 0,
                upstream,
            }));
        }

        let parts: Vec<&str> = output.stdout.trim().split('\t').collect();
        let behind = parts.first().and_then(|s| s.parse().ok()).unwrap_or(0);
        let ahead = parts.get(1).and_then(|s| s.parse().ok()).unwrap_or(0);

        Ok(Some(BranchTrackingStatus {
            ahead,
            behind,
            upstream,
        }))
    }

    fn push(&self, repo_path: &Path, remote: &str) -> Result<(), AppError> {
        let output = cli::run_git(repo_path, &["push", remote], &self.log)?;

        if output.exit_code != 0 {
            return Err(AppError::Git(format!(
                "Failed to push: {}",
                output.stderr.trim()
            )));
        }

        Ok(())
    }

    fn publish_branch(&self, repo_path: &Path, remote: &str) -> Result<(), AppError> {
        let output = cli::run_git(repo_path, &["push", "--set-upstream", remote, "HEAD"], &self.log)?;

        if output.exit_code != 0 {
            return Err(AppError::Git(format!(
                "Failed to publish branch: {}",
                output.stderr.trim()
            )));
        }

        Ok(())
    }

    fn pull(&self, repo_path: &Path, remote: &str) -> Result<(), AppError> {
        let output = cli::run_git(repo_path, &["pull", remote], &self.log)?;

        if output.exit_code != 0 {
            return Err(AppError::Git(format!(
                "Failed to pull: {}",
                output.stderr.trim()
            )));
        }

        Ok(())
    }

    fn fetch(&self, repo_path: &Path, remote: &str) -> Result<(), AppError> {
        let output = cli::run_git(repo_path, &["fetch", remote], &self.log)?;

        if output.exit_code != 0 {
            return Err(AppError::Git(format!(
                "Failed to fetch: {}",
                output.stderr.trim()
            )));
        }

        Ok(())
    }

    fn branch_graph(
        &self,
        repo_path: &Path,
        branches: &[&str],
        remote: Option<&str>,
        max_commits: Option<u32>,
    ) -> Result<BranchGraphData, AppError> {
        // Build the branch arguments for git log.
        let mut args: Vec<&str> = vec!["log", "--topo-order"];

        // Limit the number of commits if requested.
        let max_count_str;
        if let Some(max) = max_commits {
            max_count_str = format!("--max-count={}", max);
            args.push(&max_count_str);
        }

        // Collect the actual branch names to include.
        let mut branch_names: Vec<String> = if branches.is_empty() {
            // Get all local branches.
            let output = cli::run_git_background(
                repo_path,
                &["for-each-ref", "--format=%(refname:short)", "refs/heads/"],
                &self.log,
            )?;
            output
                .stdout
                .lines()
                .filter(|l| !l.is_empty())
                .map(|l| l.to_string())
                .collect()
        } else {
            branches.iter().map(|b| b.to_string()).collect()
        };

        // Include the remote's default branch so we can visualize fork points.
        if let Some(remote_name) = remote {
            // Try symbolic-ref first (e.g. origin/HEAD → origin/master).
            let head_output = cli::run_git_background(
                repo_path,
                &[
                    "symbolic-ref",
                    "--short",
                    &format!("refs/remotes/{remote_name}/HEAD"),
                ],
                &self.log,
            );
            let mut added = false;
            if let Ok(o) = &head_output {
                if o.exit_code == 0 {
                    let r = o.stdout.trim().to_string();
                    if !r.is_empty() && !branch_names.contains(&r) {
                        branch_names.push(r);
                        added = true;
                    }
                }
            }
            // Fallback: try origin/main then origin/master.
            if !added {
                for name in &["main", "master"] {
                    let ref_name = format!("{remote_name}/{name}");
                    let check = cli::run_git_background(
                        repo_path,
                        &["rev-parse", "--verify", &format!("refs/remotes/{ref_name}")],
                        &self.log,
                    );
                    if let Ok(o) = check {
                        if o.exit_code == 0 && !branch_names.contains(&ref_name) {
                            branch_names.push(ref_name);
                            break;
                        }
                    }
                }
            }
        }

        // Add each branch as a ref to traverse.
        let branch_refs: Vec<String> = branch_names.clone();
        for b in &branch_refs {
            args.push(b);
        }

        // NUL-delimited format: hash, short_hash, parents, summary, author, date, decorations
        let format = "--format=%H%x00%h%x00%P%x00%s%x00%an%x00%aI%x00%D";
        args.push(format);

        let output = cli::run_git_background(repo_path, &args, &self.log)?;

        if output.exit_code != 0 {
            return Err(AppError::Git(format!(
                "Failed to get branch graph: {}",
                output.stderr.trim()
            )));
        }

        let mut commits = Vec::new();
        for line in output.stdout.lines() {
            if line.is_empty() {
                continue;
            }
            let parts: Vec<&str> = line.split('\0').collect();
            if parts.len() < 7 {
                continue;
            }
            let parents: Vec<String> = parts[2]
                .split_whitespace()
                .map(|s| s.to_string())
                .collect();
            let refs: Vec<String> = if parts[6].is_empty() {
                Vec::new()
            } else {
                parts[6]
                    .split(", ")
                    .map(|r| {
                        // Strip prefixes like "HEAD -> "
                        r.strip_prefix("HEAD -> ").unwrap_or(r).to_string()
                    })
                    .collect()
            };
            commits.push(GraphCommit {
                hash: parts[0].to_string(),
                short_hash: parts[1].to_string(),
                summary: parts[3].to_string(),
                author: parts[4].to_string(),
                timestamp: parts[5].to_string(),
                parents,
                refs,
            });
        }

        // Determine local-only commits per branch that has a remote tracking ref.
        let mut local_only: HashSet<String> = HashSet::new();
        if let Some(remote_name) = remote {
            for branch in &branch_names {
                let range = format!("{remote_name}/{branch}..{branch}");
                let lo_output = cli::run_git_background(
                    repo_path,
                    &["log", "--format=%H", &range],
                    &self.log,
                );
                if let Ok(lo) = lo_output {
                    if lo.exit_code == 0 {
                        for h in lo.stdout.lines() {
                            let h = h.trim();
                            if !h.is_empty() {
                                local_only.insert(h.to_string());
                            }
                        }
                    }
                }
            }
        }

        Ok(BranchGraphData {
            commits,
            branches: branch_names,
            local_only_commits: local_only.into_iter().collect(),
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

/// Parse a multi-file unified diff (e.g. from `git diff-tree -p`) into per-file diffs.
fn parse_multi_file_diff(raw: &str) -> Vec<FileDiff> {
    let mut files: Vec<FileDiff> = Vec::new();
    let mut current_path: Option<String> = None;
    let mut current_raw = String::new();

    for line in raw.lines() {
        if line.starts_with("diff --git ") {
            // Flush previous file.
            if let Some(path) = current_path.take() {
                let hunks = parse_unified_diff(&current_raw);
                files.push(FileDiff { path, hunks });
            }
            current_raw.clear();

            // Extract path from "diff --git a/path b/path".
            let path = line
                .split(" b/")
                .last()
                .unwrap_or("")
                .to_string();
            current_path = Some(path);
        } else {
            current_raw.push_str(line);
            current_raw.push('\n');
        }
    }

    // Flush last file.
    if let Some(path) = current_path.take() {
        let hunks = parse_unified_diff(&current_raw);
        files.push(FileDiff { path, hunks });
    }

    files
}

/// Build a partial unified diff patch from selected lines.
///
/// The resulting patch is a valid unified diff that can be piped to `git apply --cached`.
/// Lines not in `selections` are converted to context lines to maintain correct offsets.
///
/// When `reverse` is false (staging from working tree):
///   - Non-selected additions are omitted (they don't exist in old side).
///   - Non-selected deletions become context (they exist in old side).
///
/// When `reverse` is true (unstaging from index):
///   - Non-selected additions become context (they exist in new/index side).
///   - Non-selected deletions are omitted (they don't exist in new/index side).
pub fn build_partial_patch(
    file_path: &str,
    diff: &FileDiff,
    selections: &[LineSelection],
    reverse: bool,
) -> String {
    use std::collections::HashSet;

    let selected: HashSet<(u32, u32)> = selections
        .iter()
        .map(|s| (s.hunk_index, s.line_index))
        .collect();

    let mut patch = String::new();
    patch.push_str(&format!("--- a/{file_path}\n"));
    patch.push_str(&format!("+++ b/{file_path}\n"));

    for (hunk_idx, hunk) in diff.hunks.iter().enumerate() {
        // Check if any line in this hunk is selected.
        let hunk_has_selection = hunk
            .lines
            .iter()
            .enumerate()
            .any(|(line_idx, line)| {
                matches!(line.kind, DiffLineKind::Addition | DiffLineKind::Deletion)
                    && selected.contains(&(hunk_idx as u32, line_idx as u32))
            });

        if !hunk_has_selection {
            continue;
        }

        // Build the hunk with non-selected change lines converted to context.
        let mut hunk_lines = Vec::new();
        let mut old_count: u32 = 0;
        let mut new_count: u32 = 0;

        for (line_idx, line) in hunk.lines.iter().enumerate() {
            let is_selected = selected.contains(&(hunk_idx as u32, line_idx as u32));

            match line.kind {
                DiffLineKind::Context => {
                    hunk_lines.push(format!(" {}\n", line.content));
                    old_count += 1;
                    new_count += 1;
                }
                DiffLineKind::Addition => {
                    if is_selected {
                        hunk_lines.push(format!("+{}\n", line.content));
                        new_count += 1;
                    } else if reverse {
                        // Unstaging: non-selected addition stays in index → context.
                        hunk_lines.push(format!(" {}\n", line.content));
                        old_count += 1;
                        new_count += 1;
                    } else {
                        // Staging: non-selected addition doesn't exist in old → omit.
                    }
                }
                DiffLineKind::Deletion => {
                    if is_selected {
                        hunk_lines.push(format!("-{}\n", line.content));
                        old_count += 1;
                    } else if reverse {
                        // Unstaging: non-selected deletion doesn't exist in index → omit.
                    } else {
                        // Staging: non-selected deletion exists in old → context.
                        hunk_lines.push(format!(" {}\n", line.content));
                        old_count += 1;
                        new_count += 1;
                    }
                }
            }
        }

        // Write hunk header.
        patch.push_str(&format!(
            "@@ -{},{} +{},{} @@\n",
            hunk.old_start, old_count, hunk.old_start, new_count
        ));
        for hl in &hunk_lines {
            patch.push_str(hl);
        }
    }

    patch
}

/// Apply a patch to the git index via `git apply --cached`.
/// If `reverse` is true, applies with `--reverse` (for unstaging).
fn apply_patch(
    repo_path: &Path,
    patch: &str,
    reverse: bool,
    log: &CommandLog,
) -> Result<(), AppError> {
    use std::io::Write;

    let mut args = vec!["apply", "--cached", "--unidiff-zero", "--allow-empty"];
    if reverse {
        args.push("--reverse");
    }
    args.push("-");

    let cmd_string = format!("git {}", args.join(" "));
    tracing::debug!(cmd = %cmd_string, cwd = %repo_path.display(), "applying patch");

    let start = std::time::Instant::now();

    let mut child = Command::new("git")
        .args(&args[..args.len() - 1]) // Exclude the "-" we used for display
        .current_dir(repo_path)
        .stdin(std::process::Stdio::piped())
        .stdout(std::process::Stdio::piped())
        .stderr(std::process::Stdio::piped())
        .spawn()
        .map_err(|e| AppError::Io(format!("failed to spawn git apply: {e}")))?;

    if let Some(ref mut stdin) = child.stdin {
        stdin
            .write_all(patch.as_bytes())
            .map_err(|e| AppError::Io(format!("failed to write patch to stdin: {e}")))?;
    }
    // Drop stdin to signal EOF.
    drop(child.stdin.take());

    let output = child
        .wait_with_output()
        .map_err(|e| AppError::Io(format!("failed to wait on git apply: {e}")))?;

    let exit_code = output.status.code().unwrap_or(-1);
    let stdout = String::from_utf8_lossy(&output.stdout).into_owned();
    let stderr = String::from_utf8_lossy(&output.stderr).into_owned();

    log.record(
        &cmd_string,
        &repo_path.display().to_string(),
        exit_code,
        &stdout,
        &stderr,
        start.elapsed().as_millis() as u32,
        false,
    );

    if exit_code != 0 {
        return Err(AppError::Git(format!(
            "Failed to apply patch: {}",
            stderr.trim()
        )));
    }

    Ok(())
}
