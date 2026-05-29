use std::collections::HashSet;
use std::path::Path;
use std::process::Command;

use crate::command_log::CommandLog;
use crate::error::AppError;
use crate::vcs::git::cli;
use crate::vcs::traits::VcsProvider;
use crate::vcs::types::{
    BranchDeleteInfo, BranchGraphData, BranchInfo, BranchTrackingStatus, CommitInfo, ConflictResolution, DiffArea,
    DiffHunk, DiffLine, DiffLineKind, FileConflictInfo, FileDiff, FileStats, FileStatus, GraphCommit, HeadState,
    InlineHighlight, LineSelection, MergeConflictInfo, MergeResult, RemoteInfo, RepoInfo, RepoStatus, RestoredCommitMessage, RevertResult, StashEntry, StatusEntry,
};

pub struct GitProvider {
    log: CommandLog,
}

impl GitProvider {
    pub fn new(log: CommandLog) -> Self {
        Self { log }
    }
}

/// Check git command success; return typed error on failure.
fn check_git_success(output: &cli::GitOutput, operation: &str) -> Result<(), AppError> {
    if output.exit_code != 0 {
        Err(AppError::Git(format!(
            "Failed to {}: {}",
            operation,
            output.stderr.trim()
        )))
    } else {
        Ok(())
    }
}

/// Detect if a git operation error is an SSH authentication failure.
fn is_ssh_auth_error(stderr: &str) -> bool {
    let s = stderr.trim();
    s.contains("Permission denied (publickey)")
        || s.contains("Could not read from remote repository")
        || s.contains("Host key verification failed")
        || s.contains("please make sure you have the correct access rights")
        || s.contains("no matching host key type found")
}

/// Classify a git operation error. Returns `SshAuthRequired` if the error
/// looks like an SSH authentication failure, otherwise `Git`.
fn classify_git_error(operation: &str, stderr: &str) -> AppError {
    if is_ssh_auth_error(stderr) {
        // Drop cached passphrase on SSH auth failures so subsequent
        // attempts must provide fresh credentials.
        cli::set_ssh_passphrase(None);
        AppError::SshAuthRequired(format!("Failed to {}: {}", operation, stderr.trim()))
    } else {
        AppError::Git(format!("Failed to {}: {}", operation, stderr.trim()))
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

        Ok(RepoInfo { name, path: root })
    }

    fn current_branch(&self, repo_path: &Path) -> Result<String, AppError> {
        let output =
            cli::run_git_background(repo_path, &["rev-parse", "--abbrev-ref", "HEAD"], &self.log)?;

        check_git_success(&output, "get current branch")?;

        Ok(output.stdout.trim().to_string())
    }

    fn head_state(&self, repo_path: &Path) -> Result<HeadState, AppError> {
        // Check if HEAD points at a symbolic ref (i.e. a branch).
        let sym_output =
            cli::run_git_background(repo_path, &["symbolic-ref", "-q", "HEAD"], &self.log)?;

        if sym_output.exit_code == 0 {
            // On a branch — extract short name from refs/heads/<name>
            let full_ref = sym_output.stdout.trim();
            let branch = full_ref.strip_prefix("refs/heads/").unwrap_or(full_ref);
            return Ok(HeadState {
                branch: Some(branch.to_string()),
                browsing_history: false,
            });
        }

        // Detached: find the most likely context branch.
        let output = cli::run_git_background(
            repo_path,
            &[
                "branch",
                "--contains",
                "HEAD",
                "--sort=-committerdate",
                "--format=%(refname:short)",
            ],
            &self.log,
        )?;

        let context_branch = if output.exit_code == 0 {
            output
                .stdout
                .lines()
                .find(|l| {
                    let trimmed = l.trim();
                    !trimmed.is_empty() && !trimmed.starts_with('(')
                })
                .map(|b| b.trim().to_string())
        } else {
            None
        };

        Ok(HeadState {
            branch: context_branch,
            browsing_history: true,
        })
    }

    fn checkout_commit(&self, repo_path: &Path, hash: &str) -> Result<(), AppError> {
        let output = cli::run_git(repo_path, &["checkout", hash], &self.log)?;

        check_git_success(&output, "checkout commit")?;

        Ok(())
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

        check_git_success(&output, "get commit log")?;

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

        check_git_success(&output, "list branches")?;

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

        check_git_success(&output, "switch branch")?;

        Ok(())
    }

    fn create_branch(&self, repo_path: &Path, branch_name: &str) -> Result<(), AppError> {
        let output = cli::run_git(repo_path, &["switch", "-c", branch_name], &self.log)?;

        check_git_success(&output, "create branch")?;

        Ok(())
    }

    fn delete_branch(
        &self,
        repo_path: &Path,
        branch_name: &str,
        force: bool,
    ) -> Result<(), AppError> {
        let flag = if force { "-D" } else { "-d" };
        let output = cli::run_git(repo_path, &["branch", flag, branch_name], &self.log)?;

        check_git_success(&output, "delete branch")?;

        Ok(())
    }

    fn delete_remote_branch(
        &self,
        repo_path: &Path,
        remote: &str,
        branch_name: &str,
    ) -> Result<(), AppError> {
        let output = cli::run_git(
            repo_path,
            &["push", remote, "--delete", branch_name],
            &self.log,
        )?;

        if output.exit_code != 0 {
            return Err(classify_git_error("delete remote branch", &output.stderr));
        }

        Ok(())
    }

    fn branch_delete_info(
        &self,
        repo_path: &Path,
        branch_name: &str,
    ) -> Result<BranchDeleteInfo, AppError> {
        // Check upstream tracking reference for this branch.
        // Format: "%(upstream:remotename)" gives "origin", "%(upstream:remoteref)" gives "refs/heads/foo"
        let upstream_ref = format!("refs/heads/{}", branch_name);
        let output = cli::run_git_background(
            repo_path,
            &[
                "for-each-ref",
                "--format=%(upstream:remotename)%00%(upstream:lstrip=3)",
                &upstream_ref,
            ],
            &self.log,
        )?;
        let parts: Vec<&str> = output.stdout.trim().split('\0').collect();
        let remote_name_raw = parts.first().map(|s| s.trim()).unwrap_or("");
        let remote_branch_raw = parts.get(1).map(|s| s.trim()).unwrap_or("");

        if remote_name_raw.is_empty() || remote_branch_raw.is_empty() {
            // No upstream configured at all.
            return Ok(BranchDeleteInfo {
                exists_on_remote: false,
                remote_name: None,
                remote_branch_name: None,
            });
        }

        let remote_name = remote_name_raw.to_string();
        let remote_branch_name = remote_branch_raw.to_string();

        // Check if the branch actually still exists on the remote via ls-remote.
        let remote_ref = format!("refs/heads/{}", remote_branch_name);
        let output = cli::run_git_background(
            repo_path,
            &["ls-remote", "--heads", &remote_name, &remote_ref],
            &self.log,
        )?;
        let exists_on_remote = output.exit_code == 0 && !output.stdout.trim().is_empty();

        Ok(BranchDeleteInfo {
            exists_on_remote,
            remote_name: Some(remote_name),
            remote_branch_name: Some(remote_branch_name),
        })
    }

    fn status(&self, repo_path: &Path) -> Result<RepoStatus, AppError> {
        let output = cli::run_git_background(
            repo_path,
            &["status", "--porcelain=v1", "-z", "--untracked-files=all"],
            &self.log,
        )?;

        check_git_success(&output, "get status")?;

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

            // Nested git repositories can appear as directory-only entries like
            // "?? .tmp-test-repo/" even with --untracked-files=all. The staging
            // UI only operates on files, so drop directory placeholders here.
            if path.ends_with('/') {
                i += 1;
                continue;
            }

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

    fn commit(
        &self,
        repo_path: &Path,
        summary: &str,
        description: &str,
        allow_empty: bool,
    ) -> Result<(), AppError> {
        let mut args = vec!["commit", "-m", summary];
        if !description.is_empty() {
            args.push("-m");
            args.push(description);
        }
        if allow_empty {
            args.push("--allow-empty");
        }
        let output = cli::run_git(repo_path, &args, &self.log)?;

        check_git_success(&output, "commit")?;

        Ok(())
    }

    fn undo_last_commit(&self, repo_path: &Path) -> Result<RestoredCommitMessage, AppError> {
        let branch_output =
            cli::run_git_background(repo_path, &["rev-parse", "--abbrev-ref", "HEAD"], &self.log)?;
        check_git_success(&branch_output, "get current branch")?;
        if branch_output.stdout.trim() == "HEAD" {
            return Err(AppError::Git(
                "Cannot undo commit while browsing history (detached HEAD).".to_string(),
            ));
        }

        // Ensure this is not the initial commit, because HEAD~1 does not exist.
        let parent_check = cli::run_git_background(repo_path, &["rev-parse", "--verify", "HEAD~1"], &self.log)?;
        if parent_check.exit_code != 0 {
            return Err(AppError::Git(
                "Cannot undo the initial commit.".to_string(),
            ));
        }

        // Disallow rewriting history only when HEAD is present on a currently
        // configured remote-tracking ref. This intentionally ignores orphaned
        // refs/remotes/* entries from removed remotes.
        let remotes_output =
            cli::run_git_background(repo_path, &["remote"], &self.log)?;
        check_git_success(&remotes_output, "list remotes")?;

        let remote_names: Vec<String> = remotes_output
            .stdout
            .lines()
            .map(|line| line.trim())
            .filter(|line| !line.is_empty())
            .map(ToOwned::to_owned)
            .collect();

        for remote in remote_names {
            let remote_ref = format!("refs/remotes/{remote}");
            let contains_output = cli::run_git_background(
                repo_path,
                &[
                    "for-each-ref",
                    "--contains",
                    "HEAD",
                    "--format=%(refname:short)",
                    &remote_ref,
                ],
                &self.log,
            )?;
            check_git_success(&contains_output, "check whether latest commit is pushed")?;
            if contains_output
                .stdout
                .lines()
                .any(|line| !line.trim().is_empty())
            {
                return Err(AppError::Git(
                    "Cannot undo because the latest commit is already pushed.".to_string(),
                ));
            }
        }

        let message_output =
            cli::run_git_background(repo_path, &["show", "-s", "--format=%B", "HEAD"], &self.log)?;
        check_git_success(&message_output, "read latest commit message")?;

        let raw_message = message_output.stdout.trim_end_matches('\n');
        let mut lines = raw_message.lines();
        let summary = lines.next().unwrap_or("").trim().to_string();
        let description = lines.collect::<Vec<_>>().join("\n").trim_start_matches('\n').to_string();

        let reset_output = cli::run_git(repo_path, &["reset", "--soft", "HEAD~1"], &self.log)?;
        check_git_success(&reset_output, "undo latest commit")?;

        Ok(RestoredCommitMessage {
            summary,
            description,
        })
    }

    fn stage_files(&self, repo_path: &Path, paths: &[&str]) -> Result<(), AppError> {
        let mut args = vec!["add", "--"];
        args.extend(paths);
        let output = cli::run_git(repo_path, &args, &self.log)?;

        check_git_success(&output, "stage files")?;

        Ok(())
    }

    fn unstage_files(&self, repo_path: &Path, paths: &[&str]) -> Result<(), AppError> {
        let mut args = vec!["reset", "HEAD", "--"];
        args.extend(paths);
        let output = cli::run_git(repo_path, &args, &self.log)?;

        check_git_success(&output, "unstage files")?;

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

        check_git_success(&output, "get diff")?;

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

        check_git_success(&output, "get commit diff")?;

        Ok(parse_multi_file_diff(&output.stdout))
    }

    fn list_commit_files(
        &self,
        repo_path: &Path,
        hash: &str,
    ) -> Result<Vec<StatusEntry>, AppError> {
        let output = cli::run_git(
            repo_path,
            &[
                "diff-tree",
                "--no-commit-id",
                "--name-status",
                "-r",
                "--root",
                hash,
            ],
            &self.log,
        )?;

        check_git_success(&output, "list commit files")?;

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
            &[
                "diff-tree",
                "-p",
                "--root",
                "--no-commit-id",
                hash,
                "--",
                file_path,
            ],
            &self.log,
        )?;

        check_git_success(&output, "get commit file diff")?;

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

        check_git_success(&output, "read file at revision")?;

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
        let output = cli::run_git_background(repo_path, &["remote", "-v"], &self.log)?;

        check_git_success(&output, "list remotes")?;

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

        // Prefer "origin" as the first entry regardless of alphabetical order.
        if let Some(idx) = remotes.iter().position(|r| r.name == "origin")
            && idx != 0
        {
            let origin = remotes.remove(idx);
            remotes.insert(0, origin);
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
        let rev_range = "@{u}...HEAD".to_string();
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

    fn remote_branch_status(
        &self,
        repo_path: &Path,
        remote: &str,
    ) -> Result<Option<BranchTrackingStatus>, AppError> {
        // Get the current branch name.
        let branch_output =
            cli::run_git_background(repo_path, &["rev-parse", "--abbrev-ref", "HEAD"], &self.log)?;
        if branch_output.exit_code != 0 {
            return Ok(None);
        }
        let branch = branch_output.stdout.trim();
        if branch.is_empty() {
            return Ok(None);
        }

        // Check if <remote>/<branch> exists.
        let remote_ref = format!("refs/remotes/{remote}/{branch}");
        let verify = cli::run_git_background(
            repo_path,
            &["rev-parse", "--verify", &remote_ref],
            &self.log,
        )?;
        if verify.exit_code != 0 {
            // Branch does not exist on this remote.
            return Ok(None);
        }

        // Calculate ahead/behind relative to <remote>/<branch>.
        let upstream = format!("{remote}/{branch}");
        let rev_range = format!("{upstream}...HEAD");
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
        let output = cli::run_git(repo_path, &["push", remote, "HEAD"], &self.log)?;

        if output.exit_code != 0 {
            return Err(classify_git_error("push", &output.stderr));
        }

        Ok(())
    }

    fn publish_branch(&self, repo_path: &Path, remote: &str) -> Result<(), AppError> {
        let output = cli::run_git(
            repo_path,
            &["push", "--set-upstream", remote, "HEAD"],
            &self.log,
        )?;

        if output.exit_code != 0 {
            return Err(classify_git_error("publish branch", &output.stderr));
        }

        Ok(())
    }

    fn pull(&self, repo_path: &Path, remote: &str) -> Result<(), AppError> {
        let output = cli::run_git(repo_path, &["pull", remote], &self.log)?;

        if output.exit_code != 0 {
            return Err(classify_git_error("pull", &output.stderr));
        }

        Ok(())
    }

    fn fetch(&self, repo_path: &Path, remote: &str) -> Result<(), AppError> {
        let output = cli::run_git(repo_path, &["fetch", "--prune", remote], &self.log)?;

        if output.exit_code != 0 {
            return Err(classify_git_error("fetch", &output.stderr));
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
        // Step 1: Collect the actual branch names to include.
        let mut branch_names = self.collect_branch_names(repo_path, branches)?;

        // Step 2: Include the remote's default branch so we can visualize fork points.
        self.add_remote_default_branch(repo_path, remote, &mut branch_names)?;

        // Step 3: Build the branch arguments for git log.
        let mut args: Vec<&str> = vec!["log", "--topo-order"];

        // Limit the number of commits if requested.
        let max_count_str;
        if let Some(max) = max_commits {
            max_count_str = format!("--max-count={}", max);
            args.push(&max_count_str);
        }

        // Add each branch as a ref to traverse.
        let branch_refs: Vec<String> = branch_names.clone();
        for b in &branch_refs {
            args.push(b);
        }

        // NUL-delimited format with SOH record separator: hash, short_hash, parents, summary, author, date, decorations
        let format = "--format=%x01%H%x00%h%x00%P%x00%s%x00%an%x00%aI%x00%D";
        args.push(format);
        args.push("--shortstat");

        let output = cli::run_git_background(repo_path, &args, &self.log)?;
        check_git_success(&output, "get branch graph")?;

        // Step 4: Parse commits from git log output.
        let commits = Self::parse_graph_commits(&output.stdout);

        // Step 5: Determine local-only commits using range specs and remote queries.
        let local_only = self.compute_local_only_commits(repo_path, remote, &branch_names)?;

        // Step 6: Return the branch graph data.
        Ok(BranchGraphData {
            commits,
            branches: branch_names,
            local_only_commits: local_only.into_iter().collect(),
        })
    }

    fn init_repository(&self, path: &Path) -> Result<RepoInfo, AppError> {
        if !path.exists() {
            std::fs::create_dir(path)
                .map_err(|e| AppError::Io(format!("Failed to create directory: {e}")))?;
        }
        let output = cli::run_git(path, &["init"], &self.log)?;
        if output.exit_code != 0 {
            return Err(AppError::Git(output.stderr.trim().to_string()));
        }
        self.open_repository(path)
    }

    fn clone_repository(&self, url: &str, parent_folder: &Path) -> Result<RepoInfo, AppError> {
        let output = cli::run_git(parent_folder, &["clone", url], &self.log)?;
        if output.exit_code != 0 {
            return Err(classify_git_error("clone repository", &output.stderr));
        }
        // Determine the cloned directory name from the URL.
        let repo_name = url
            .trim_end_matches('/')
            .rsplit('/')
            .next()
            .unwrap_or("repo")
            .trim_end_matches(".git");
        let repo_path = parent_folder.join(repo_name);
        self.open_repository(&repo_path)
    }

    fn diff_stats(&self, repo_path: &Path, area: DiffArea) -> Result<Vec<FileStats>, AppError> {
        let mut args = vec!["diff", "--numstat"];
        if matches!(area, DiffArea::Staged) {
            args.push("--cached");
        }

        let output = cli::run_git_background(repo_path, &args, &self.log)?;

        check_git_success(&output, "get diff stats")?;

        let mut stats = parse_numstat(&output.stdout);

        // For unstaged area, `git diff --numstat` doesn't include untracked files.
        // Count their lines manually and add them as additions.
        if matches!(area, DiffArea::Unstaged) {
            let status = self.status(repo_path)?;
            for entry in &status.unstaged {
                if entry.status == FileStatus::Untracked {
                    let file_path = repo_path.join(&entry.path);
                    if let Ok(content) = std::fs::read_to_string(&file_path) {
                        let line_count = content.lines().count() as u32;
                        if line_count > 0 {
                            stats.push(FileStats {
                                path: entry.path.clone(),
                                additions: line_count,
                                deletions: 0,
                            });
                        }
                    }
                    // Binary/unreadable files silently get 0 lines, which is fine.
                }
            }
        }

        Ok(stats)
    }

    fn commit_file_stats(
        &self,
        repo_path: &Path,
        hash: &str,
    ) -> Result<Vec<FileStats>, AppError> {
        let output = cli::run_git(
            repo_path,
            &["diff-tree", "--no-commit-id", "--numstat", "-r", "--root", hash],
            &self.log,
        )?;

        check_git_success(&output, "get commit file stats")?;

        Ok(parse_numstat(&output.stdout))
    }

    fn discard_unstaged_files(&self, repo_path: &Path, paths: &[&str]) -> Result<(), AppError> {
        // Separate tracked (modified/deleted) from untracked (new) files.
        let status = self.status(repo_path)?;
        let untracked: HashSet<&str> = status
            .unstaged
            .iter()
            .filter(|e| e.status == FileStatus::Untracked || e.status == FileStatus::Added)
            .map(|e| e.path.as_str())
            .collect();

        let tracked_paths: Vec<&str> = paths.iter().copied().filter(|p| !untracked.contains(p)).collect();
        let untracked_paths: Vec<&str> = paths.iter().copied().filter(|p| untracked.contains(p)).collect();

        // Restore tracked files to their index state.
        if !tracked_paths.is_empty() {
            let mut args = vec!["checkout", "--"];
            args.extend(tracked_paths.iter());
            let output = cli::run_git(repo_path, &args, &self.log)?;
            check_git_success(&output, "discard changes")?;
        }

        // Remove untracked files.
        if !untracked_paths.is_empty() {
            let mut args = vec!["clean", "-f", "--"];
            args.extend(untracked_paths.iter());
            let output = cli::run_git(repo_path, &args, &self.log)?;
            check_git_success(&output, "remove untracked files")?;
        }

        Ok(())
    }

    fn discard_staged_files(&self, repo_path: &Path, paths: &[&str]) -> Result<(), AppError> {
        // First unstage the files.
        self.unstage_files(repo_path, paths)?;
        // Then discard the working tree changes.
        self.discard_unstaged_files(repo_path, paths)
    }

    fn discard_lines(
        &self,
        repo_path: &Path,
        file_path: &str,
        diff: &FileDiff,
        selections: &[LineSelection],
        area: DiffArea,
    ) -> Result<(), AppError> {
        match area {
            DiffArea::Unstaged => {
                // Build a forward patch of the selected lines, then apply it in reverse
                // to the working tree (not --cached).
                let patch = build_partial_patch(file_path, diff, selections, false);
                if patch.is_empty() {
                    return Ok(());
                }
                apply_patch_to_worktree(repo_path, &patch, true, &self.log)
            }
            DiffArea::Staged => {
                // For staged changes, we reverse-apply from the index.
                // Build the patch with reverse=true context logic (like unstage_lines),
                // then apply it to the index (--cached --reverse).
                let patch = build_partial_patch(file_path, diff, selections, true);
                if patch.is_empty() {
                    return Ok(());
                }
                // Apply reverse to index to unstage those lines
                apply_patch(repo_path, &patch, true, &self.log)?;
                // Also apply reverse to working tree to discard the actual content
                // Re-build patch with forward logic for worktree application
                let worktree_patch = build_partial_patch(file_path, diff, selections, false);
                if worktree_patch.is_empty() {
                    return Ok(());
                }
                apply_patch_to_worktree(repo_path, &worktree_patch, true, &self.log)
            }
        }
    }

    fn merge_branch(&self, repo_path: &Path, branch_name: &str) -> Result<MergeResult, AppError> {
        let output = cli::run_git(repo_path, &["merge", branch_name], &self.log)?;

        if output.exit_code == 0 {
            return Ok(MergeResult::Success);
        }

        // Check if the failure is due to conflicts
        let stderr = output.stderr.trim();
        let stdout = output.stdout.trim();
        if stdout.contains("CONFLICT") || stderr.contains("CONFLICT") || stderr.contains("Automatic merge failed") || stdout.contains("Automatic merge failed") {
            // Gather the list of conflicted files
            let conflicts = self.get_conflicted_files(repo_path)?;
            return Ok(MergeResult::Conflict(MergeConflictInfo {
                incoming_branch: branch_name.to_string(),
                conflicted_files: conflicts,
            }));
        }

        Err(AppError::Git(format!(
            "Merge failed: {}",
            if stderr.is_empty() { stdout } else { stderr }
        )))
    }

    fn merge_abort(&self, repo_path: &Path) -> Result<(), AppError> {
        let output = cli::run_git(repo_path, &["merge", "--abort"], &self.log)?;
        if output.exit_code != 0 {
            return Err(AppError::Git(format!(
                "Failed to abort merge: {}",
                output.stderr.trim()
            )));
        }
        Ok(())
    }

    fn merge_conflicts(&self, repo_path: &Path) -> Result<MergeConflictInfo, AppError> {
        // Check if we're in a merge or revert state
        let merge_head = repo_path.join(".git").join("MERGE_HEAD");
        let revert_head = repo_path.join(".git").join("REVERT_HEAD");
        if !merge_head.exists() && !revert_head.exists() {
            return Ok(MergeConflictInfo {
                incoming_branch: String::new(),
                conflicted_files: Vec::new(),
            });
        }

        // Get the incoming branch name from MERGE_MSG (works for both merge and revert)
        let incoming_branch = self.get_merge_incoming_branch(repo_path);

        let conflicts = self.get_conflicted_files(repo_path)?;
        Ok(MergeConflictInfo {
            incoming_branch,
            conflicted_files: conflicts,
        })
    }

    fn conflict_counts(&self, repo_path: &Path) -> Result<Vec<FileConflictInfo>, AppError> {
        let files = self.get_conflicted_files(repo_path)?;
        let mut results = Vec::with_capacity(files.len());
        for file in files {
            let full_path = repo_path.join(&file);
            let count = match std::fs::read_to_string(&full_path) {
                Ok(content) => content.lines().filter(|l| l.starts_with("<<<<<<< ")).count() as u32,
                Err(_) => 0,
            };
            results.push(FileConflictInfo { path: file, conflict_count: count });
        }
        Ok(results)
    }

    fn resolve_conflict(
        &self,
        repo_path: &Path,
        file_path: &str,
        resolution: ConflictResolution,
    ) -> Result<(), AppError> {
        match resolution {
            ConflictResolution::AcceptCurrent => {
                let output = cli::run_git(
                    repo_path,
                    &["checkout", "--ours", "--", file_path],
                    &self.log,
                )?;
                if output.exit_code != 0 {
                    return Err(AppError::Git(format!(
                        "Failed to accept current changes: {}",
                        output.stderr.trim()
                    )));
                }
            }
            ConflictResolution::AcceptIncoming => {
                let output = cli::run_git(
                    repo_path,
                    &["checkout", "--theirs", "--", file_path],
                    &self.log,
                )?;
                if output.exit_code != 0 {
                    return Err(AppError::Git(format!(
                        "Failed to accept incoming changes: {}",
                        output.stderr.trim()
                    )));
                }
            }
            ConflictResolution::AcceptBoth => {
                // Read the file and remove conflict markers, keeping both sides
                let full_path = repo_path.join(file_path);
                let content = std::fs::read_to_string(&full_path)
                    .map_err(|e| AppError::Io(format!("Failed to read file: {e}")))?;
                let resolved = strip_conflict_markers(&content);
                std::fs::write(&full_path, resolved)
                    .map_err(|e| AppError::Io(format!("Failed to write file: {e}")))?;
            }
        }

        // Stage the resolved file
        let output = cli::run_git(repo_path, &["add", "--", file_path], &self.log)?;
        if output.exit_code != 0 {
            return Err(AppError::Git(format!(
                "Failed to stage resolved file: {}",
                output.stderr.trim()
            )));
        }

        Ok(())
    }

    fn open_in_merge_tool(
        &self,
        repo_path: &Path,
        file_path: &str,
    ) -> Result<(), AppError> {
        // Open the file in VS Code with merge editor
        let full_path = repo_path.join(file_path);
        let status = Command::new("code")
            .args(["--wait", "--merge"])
            .arg(&full_path)
            .arg(&full_path)
            .arg(&full_path)
            .arg(&full_path)
            .current_dir(repo_path)
            .spawn()
            .map_err(|e| AppError::Io(format!("Failed to open merge tool: {e}")))?;

        // Don't wait — let the user work in the external tool
        drop(status);
        Ok(())
    }

    fn merge_continue(&self, repo_path: &Path, message: &str) -> Result<(), AppError> {
        let output = cli::run_git(
            repo_path,
            &["commit", "-m", message],
            &self.log,
        )?;
        if output.exit_code != 0 {
            return Err(AppError::Git(format!(
                "Failed to complete merge: {}",
                output.stderr.trim()
            )));
        }
        Ok(())
    }

    fn revert_commit(&self, repo_path: &Path, hash: &str) -> Result<RevertResult, AppError> {
        // Check if the commit is a merge (multiple parents). Merge commits
        // require `-m 1` to specify the mainline parent (the branch that was
        // merged into), matching GitHub Desktop's behavior.
        let parent_check = cli::run_git(
            repo_path,
            &["rev-parse", &format!("{hash}^2")],
            &self.log,
        )?;
        let is_merge = parent_check.exit_code == 0;

        let mut args = vec!["revert", "--no-edit"];
        if is_merge {
            args.push("-m");
            args.push("1");
        }
        args.push(hash);

        let output = cli::run_git(repo_path, &args, &self.log)?;

        if output.exit_code == 0 {
            return Ok(RevertResult::Success);
        }

        // Check if the failure is due to conflicts
        let stderr = output.stderr.trim();
        let stdout = output.stdout.trim();
        if stdout.contains("CONFLICT") || stderr.contains("CONFLICT")
            || stderr.contains("could not revert") || stdout.contains("could not revert")
        {
            let conflicts = self.get_conflicted_files(repo_path)?;
            return Ok(RevertResult::Conflict(MergeConflictInfo {
                incoming_branch: hash.to_string(),
                conflicted_files: conflicts,
            }));
        }

        Err(AppError::Git(format!(
            "Revert failed: {}",
            if stderr.is_empty() { stdout } else { stderr }
        )))
    }

    fn revert_abort(&self, repo_path: &Path) -> Result<(), AppError> {
        let output = cli::run_git(repo_path, &["revert", "--abort"], &self.log)?;
        if output.exit_code != 0 {
            return Err(AppError::Git(format!(
                "Failed to abort revert: {}",
                output.stderr.trim()
            )));
        }
        Ok(())
    }

    fn revert_continue(&self, repo_path: &Path) -> Result<(), AppError> {
        // Use `git commit --no-edit` to finalize (same approach as merge_continue).
        // REVERT_HEAD tells git to use the pre-populated revert message.
        let output = cli::run_git(
            repo_path,
            &["commit", "--no-edit"],
            &self.log,
        )?;
        if output.exit_code != 0 {
            // If the commit failed because there's nothing to commit (e.g.
            // all conflicts were resolved by keeping the current version),
            // skip this revert instead of erroring.
            let combined = format!("{}{}", output.stdout.trim(), output.stderr.trim());
            if combined.contains("nothing to commit") || combined.contains("nothing added to commit") {
                let skip = cli::run_git(repo_path, &["revert", "--skip"], &self.log)?;
                if skip.exit_code != 0 {
                    return Err(AppError::Git(format!(
                        "Failed to skip revert: {}",
                        skip.stderr.trim()
                    )));
                }
                return Ok(());
            }
            return Err(AppError::Git(format!(
                "Failed to continue revert: {}",
                output.stderr.trim()
            )));
        }
        Ok(())
    }

    fn cherry_pick_commits(
        &self,
        repo_path: &Path,
        hashes: &[String],
        target_branch: &str,
        create_branch: bool,
    ) -> Result<(), AppError> {
        if hashes.is_empty() {
            return Err(AppError::Git("No commits selected for cherry-pick".to_string()));
        }

        let switch_args = if create_branch {
            vec!["switch", "-c", target_branch]
        } else {
            vec!["switch", target_branch]
        };

        let switch_output = cli::run_git(repo_path, &switch_args, &self.log)?;
        check_git_success(&switch_output, "switch target branch for cherry-pick")?;

        for hash in hashes {
            let output = cli::run_git(repo_path, &["cherry-pick", "-x", hash], &self.log)?;
            if output.exit_code != 0 {
                let _ = cli::run_git(repo_path, &["cherry-pick", "--abort"], &self.log);
                let reason = if output.stderr.trim().is_empty() {
                    output.stdout.trim().to_string()
                } else {
                    output.stderr.trim().to_string()
                };
                return Err(AppError::Git(format!(
                    "Failed to cherry-pick {hash}: {reason}"
                )));
            }
        }

        Ok(())
    }

    fn stash_staged(&self, repo_path: &Path, message: &str) -> Result<(), AppError> {
        // `git stash push --staged -m <message>` stashes only the staged changes.
        let mut args = vec!["stash", "push", "--staged"];
        if !message.is_empty() {
            args.push("-m");
            args.push(message);
        }
        let output = cli::run_git(repo_path, &args, &self.log)?;
        if output.exit_code != 0 {
            return Err(AppError::Git(format!(
                "Failed to stash staged changes: {}",
                output.stderr.trim()
            )));
        }
        Ok(())
    }

    fn list_stashes(&self, repo_path: &Path) -> Result<Vec<StashEntry>, AppError> {
        let format = "%H%x00%h%x00%s%x00%an%x00%aI";
        let output = cli::run_git_background(
            repo_path,
            &["stash", "list", &format!("--format={format}")],
            &self.log,
        )?;

        check_git_success(&output, "list stashes")?;

        let mut entries = Vec::new();
        for (index, line) in output.stdout.lines().enumerate() {
            if line.is_empty() {
                continue;
            }
            let parts: Vec<&str> = line.split('\0').collect();
            if parts.len() < 5 {
                continue;
            }
            entries.push(StashEntry {
                index: index as u32,
                stash_ref: format!("stash@{{{index}}}"),
                message: parts[2].to_string(),
                timestamp: parts[4].to_string(),
                author: parts[3].to_string(),
                hash: parts[0].to_string(),
                short_hash: parts[1].to_string(),
            });
        }

        Ok(entries)
    }

    fn stash_apply(&self, repo_path: &Path, index: u32) -> Result<(), AppError> {
        let stash_ref = format!("stash@{{{index}}}");
        let output = cli::run_git(repo_path, &["stash", "apply", &stash_ref], &self.log)?;
        check_git_success(&output, "apply stash")?;
        Ok(())
    }

    fn stash_pop(&self, repo_path: &Path, index: u32) -> Result<(), AppError> {
        let stash_ref = format!("stash@{{{index}}}");
        let output = cli::run_git(repo_path, &["stash", "pop", &stash_ref], &self.log)?;
        check_git_success(&output, "pop stash")?;
        Ok(())
    }

    fn stash_drop(&self, repo_path: &Path, index: u32) -> Result<(), AppError> {
        let stash_ref = format!("stash@{{{index}}}");
        let output = cli::run_git(repo_path, &["stash", "drop", &stash_ref], &self.log)?;
        check_git_success(&output, "drop stash")?;
        Ok(())
    }

    fn list_stash_files(&self, repo_path: &Path, index: u32) -> Result<Vec<StatusEntry>, AppError> {
        let stash_ref = format!("stash@{{{index}}}");
        let output = cli::run_git(
            repo_path,
            &["stash", "show", "--name-status", &stash_ref],
            &self.log,
        )?;
        check_git_success(&output, "list stash files")?;
        let mut entries = Vec::new();
        for line in output.stdout.lines() {
            if line.is_empty() {
                continue;
            }
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

    fn diff_stash_file(
        &self,
        repo_path: &Path,
        index: u32,
        file_path: &str,
    ) -> Result<FileDiff, AppError> {
        let stash_ref = format!("stash@{{{index}}}");
        // Use `git diff` between the stash's parent and the stash itself for the specific file.
        let parent_ref = format!("{stash_ref}^");
        let output = cli::run_git(
            repo_path,
            &["diff", &parent_ref, &stash_ref, "--", file_path],
            &self.log,
        )?;
        check_git_success(&output, "get stash file diff")?;
        let hunks = parse_unified_diff(&output.stdout);
        Ok(FileDiff {
            path: file_path.to_string(),
            hunks,
        })
    }

    fn stash_file_stats(&self, repo_path: &Path, index: u32) -> Result<Vec<FileStats>, AppError> {
        let stash_ref = format!("stash@{{{index}}}");
        let output = cli::run_git(
            repo_path,
            &["stash", "show", "--numstat", &stash_ref],
            &self.log,
        )?;
        if output.exit_code != 0 {
            return Err(AppError::Git(format!(
                "Failed to get stash file stats: {}",
                output.stderr.trim()
            )));
        }
        Ok(parse_numstat(&output.stdout))
    }

    fn show_file_at_stash(
        &self,
        repo_path: &Path,
        index: u32,
        file_path: &str,
    ) -> Result<String, AppError> {
        let stash_ref = format!("stash@{{{index}}}");
        let rev_path = format!("{stash_ref}:{file_path}");
        let output = cli::run_git(repo_path, &["show", &rev_path], &self.log)?;
        if output.exit_code != 0 {
            return Err(AppError::Git(format!(
                "Failed to read file at stash revision: {}",
                output.stderr.trim()
            )));
        }
        Ok(output.stdout)
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

impl GitProvider {
    /// Collect the branch names to include in the graph.
    /// If `requested_branches` is empty, fetches all local branches.
    /// Otherwise, uses the provided branch names.
    fn collect_branch_names(
        &self,
        repo_path: &Path,
        requested_branches: &[&str],
    ) -> Result<Vec<String>, AppError> {
        if requested_branches.is_empty() {
            // Get all local branches.
            let output = cli::run_git_background(
                repo_path,
                &["for-each-ref", "--format=%(refname:short)", "refs/heads/"],
                &self.log,
            )?;
            Ok(output
                .stdout
                .lines()
                .filter(|l| !l.is_empty())
                .map(|l| l.to_string())
                .collect())
        } else {
            Ok(requested_branches.iter().map(|b| b.to_string()).collect())
        }
    }

    /// Add the remote's default branch to the branch list if it exists.
    /// Tries symbolic-ref first, then falls back to origin/main or origin/master.
    fn add_remote_default_branch(
        &self,
        repo_path: &Path,
        remote: Option<&str>,
        branch_names: &mut Vec<String>,
    ) -> Result<(), AppError> {
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
            if let Ok(o) = &head_output
                && o.exit_code == 0
            {
                let r = o.stdout.trim().to_string();
                if !r.is_empty() && !branch_names.contains(&r) {
                    branch_names.push(r);
                    added = true;
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
                    if let Ok(o) = check
                        && o.exit_code == 0 && !branch_names.contains(&ref_name)
                    {
                        branch_names.push(ref_name);
                        break;
                    }
                }
            }
        }
        Ok(())
    }

    /// Parse git log output into GraphCommit objects.
    /// Expects NUL-delimited format with SOH record separator.
    fn parse_graph_commits(output_stdout: &str) -> Vec<GraphCommit> {
        let mut commits = Vec::new();
        // Split by SOH character to get individual commit records.
        for record in output_stdout.split('\x01') {
            if record.trim().is_empty() {
                continue;
            }
            // The first non-empty line in the record is the commit data.
            // Subsequent lines may include a shortstat summary.
            let mut commit_line: Option<&str> = None;
            let mut insertions: Option<u32> = None;
            let mut deletions: Option<u32> = None;

            for line in record.lines() {
                if line.is_empty() {
                    continue;
                }
                if commit_line.is_none() && line.contains('\0') {
                    commit_line = Some(line);
                } else if line.contains("changed") {
                    // Parse shortstat: " X file(s) changed, Y insertion(s)(+), Z deletion(s)(-)"
                    let (ins, del) = parse_shortstat(line);
                    insertions = Some(ins);
                    deletions = Some(del);
                }
            }

            let Some(line) = commit_line else {
                continue;
            };
            let parts: Vec<&str> = line.split('\0').collect();
            if parts.len() < 7 {
                continue;
            }
            let parents: Vec<String> = parts[2].split_whitespace().map(|s| s.to_string()).collect();
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
                insertions,
                deletions,
            });
        }
        commits
    }

    /// Determine local-only commits using range specs and remote queries.
    /// Returns a HashSet of commit hashes that exist locally but not in the remote.
    fn compute_local_only_commits(
        &self,
        repo_path: &Path,
        remote: Option<&str>,
        branch_names: &[String],
    ) -> Result<HashSet<String>, AppError> {
        let mut local_only: HashSet<String> = HashSet::new();
        if let Some(remote_name) = remote {
            let remote_prefix = format!("{remote_name}/");

            // Query which remote tracking branches exist.
            let remote_refs_output = cli::run_git_background(
                repo_path,
                &[
                    "for-each-ref",
                    "--format=%(refname:short)",
                    &format!("refs/remotes/{remote_name}/"),
                ],
                &self.log,
            );
            let remote_branches: HashSet<String> = remote_refs_output
                .map(|o| {
                    if o.exit_code == 0 {
                        o.stdout.lines().map(|l| l.trim().to_string()).collect()
                    } else {
                        HashSet::new()
                    }
                })
                .unwrap_or_default();

            // Branches that have a remote counterpart: use range specs.
            let ranges: Vec<String> = branch_names
                .iter()
                .filter(|b| !b.starts_with(&remote_prefix))
                .filter(|b| remote_branches.contains(&format!("{remote_name}/{b}")))
                .map(|b| format!("{remote_name}/{b}..{b}"))
                .collect();

            if !ranges.is_empty() {
                let mut lo_args: Vec<&str> = vec!["log", "--format=%H"];
                for r in &ranges {
                    lo_args.push(r);
                }
                let lo_output = cli::run_git_background(repo_path, &lo_args, &self.log);
                if let Ok(lo) = lo_output
                    && lo.exit_code == 0
                {
                    for h in lo.stdout.lines() {
                        let h = h.trim();
                        if !h.is_empty() {
                            local_only.insert(h.to_string());
                        }
                    }
                }
            }

            // Branches that have NO remote counterpart (unpublished): all their
            // commits not reachable from any of the remote's branches are local-only.
            let unpublished: Vec<&String> = branch_names
                .iter()
                .filter(|b| !b.starts_with(&remote_prefix))
                .filter(|b| !remote_branches.contains(&format!("{remote_name}/{b}")))
                .collect();

            if !unpublished.is_empty() {
                let mut unp_args: Vec<&str> = vec!["log", "--format=%H"];
                for b in &unpublished {
                    unp_args.push(b);
                }
                unp_args.push("--not");
                let remotes_pattern = format!("--remotes={remote_name}");
                unp_args.push(&remotes_pattern);
                let unp_output =
                    cli::run_git_background(repo_path, &unp_args, &self.log);
                if let Ok(unp) = unp_output
                    && unp.exit_code == 0
                {
                    for h in unp.stdout.lines() {
                        let h = h.trim();
                        if !h.is_empty() {
                            local_only.insert(h.to_string());
                        }
                    }
                }
            }
        }
        Ok(local_only)
    }

    /// Get the list of files with unresolved merge conflicts.
    fn get_conflicted_files(&self, repo_path: &Path) -> Result<Vec<String>, AppError> {
        let output = cli::run_git_background(
            repo_path,
            &["diff", "--name-only", "--diff-filter=U"],
            &self.log,
        )?;
        if output.exit_code != 0 {
            return Err(AppError::Git(format!(
                "Failed to list conflicts: {}",
                output.stderr.trim()
            )));
        }
        Ok(output
            .stdout
            .lines()
            .filter(|l| !l.trim().is_empty())
            .map(|l| l.trim().to_string())
            .collect())
    }

    /// Try to determine the incoming branch name from MERGE_MSG or MERGE_HEAD.
    fn get_merge_incoming_branch(&self, repo_path: &Path) -> String {
        // Try reading MERGE_MSG first — it usually says "Merge branch '<name>'"
        let merge_msg_path = repo_path.join(".git").join("MERGE_MSG");
        if let Ok(msg) = std::fs::read_to_string(&merge_msg_path) {
            if let Some(branch) = msg
                .lines()
                .next()
                .and_then(|l| l.strip_prefix("Merge branch '"))
                .and_then(|l| l.strip_suffix('\''))
            {
                return branch.to_string();
            }
            // Handle "Merge branch 'name' into ..."
            if let Some(line) = msg.lines().next()
                && let Some(start) = line.strip_prefix("Merge branch '")
                && let Some(end) = start.find('\'')
            {
                return start[..end].to_string();
            }
        }
        String::from("unknown")
    }
}

/// Remove conflict markers from file content, keeping both sides.
fn strip_conflict_markers(content: &str) -> String {
    let mut result = String::with_capacity(content.len());
    let mut in_conflict = false;
    let mut _in_theirs = false;

    for line in content.lines() {
        if line.starts_with("<<<<<<< ") {
            in_conflict = true;
            _in_theirs = false;
            continue;
        }
        if line.starts_with("=======" ) && in_conflict {
            _in_theirs = true;
            continue;
        }
        if line.starts_with(">>>>>>> ") && in_conflict {
            in_conflict = false;
            _in_theirs = false;
            continue;
        }
        result.push_str(line);
        result.push('\n');
    }
    result
}

/// Merge consecutive highlight spans that are only separated by whitespace.
/// This avoids fragmented highlights like `[foo] [bar]` when the whole
/// region `foo bar` changed — producing a single `[foo bar]` instead.
fn merge_highlights_over_whitespace(highlights: &mut Vec<InlineHighlight>, text: &str) {
    if highlights.len() < 2 {
        return;
    }
    let mut merged: Vec<InlineHighlight> = vec![highlights[0].clone()];
    for h in &highlights[1..] {
        let prev = merged.last_mut().expect("merged is non-empty");
        let prev_end = prev.start + prev.length;
        let gap = &text[prev_end as usize..h.start as usize];
        if gap.chars().all(|c| c.is_whitespace()) {
            // Extend previous highlight to cover the gap and this span.
            prev.length = (h.start + h.length) - prev.start;
        } else {
            merged.push(h.clone());
        }
    }
    *highlights = merged;
}

/// Compute inline (character-level) highlights for paired deletion/addition
/// lines within each hunk. This identifies which parts of a line actually
/// changed when a line was modified rather than purely added or deleted.
fn compute_inline_highlights(hunks: &mut Vec<DiffHunk>) {
    use similar::{ChangeTag, TextDiff};

    for hunk in hunks.iter_mut() {
        let lines = &mut hunk.lines;
        let mut i = 0;

        while i < lines.len() {
            // Find a block of consecutive deletions.
            if !matches!(lines[i].kind, DiffLineKind::Deletion) {
                i += 1;
                continue;
            }

            let del_start = i;
            while i < lines.len() && matches!(lines[i].kind, DiffLineKind::Deletion) {
                i += 1;
            }
            let del_end = i; // exclusive

            // Check if deletions are immediately followed by additions.
            let add_start = i;
            while i < lines.len() && matches!(lines[i].kind, DiffLineKind::Addition) {
                i += 1;
            }
            let add_end = i; // exclusive

            let del_count = del_end - del_start;
            let add_count = add_end - add_start;

            if del_count == 0 || add_count == 0 {
                // Pure additions or pure deletions — no inline highlighting.
                continue;
            }

            // Pair lines 1:1 for min(del_count, add_count).
            let pairs = del_count.min(add_count);
            for p in 0..pairs {
                let del_idx = del_start + p;
                let add_idx = add_start + p;

                // Clone content to avoid borrow conflicts when mutating highlights.
                let old_text = lines[del_idx].content.clone();
                let new_text = lines[add_idx].content.clone();

                // Use word-level diff for cleaner, more meaningful highlights.
                let diff = TextDiff::from_words(&old_text, &new_text);

                // Skip inline highlights when lines are too dissimilar — they
                // are effectively replacements rather than modifications.
                if diff.ratio() < 0.5 {
                    continue;
                }

                let mut del_highlights = Vec::new();
                let mut add_highlights = Vec::new();
                let mut old_pos: u32 = 0;
                let mut new_pos: u32 = 0;

                for change in diff.iter_all_changes() {
                    let len = change.value().len() as u32;
                    match change.tag() {
                        ChangeTag::Equal => {
                            old_pos += len;
                            new_pos += len;
                        }
                        ChangeTag::Delete => {
                            del_highlights.push(InlineHighlight {
                                start: old_pos,
                                length: len,
                            });
                            old_pos += len;
                        }
                        ChangeTag::Insert => {
                            add_highlights.push(InlineHighlight {
                                start: new_pos,
                                length: len,
                            });
                            new_pos += len;
                        }
                    }
                }

                // Merge adjacent highlights separated only by whitespace so
                // that e.g. "foo bar" doesn't show as two disjoint spans.
                merge_highlights_over_whitespace(&mut del_highlights, &old_text);
                merge_highlights_over_whitespace(&mut add_highlights, &new_text);

                // Only apply highlights when they cover a meaningful portion —
                // suppress when highlights span most of the line (noisy).
                let old_len = old_text.len() as u32;
                let new_len = new_text.len() as u32;
                let max_highlight_ratio = 0.7;

                let del_total: u32 = del_highlights.iter().map(|h| h.length).sum();
                let add_total: u32 = add_highlights.iter().map(|h| h.length).sum();

                if !del_highlights.is_empty()
                    && del_total < old_len
                    && (old_len == 0
                        || (del_total as f64 / old_len as f64) < max_highlight_ratio)
                {
                    lines[del_idx].highlights = del_highlights;
                }
                if !add_highlights.is_empty()
                    && add_total < new_len
                    && (new_len == 0
                        || (add_total as f64 / new_len as f64) < max_highlight_ratio)
                {
                    lines[add_idx].highlights = add_highlights;
                }
            }
        }
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
                highlights: Vec::new(),
            });
            new_line += 1;
        } else if let Some(content) = line.strip_prefix('-') {
            hunk.lines.push(DiffLine {
                kind: DiffLineKind::Deletion,
                content: content.to_string(),
                old_lineno: Some(old_line),
                new_lineno: None,
                highlights: Vec::new(),
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
                highlights: Vec::new(),
            });
            old_line += 1;
            new_line += 1;
        }
    }

    if let Some(hunk) = current_hunk {
        hunks.push(hunk);
    }

    compute_inline_highlights(&mut hunks);

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
            let path = line.split(" b/").last().unwrap_or("").to_string();
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
        let hunk_has_selection = hunk.lines.iter().enumerate().any(|(line_idx, line)| {
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
            hunk.old_start, old_count, hunk.new_start, new_count
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

    if let Err(e) = log.record(
        &cmd_string,
        &repo_path.display().to_string(),
        exit_code,
        &stdout,
        &stderr,
        start.elapsed().as_millis() as u32,
        false,
    ) {
        tracing::warn!(error = %e, "failed to record git command in log");
    }

    if exit_code != 0 {
        return Err(AppError::Git(format!(
            "Failed to apply patch: {}",
            stderr.trim()
        )));
    }

    Ok(())
}

/// Apply a patch to the working tree (not the index) via `git apply`.
/// If `reverse` is true, applies with `--reverse` (for discarding changes).
fn apply_patch_to_worktree(
    repo_path: &Path,
    patch: &str,
    reverse: bool,
    log: &CommandLog,
) -> Result<(), AppError> {
    use std::io::Write;

    let mut args = vec!["apply", "--unidiff-zero", "--allow-empty"];
    if reverse {
        args.push("--reverse");
    }
    args.push("-");

    let cmd_string = format!("git {}", args.join(" "));
    tracing::debug!(cmd = %cmd_string, cwd = %repo_path.display(), "applying patch to worktree");

    let start = std::time::Instant::now();

    let mut child = Command::new("git")
        .args(&args[..args.len() - 1])
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
    drop(child.stdin.take());

    let output = child
        .wait_with_output()
        .map_err(|e| AppError::Io(format!("failed to wait on git apply: {e}")))?;

    let exit_code = output.status.code().unwrap_or(-1);
    let stdout = String::from_utf8_lossy(&output.stdout).into_owned();
    let stderr = String::from_utf8_lossy(&output.stderr).into_owned();

    if let Err(e) = log.record(
        &cmd_string,
        &repo_path.display().to_string(),
        exit_code,
        &stdout,
        &stderr,
        start.elapsed().as_millis() as u32,
        false,
    ) {
        tracing::warn!(error = %e, "failed to record git command in log");
    }

    if exit_code != 0 {
        return Err(AppError::Git(format!(
            "Failed to apply patch: {}",
            stderr.trim()
        )));
    }

    Ok(())
}

/// Parse `--numstat` output into a list of per-file stats.
/// Format: "<additions>\t<deletions>\t<path>" per line.
/// Binary files show "-\t-\t<path>" — we report them as 0/0.
fn parse_numstat(raw: &str) -> Vec<FileStats> {
    let mut stats = Vec::new();
    for line in raw.lines() {
        if line.is_empty() {
            continue;
        }
        let parts: Vec<&str> = line.splitn(3, '\t').collect();
        if parts.len() < 3 {
            continue;
        }
        let additions = parts[0].parse::<u32>().unwrap_or(0);
        let deletions = parts[1].parse::<u32>().unwrap_or(0);
        stats.push(FileStats {
            path: parts[2].to_string(),
            additions,
            deletions,
        });
    }
    stats
}

/// Parse a `--shortstat` line into (insertions, deletions).
/// Format: " X file(s) changed, Y insertion(s)(+), Z deletion(s)(-)"
fn parse_shortstat(line: &str) -> (u32, u32) {
    let mut insertions = 0u32;
    let mut deletions = 0u32;

    let parts: Vec<&str> = line.split(", ").collect();
    for part in &parts[1..] {
        let trimmed = part.trim();
        if trimmed.contains("insertion") {
            insertions = trimmed
                .split_whitespace()
                .next()
                .and_then(|n| n.parse().ok())
                .unwrap_or(0);
        } else if trimmed.contains("deletion") {
            deletions = trimmed
                .split_whitespace()
                .next()
                .and_then(|n| n.parse().ok())
                .unwrap_or(0);
        }
    }

    (insertions, deletions)
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn inline_highlights_paired_modification() {
        let raw = "\
@@ -1,1 +1,1 @@
-const result = calculateSum(a, b);
+const result = calculateDiff(a, b);
";
        let hunks = parse_unified_diff(raw);
        assert_eq!(hunks.len(), 1);
        let lines = &hunks[0].lines;
        assert_eq!(lines.len(), 2);

        // Deletion: "calculateSum" should be highlighted
        let del = &lines[0];
        assert_eq!(del.kind, DiffLineKind::Deletion);
        assert!(!del.highlights.is_empty(), "deletion should have inline highlights");
        let del_hl_total: u32 = del.highlights.iter().map(|h| h.length).sum();
        assert!(del_hl_total < del.content.len() as u32, "highlights should be partial");

        // Addition: "calculateDiff" should be highlighted
        let add = &lines[1];
        assert_eq!(add.kind, DiffLineKind::Addition);
        assert!(!add.highlights.is_empty(), "addition should have inline highlights");
        let add_hl_total: u32 = add.highlights.iter().map(|h| h.length).sum();
        assert!(add_hl_total < add.content.len() as u32, "highlights should be partial");
    }

    #[test]
    fn inline_highlights_pure_addition_no_highlights() {
        let raw = "\
@@ -1,1 +1,2 @@
 context
+added line
";
        let hunks = parse_unified_diff(raw);
        let lines = &hunks[0].lines;
        // Pure addition — should not have inline highlights
        let add = &lines[1];
        assert_eq!(add.kind, DiffLineKind::Addition);
        assert!(add.highlights.is_empty(), "pure addition should not have inline highlights");
    }

    #[test]
    fn inline_highlights_pure_deletion_no_highlights() {
        let raw = "\
@@ -1,2 +1,1 @@
 context
-deleted line
";
        let hunks = parse_unified_diff(raw);
        let lines = &hunks[0].lines;
        let del = &lines[1];
        assert_eq!(del.kind, DiffLineKind::Deletion);
        assert!(del.highlights.is_empty(), "pure deletion should not have inline highlights");
    }

    #[test]
    fn inline_highlights_entirely_different_lines_no_highlights() {
        // When lines are completely different, highlights would cover the whole line
        // and thus add no value — should be suppressed.
        let raw = "\
@@ -1,1 +1,1 @@
-abcdef
+uvwxyz
";
        let hunks = parse_unified_diff(raw);
        let lines = &hunks[0].lines;
        assert!(lines[0].highlights.is_empty(), "completely different deletion should not get highlights");
        assert!(lines[1].highlights.is_empty(), "completely different addition should not get highlights");
    }

    #[test]
    fn inline_highlights_partial_change_correct_spans() {
        let raw = "\
@@ -1,1 +1,1 @@
-return a + b;
+return a - b;
";
        let hunks = parse_unified_diff(raw);
        let del = &hunks[0].lines[0];
        let add = &hunks[0].lines[1];

        // The only change is "+" → "-"
        assert_eq!(del.highlights.len(), 1);
        assert_eq!(del.highlights[0].start, 9); // position of "+"
        assert_eq!(del.highlights[0].length, 1);

        assert_eq!(add.highlights.len(), 1);
        assert_eq!(add.highlights[0].start, 9); // position of "-"
        assert_eq!(add.highlights[0].length, 1);
    }

    #[test]
    fn inline_highlights_unequal_block_pairs_first_n() {
        // 2 deletions, 3 additions: first 2 should be paired
        let raw = "\
@@ -1,2 +1,3 @@
-old line 1
-old line 2
+new line 1
+new line 2
+completely new line
";
        let hunks = parse_unified_diff(raw);
        let lines = &hunks[0].lines;

        // First pair: "old line 1" → "new line 1"
        assert!(!lines[0].highlights.is_empty(), "first deletion should have highlights");
        assert!(!lines[2].highlights.is_empty(), "first addition should have highlights");

        // Second pair: "old line 2" → "new line 2"
        assert!(!lines[1].highlights.is_empty(), "second deletion should have highlights");
        assert!(!lines[3].highlights.is_empty(), "second addition should have highlights");

        // Unpaired addition
        assert!(lines[4].highlights.is_empty(), "unpaired addition should not have highlights");
    }

    #[test]
    fn inline_highlights_context_lines_no_highlights() {
        let raw = "\
@@ -1,3 +1,3 @@
 context before
-old
+new
 context after
";
        let hunks = parse_unified_diff(raw);
        let lines = &hunks[0].lines;

        assert!(lines[0].highlights.is_empty(), "context line before should not have highlights");
        assert!(lines[3].highlights.is_empty(), "context line after should not have highlights");
    }

    #[test]
    fn inline_highlights_low_similarity_skipped() {
        // Lines that are structurally very different should not get noisy highlights.
        let raw = "\
@@ -1,1 +1,1 @@
-    <span className={`flex-shrink-0 font-mono text-xs`}>
+    <FileStatusBadge status={entry.status} variant=\"inline\" />
";
        let hunks = parse_unified_diff(raw);
        let lines = &hunks[0].lines;
        assert!(
            lines[0].highlights.is_empty(),
            "low-similarity deletion should not get highlights"
        );
        assert!(
            lines[1].highlights.is_empty(),
            "low-similarity addition should not get highlights"
        );
    }

    #[test]
    fn inline_highlights_merge_over_whitespace() {
        // Adjacent changed words separated by spaces should produce one
        // continuous highlight, not fragmented spans.
        let raw = "\
@@ -1,1 +1,1 @@
-// entire line is highlighted it adds no information.
+// entire line is highlighted it adds no useful information.
";
        let hunks = parse_unified_diff(raw);
        let add = &hunks[0].lines[1];
        assert_eq!(add.kind, DiffLineKind::Addition);
        // "no useful information." changed from "no information."
        // The word-level diff sees "no" as equal, then "information." vs
        // "useful information." — but however it splits, the adjacent
        // changed tokens should be merged into one highlight.
        for i in 1..add.highlights.len() {
            let prev_end = add.highlights[i - 1].start + add.highlights[i - 1].length;
            let gap = &add.content[prev_end as usize..add.highlights[i].start as usize];
            assert!(
                !gap.chars().all(|c| c.is_whitespace()),
                "highlights separated only by whitespace should be merged, but found gap {:?} between spans",
                gap
            );
        }
    }

    #[test]
    fn merge_highlights_over_whitespace_basic() {
        let text = "hello cruel world";
        let mut highlights = vec![
            InlineHighlight { start: 0, length: 5 },   // "hello"
            InlineHighlight { start: 6, length: 5 },   // "cruel"
        ];
        merge_highlights_over_whitespace(&mut highlights, text);
        assert_eq!(highlights.len(), 1);
        assert_eq!(highlights[0].start, 0);
        assert_eq!(highlights[0].length, 11); // "hello cruel"
    }

    #[test]
    fn merge_highlights_preserves_non_whitespace_gaps() {
        let text = "aXXb";
        let mut highlights = vec![
            InlineHighlight { start: 0, length: 1 }, // "a"
            InlineHighlight { start: 3, length: 1 }, // "b"
        ];
        merge_highlights_over_whitespace(&mut highlights, text);
        assert_eq!(highlights.len(), 2, "non-whitespace gap should not be merged");
    }
}
