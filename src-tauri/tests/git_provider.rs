use std::process::Command;

use cheesegit_lib::command_log::CommandLog;
use cheesegit_lib::vcs::git::GitProvider;
use cheesegit_lib::vcs::traits::VcsProvider;
use cheesegit_lib::vcs::types::DiffArea;
use tempfile::TempDir;

/// Create a temporary directory and initialize a git repo inside it.
fn make_temp_repo() -> TempDir {
    let dir = TempDir::new().expect("failed to create temp dir");
    let status = Command::new("git")
        .args(["init"])
        .current_dir(dir.path())
        .output()
        .expect("failed to run git init");
    assert!(status.status.success(), "git init failed");
    dir
}

#[test]
fn open_valid_repository() {
    let dir = make_temp_repo();
    let log = CommandLog::new(10);
    let provider = GitProvider::new(log.clone());

    let info = provider
        .open_repository(dir.path())
        .expect("should succeed");

    assert_eq!(info.name, dir.path().file_name().unwrap().to_str().unwrap());
    assert!(!info.path.is_empty());

    // Should have logged the git command
    let entries = log.entries();
    assert_eq!(entries.len(), 1);
    assert!(entries[0].command.contains("rev-parse"));
    assert_eq!(entries[0].exit_code, 0);
}

#[test]
fn open_nonexistent_directory() {
    let log = CommandLog::new(10);
    let provider = GitProvider::new(log);

    let path = std::path::Path::new("/tmp/cheesegit-nonexistent-path-abc123");
    let result = provider.open_repository(path);

    // run_git will fail because the directory doesn't exist
    assert!(result.is_err());
}

#[test]
fn open_non_repo_directory() {
    let dir = TempDir::new().expect("failed to create temp dir");
    let log = CommandLog::new(10);
    let provider = GitProvider::new(log.clone());

    let result = provider.open_repository(dir.path());
    assert!(result.is_err());

    let err_msg = format!("{}", result.unwrap_err());
    assert!(err_msg.contains("Not a git repository"));

    // The failed command should still be logged
    let entries = log.entries();
    assert_eq!(entries.len(), 1);
    assert_ne!(entries[0].exit_code, 0);
}

#[test]
fn open_subdirectory_resolves_to_root() {
    let dir = make_temp_repo();
    let subdir = dir.path().join("src");
    std::fs::create_dir(&subdir).expect("failed to create subdir");

    let log = CommandLog::new(10);
    let provider = GitProvider::new(log);

    let info = provider.open_repository(&subdir).expect("should succeed");

    // Should resolve to the repo root, not the subdirectory
    assert_eq!(info.name, dir.path().file_name().unwrap().to_str().unwrap());
}

#[test]
fn commands_are_logged_with_cwd() {
    let dir = make_temp_repo();
    let log = CommandLog::new(10);
    let provider = GitProvider::new(log.clone());

    provider
        .open_repository(dir.path())
        .expect("should succeed");

    let entries = log.entries();
    assert_eq!(entries[0].cwd, dir.path().display().to_string());
}

/// Helper: create a temp repo with git user config and an initial commit.
fn make_temp_repo_with_commit() -> TempDir {
    let dir = make_temp_repo();
    let path = dir.path();

    // Set local user config so commit works without global config.
    Command::new("git")
        .args(["config", "user.email", "test@test.com"])
        .current_dir(path)
        .output()
        .unwrap();
    Command::new("git")
        .args(["config", "user.name", "Test User"])
        .current_dir(path)
        .output()
        .unwrap();

    // Create a file and commit it.
    std::fs::write(path.join("hello.txt"), "hello").unwrap();
    Command::new("git")
        .args(["add", "."])
        .current_dir(path)
        .output()
        .unwrap();
    Command::new("git")
        .args(["commit", "-m", "initial commit"])
        .current_dir(path)
        .output()
        .unwrap();
    dir
}

#[test]
fn current_branch_returns_default_branch() {
    let dir = make_temp_repo_with_commit();
    let log = CommandLog::new(10);
    let provider = GitProvider::new(log);

    let branch = provider.current_branch(dir.path()).expect("should succeed");

    // Default branch is usually "main" or "master" depending on git config.
    assert!(
        branch == "main" || branch == "master",
        "expected main or master, got: {branch}"
    );
}

#[test]
fn current_branch_fails_for_non_repo() {
    let dir = TempDir::new().unwrap();
    let log = CommandLog::new(10);
    let provider = GitProvider::new(log);

    let result = provider.current_branch(dir.path());
    assert!(result.is_err());
}

#[test]
fn commit_log_returns_commits() {
    let dir = make_temp_repo_with_commit();
    let log = CommandLog::new(50);
    let provider = GitProvider::new(log);

    let commits = provider.commit_log(dir.path(), 10).expect("should succeed");

    assert_eq!(commits.len(), 1);
    assert_eq!(commits[0].summary, "initial commit");
    assert_eq!(commits[0].author, "Test User");
    assert!(!commits[0].hash.is_empty());
    assert!(!commits[0].short_hash.is_empty());
    assert!(!commits[0].timestamp.is_empty());
}

#[test]
fn commit_log_respects_limit() {
    let dir = make_temp_repo_with_commit();
    let path = dir.path();

    // Add a second commit.
    std::fs::write(path.join("world.txt"), "world").unwrap();
    Command::new("git")
        .args(["add", "."])
        .current_dir(path)
        .output()
        .unwrap();
    Command::new("git")
        .args(["commit", "-m", "second commit"])
        .current_dir(path)
        .output()
        .unwrap();

    let log = CommandLog::new(50);
    let provider = GitProvider::new(log);

    let commits = provider.commit_log(path, 1).expect("should succeed");
    assert_eq!(commits.len(), 1);
    assert_eq!(commits[0].summary, "second commit");
}

#[test]
fn commit_log_empty_repo_returns_error() {
    let dir = make_temp_repo();
    let log = CommandLog::new(10);
    let provider = GitProvider::new(log);

    // No commits yet, git log should fail.
    let result = provider.commit_log(dir.path(), 10);
    assert!(result.is_err());
}

#[test]
fn list_branches_returns_current_branch() {
    let dir = make_temp_repo_with_commit();
    let log = CommandLog::new(50);
    let provider = GitProvider::new(log);

    let branches = provider.list_branches(dir.path()).expect("should succeed");

    assert!(!branches.is_empty());
    let current = branches.iter().find(|b| b.is_current);
    assert!(current.is_some(), "should have a current branch");
    let current = current.unwrap();
    assert!(
        current.name == "main" || current.name == "master",
        "expected main or master, got: {}",
        current.name
    );
    assert!(!current.last_commit_date.is_empty());
}

#[test]
fn switch_branch_changes_current_branch() {
    let dir = make_temp_repo_with_commit();
    let log = CommandLog::new(50);
    let provider = GitProvider::new(log);

    // Create a new branch
    provider
        .create_branch(dir.path(), "feature-test")
        .expect("should create branch");

    // Verify we're on the new branch
    let branch = provider
        .current_branch(dir.path())
        .expect("should get branch");
    assert_eq!(branch, "feature-test");

    // Switch back to the original branch
    let original = provider
        .list_branches(dir.path())
        .unwrap()
        .into_iter()
        .find(|b| b.name != "feature-test")
        .expect("should have original branch");

    provider
        .switch_branch(dir.path(), &original.name)
        .expect("should switch");
    let branch = provider
        .current_branch(dir.path())
        .expect("should get branch");
    assert_eq!(branch, original.name);
}

#[test]
fn create_branch_creates_and_switches() {
    let dir = make_temp_repo_with_commit();
    let log = CommandLog::new(50);
    let provider = GitProvider::new(log);

    provider
        .create_branch(dir.path(), "new-feature")
        .expect("should create branch");

    let branch = provider
        .current_branch(dir.path())
        .expect("should get branch");
    assert_eq!(branch, "new-feature");

    let branches = provider
        .list_branches(dir.path())
        .expect("should list branches");
    assert!(branches.iter().any(|b| b.name == "new-feature"));
}

#[test]
fn switch_to_nonexistent_branch_fails() {
    let dir = make_temp_repo_with_commit();
    let log = CommandLog::new(50);
    let provider = GitProvider::new(log);

    let result = provider.switch_branch(dir.path(), "does-not-exist");
    assert!(result.is_err());
}

#[test]
fn list_branches_ordered_by_recent() {
    let dir = make_temp_repo_with_commit();
    let path = dir.path();
    let log = CommandLog::new(50);
    let provider = GitProvider::new(log);

    // Create two more branches, each with a commit.
    provider.create_branch(path, "branch-a").unwrap();
    std::fs::write(path.join("a.txt"), "a").unwrap();
    Command::new("git")
        .args(["add", "."])
        .current_dir(path)
        .output()
        .unwrap();
    Command::new("git")
        .args(["commit", "-m", "commit on a"])
        .current_dir(path)
        .output()
        .unwrap();

    // Branch-a now has the most recent commit, so it should come first.
    let branches = provider.list_branches(path).expect("should list branches");
    assert_eq!(branches[0].name, "branch-a");
}

#[test]
fn status_shows_unstaged_changes() {
    let dir = make_temp_repo_with_commit();
    let path = dir.path();
    let log = CommandLog::new(50);
    let provider = GitProvider::new(log);

    // Modify a tracked file without staging.
    std::fs::write(path.join("hello.txt"), "modified").unwrap();

    let status = provider.status(path).expect("should succeed");
    assert!(status.staged.is_empty());
    assert_eq!(status.unstaged.len(), 1);
    assert_eq!(status.unstaged[0].path, "hello.txt");
}

#[test]
fn status_shows_staged_changes() {
    let dir = make_temp_repo_with_commit();
    let path = dir.path();
    let log = CommandLog::new(50);
    let provider = GitProvider::new(log);

    // Add a new file and stage it.
    std::fs::write(path.join("new.txt"), "new").unwrap();
    Command::new("git")
        .args(["add", "new.txt"])
        .current_dir(path)
        .output()
        .unwrap();

    let status = provider.status(path).expect("should succeed");
    assert_eq!(status.staged.len(), 1);
    assert_eq!(status.staged[0].path, "new.txt");
}

#[test]
fn status_shows_file_in_both_staged_and_unstaged() {
    let dir = make_temp_repo_with_commit();
    let path = dir.path();
    let log = CommandLog::new(50);
    let provider = GitProvider::new(log);

    // Stage a change, then modify again without staging.
    std::fs::write(path.join("hello.txt"), "staged version").unwrap();
    Command::new("git")
        .args(["add", "hello.txt"])
        .current_dir(path)
        .output()
        .unwrap();
    std::fs::write(path.join("hello.txt"), "unstaged version").unwrap();

    let status = provider.status(path).expect("should succeed");
    assert!(status.staged.iter().any(|e| e.path == "hello.txt"));
    assert!(status.unstaged.iter().any(|e| e.path == "hello.txt"));
}

#[test]
fn status_clean_repo_returns_empty() {
    let dir = make_temp_repo_with_commit();
    let log = CommandLog::new(50);
    let provider = GitProvider::new(log);

    let status = provider.status(dir.path()).expect("should succeed");
    assert!(status.staged.is_empty());
    assert!(status.unstaged.is_empty());
}

#[test]
fn commit_creates_a_new_commit() {
    let dir = make_temp_repo_with_commit();
    let path = dir.path();
    let log = CommandLog::new(50);
    let provider = GitProvider::new(log);

    // Stage a file.
    std::fs::write(path.join("committed.txt"), "data").unwrap();
    Command::new("git")
        .args(["add", "."])
        .current_dir(path)
        .output()
        .unwrap();

    provider
        .commit(path, "test commit", "", false)
        .expect("should succeed");

    let commits = provider.commit_log(path, 10).expect("should get log");
    assert_eq!(commits[0].summary, "test commit");
}

#[test]
fn commit_with_description() {
    let dir = make_temp_repo_with_commit();
    let path = dir.path();
    let log = CommandLog::new(50);
    let provider = GitProvider::new(log);

    std::fs::write(path.join("desc.txt"), "data").unwrap();
    Command::new("git")
        .args(["add", "."])
        .current_dir(path)
        .output()
        .unwrap();

    provider
        .commit(path, "summary line", "detailed description", false)
        .expect("should succeed");

    // Verify the commit was created with the summary.
    let commits = provider.commit_log(path, 1).expect("should get log");
    assert_eq!(commits[0].summary, "summary line");
}

#[test]
fn commit_with_nothing_staged_fails() {
    let dir = make_temp_repo_with_commit();
    let log = CommandLog::new(50);
    let provider = GitProvider::new(log);

    let result = provider.commit(dir.path(), "empty commit", "", false);
    assert!(result.is_err());
}

#[test]
fn commit_allow_empty_succeeds_with_nothing_staged() {
    let dir = make_temp_repo_with_commit();
    let log = CommandLog::new(50);
    let provider = GitProvider::new(log);

    let result = provider.commit(dir.path(), "empty commit", "", true);
    assert!(result.is_ok());

    // Verify the empty commit was created.
    let commits = provider.commit_log(dir.path(), 1).expect("should get log");
    assert_eq!(commits[0].summary, "empty commit");
}

// ── Branch Deletion ──────────────────────────────────────────────

#[test]
fn delete_merged_branch() {
    let dir = make_temp_repo_with_commit();
    let path = dir.path();
    let log = CommandLog::new(50);
    let provider = GitProvider::new(log);

    // Create and switch to a new branch, then switch back.
    provider.create_branch(path, "feature").unwrap();
    provider.switch_branch(path, "master").unwrap();

    // feature has no remote tracking in a local-only repo.
    let info = provider.branch_delete_info(path, "feature").unwrap();
    assert!(!info.exists_on_remote);

    provider.delete_branch(path, "feature", false).unwrap();
    let branches = provider.list_branches(path).unwrap();
    assert!(!branches.iter().any(|b| b.name == "feature"));
}

#[test]
fn delete_unmerged_branch_without_force_fails() {
    let dir = make_temp_repo_with_commit();
    let path = dir.path();
    let log = CommandLog::new(50);
    let provider = GitProvider::new(log);

    // Create a branch with a unique commit.
    provider.create_branch(path, "unmerged-feature").unwrap();
    std::fs::write(path.join("feature.txt"), "new work").unwrap();
    provider.stage_files(path, &["feature.txt"]).unwrap();
    provider.commit(path, "feature work", "", false).unwrap();
    provider.switch_branch(path, "master").unwrap();

    let info = provider
        .branch_delete_info(path, "unmerged-feature")
        .unwrap();
    assert!(!info.exists_on_remote);

    // Normal delete should fail for unmerged branch.
    let result = provider.delete_branch(path, "unmerged-feature", false);
    assert!(result.is_err());
}

#[test]
fn delete_unmerged_branch_with_force_succeeds() {
    let dir = make_temp_repo_with_commit();
    let path = dir.path();
    let log = CommandLog::new(50);
    let provider = GitProvider::new(log);

    provider.create_branch(path, "unmerged-feature").unwrap();
    std::fs::write(path.join("feature.txt"), "new work").unwrap();
    provider.stage_files(path, &["feature.txt"]).unwrap();
    provider.commit(path, "feature work", "", false).unwrap();
    provider.switch_branch(path, "master").unwrap();

    provider
        .delete_branch(path, "unmerged-feature", true)
        .unwrap();
    let branches = provider.list_branches(path).unwrap();
    assert!(!branches.iter().any(|b| b.name == "unmerged-feature"));
}

#[test]
fn branch_delete_info_no_remote() {
    let dir = make_temp_repo_with_commit();
    let path = dir.path();
    let log = CommandLog::new(50);
    let provider = GitProvider::new(log);

    provider.create_branch(path, "local-only").unwrap();
    provider.switch_branch(path, "master").unwrap();

    let info = provider.branch_delete_info(path, "local-only").unwrap();
    assert!(!info.exists_on_remote);
    assert!(info.remote_name.is_none());
    assert!(info.remote_branch_name.is_none());
}

// ── Stage / Unstage ──────────────────────────────────────────────

#[test]
fn stage_file_moves_to_staged() {
    let dir = make_temp_repo_with_commit();
    let path = dir.path();
    let log = CommandLog::new(50);
    let provider = GitProvider::new(log);

    // Create a new file (untracked → unstaged).
    std::fs::write(path.join("new.txt"), "new").unwrap();

    let status = provider.status(path).unwrap();
    assert_eq!(status.unstaged.len(), 1);
    assert_eq!(status.staged.len(), 0);

    provider.stage_files(path, &["new.txt"]).unwrap();

    let status = provider.status(path).unwrap();
    assert_eq!(status.staged.len(), 1);
    assert_eq!(status.staged[0].path, "new.txt");
    assert_eq!(status.unstaged.len(), 0);
}

#[test]
fn unstage_file_moves_to_unstaged() {
    let dir = make_temp_repo_with_commit();
    let path = dir.path();
    let log = CommandLog::new(50);
    let provider = GitProvider::new(log);

    // Modify an existing tracked file and stage it.
    std::fs::write(path.join("hello.txt"), "changed").unwrap();
    provider.stage_files(path, &["hello.txt"]).unwrap();

    let status = provider.status(path).unwrap();
    assert_eq!(status.staged.len(), 1);

    provider.unstage_files(path, &["hello.txt"]).unwrap();

    let status = provider.status(path).unwrap();
    assert_eq!(status.staged.len(), 0);
    assert_eq!(status.unstaged.len(), 1);
    assert_eq!(status.unstaged[0].path, "hello.txt");
}

#[test]
fn stage_multiple_files() {
    let dir = make_temp_repo_with_commit();
    let path = dir.path();
    let log = CommandLog::new(50);
    let provider = GitProvider::new(log);

    std::fs::write(path.join("a.txt"), "a").unwrap();
    std::fs::write(path.join("b.txt"), "b").unwrap();

    provider.stage_files(path, &["a.txt", "b.txt"]).unwrap();

    let status = provider.status(path).unwrap();
    assert_eq!(status.staged.len(), 2);
    assert_eq!(status.unstaged.len(), 0);
}

#[test]
fn unstage_multiple_files() {
    let dir = make_temp_repo_with_commit();
    let path = dir.path();
    let log = CommandLog::new(50);
    let provider = GitProvider::new(log);

    std::fs::write(path.join("a.txt"), "a").unwrap();
    std::fs::write(path.join("b.txt"), "b").unwrap();
    provider.stage_files(path, &["a.txt", "b.txt"]).unwrap();

    provider.unstage_files(path, &["a.txt", "b.txt"]).unwrap();

    let status = provider.status(path).unwrap();
    assert_eq!(status.staged.len(), 0);
    assert_eq!(status.unstaged.len(), 2);
}

#[test]
fn status_lists_individual_files_in_untracked_directory() {
    let dir = make_temp_repo_with_commit();
    let path = dir.path();
    let log = CommandLog::new(50);
    let provider = GitProvider::new(log);

    // Create a new directory with multiple untracked files inside it.
    let sub = path.join("newdir");
    std::fs::create_dir(&sub).unwrap();
    std::fs::write(sub.join("one.txt"), "1").unwrap();
    std::fs::write(sub.join("two.txt"), "2").unwrap();

    let status = provider.status(path).unwrap();

    // Should list the individual files, not just "newdir/".
    assert_eq!(
        status.unstaged.len(),
        2,
        "expected 2 individual files, got: {:?}",
        status.unstaged
    );
    let mut paths: Vec<&str> = status.unstaged.iter().map(|e| e.path.as_str()).collect();
    paths.sort();
    assert_eq!(paths, vec!["newdir/one.txt", "newdir/two.txt"]);
}

// ── Diff ──────────────────────────────────────────────────────────

#[test]
fn diff_unstaged_modification() {
    let dir = make_temp_repo_with_commit();
    let path = dir.path();
    let log = CommandLog::new(50);
    let provider = GitProvider::new(log);

    // Modify a tracked file without staging.
    std::fs::write(path.join("hello.txt"), "hello world\n").unwrap();

    let diff = provider
        .diff_file(path, "hello.txt", DiffArea::Unstaged)
        .unwrap();

    assert_eq!(diff.path, "hello.txt");
    assert!(!diff.hunks.is_empty(), "should have at least one hunk");

    let hunk = &diff.hunks[0];
    // Should have a deletion (old content) and an addition (new content).
    let has_deletion = hunk
        .lines
        .iter()
        .any(|l| matches!(l.kind, cheesegit_lib::vcs::types::DiffLineKind::Deletion));
    let has_addition = hunk
        .lines
        .iter()
        .any(|l| matches!(l.kind, cheesegit_lib::vcs::types::DiffLineKind::Addition));
    assert!(has_deletion, "should have a deletion line");
    assert!(has_addition, "should have an addition line");
}

#[test]
fn diff_staged_modification() {
    let dir = make_temp_repo_with_commit();
    let path = dir.path();
    let log = CommandLog::new(50);
    let provider = GitProvider::new(log);

    // Modify and stage.
    std::fs::write(path.join("hello.txt"), "staged change\n").unwrap();
    Command::new("git")
        .args(["add", "hello.txt"])
        .current_dir(path)
        .output()
        .unwrap();

    let diff = provider
        .diff_file(path, "hello.txt", DiffArea::Staged)
        .unwrap();
    assert_eq!(diff.path, "hello.txt");
    assert!(!diff.hunks.is_empty());
}

#[test]
fn diff_staged_vs_unstaged_are_different() {
    let dir = make_temp_repo_with_commit();
    let path = dir.path();
    let log = CommandLog::new(50);
    let provider = GitProvider::new(log);

    // Stage one version, then modify again.
    std::fs::write(path.join("hello.txt"), "staged version").unwrap();
    Command::new("git")
        .args(["add", "hello.txt"])
        .current_dir(path)
        .output()
        .unwrap();
    std::fs::write(path.join("hello.txt"), "unstaged version").unwrap();

    let staged_diff = provider
        .diff_file(path, "hello.txt", DiffArea::Staged)
        .unwrap();
    let unstaged_diff = provider
        .diff_file(path, "hello.txt", DiffArea::Unstaged)
        .unwrap();

    // Both should have hunks.
    assert!(!staged_diff.hunks.is_empty());
    assert!(!unstaged_diff.hunks.is_empty());

    // The additions should differ — staged shows "staged version", unstaged shows diff from staged to "unstaged version".
    let staged_adds: Vec<&str> = staged_diff.hunks[0]
        .lines
        .iter()
        .filter(|l| matches!(l.kind, cheesegit_lib::vcs::types::DiffLineKind::Addition))
        .map(|l| l.content.as_str())
        .collect();
    let unstaged_adds: Vec<&str> = unstaged_diff.hunks[0]
        .lines
        .iter()
        .filter(|l| matches!(l.kind, cheesegit_lib::vcs::types::DiffLineKind::Addition))
        .map(|l| l.content.as_str())
        .collect();

    assert_ne!(staged_adds, unstaged_adds);
}

#[test]
fn diff_no_changes_returns_empty_hunks() {
    let dir = make_temp_repo_with_commit();
    let log = CommandLog::new(50);
    let provider = GitProvider::new(log);

    let diff = provider
        .diff_file(dir.path(), "hello.txt", DiffArea::Unstaged)
        .unwrap();
    assert!(diff.hunks.is_empty(), "unchanged file should have no hunks");
}

#[test]
fn diff_line_numbers_are_correct() {
    let dir = make_temp_repo_with_commit();
    let path = dir.path();
    let log = CommandLog::new(50);
    let provider = GitProvider::new(log);

    // Create a multi-line file, commit, then modify.
    std::fs::write(path.join("multi.txt"), "line1\nline2\nline3\n").unwrap();
    Command::new("git")
        .args(["add", "."])
        .current_dir(path)
        .output()
        .unwrap();
    Command::new("git")
        .args(["commit", "-m", "add multi"])
        .current_dir(path)
        .output()
        .unwrap();

    std::fs::write(path.join("multi.txt"), "line1\nchanged\nline3\n").unwrap();

    let diff = provider
        .diff_file(path, "multi.txt", DiffArea::Unstaged)
        .unwrap();
    assert!(!diff.hunks.is_empty());

    // The deletion of "line2" should have old_lineno = 2.
    let del = diff.hunks[0]
        .lines
        .iter()
        .find(|l| matches!(l.kind, cheesegit_lib::vcs::types::DiffLineKind::Deletion))
        .expect("should have a deletion");
    assert_eq!(del.old_lineno, Some(2));
    assert_eq!(del.content, "line2");

    // The addition of "changed" should have new_lineno = 2.
    let add = diff.hunks[0]
        .lines
        .iter()
        .find(|l| matches!(l.kind, cheesegit_lib::vcs::types::DiffLineKind::Addition))
        .expect("should have an addition");
    assert_eq!(add.new_lineno, Some(2));
    assert_eq!(add.content, "changed");
}

// ── branch_graph tests ──────────────────────────────────────────────

/// Helper: create a temp repo with N commits on the default branch.
fn make_temp_repo_with_n_commits(n: usize) -> TempDir {
    let dir = make_temp_repo_with_commit(); // 1 initial commit
    let path = dir.path();
    for i in 1..n {
        std::fs::write(path.join("hello.txt"), format!("content {i}")).unwrap();
        Command::new("git")
            .args(["add", "."])
            .current_dir(path)
            .output()
            .unwrap();
        Command::new("git")
            .args(["commit", "-m", &format!("commit {i}")])
            .current_dir(path)
            .output()
            .unwrap();
    }
    dir
}

#[test]
fn branch_graph_returns_commits_and_branches() {
    let dir = make_temp_repo_with_commit();
    let log = CommandLog::new(50);
    let provider = GitProvider::new(log);

    let graph = provider
        .branch_graph(dir.path(), &[], None, None)
        .expect("should succeed");

    assert_eq!(graph.commits.len(), 1);
    assert_eq!(graph.commits[0].summary, "initial commit");
    assert!(!graph.branches.is_empty());
    assert!(graph.local_only_commits.is_empty());
}

#[test]
fn branch_graph_includes_all_local_branches() {
    let dir = make_temp_repo_with_commit();
    let path = dir.path();
    let log = CommandLog::new(50);
    let provider = GitProvider::new(log);

    // Create a side branch with a commit.
    provider.create_branch(path, "feature").unwrap();
    std::fs::write(path.join("feat.txt"), "feat").unwrap();
    Command::new("git")
        .args(["add", "."])
        .current_dir(path)
        .output()
        .unwrap();
    Command::new("git")
        .args(["commit", "-m", "feature commit"])
        .current_dir(path)
        .output()
        .unwrap();

    let graph = provider
        .branch_graph(path, &[], None, None)
        .expect("should succeed");

    // Should include commits from both branches.
    assert!(graph.commits.len() >= 2);
    // Both branches should be listed.
    assert!(graph.branches.contains(&"feature".to_string()));
}

#[test]
fn branch_graph_filters_to_specified_branches() {
    let dir = make_temp_repo_with_commit();
    let path = dir.path();
    let log = CommandLog::new(50);
    let provider = GitProvider::new(log);

    // Create a side branch with a commit.
    provider.create_branch(path, "feature").unwrap();
    std::fs::write(path.join("feat.txt"), "feat").unwrap();
    Command::new("git")
        .args(["add", "."])
        .current_dir(path)
        .output()
        .unwrap();
    Command::new("git")
        .args(["commit", "-m", "feature commit"])
        .current_dir(path)
        .output()
        .unwrap();

    // Request only the feature branch.
    let graph = provider
        .branch_graph(path, &["feature"], None, None)
        .expect("should succeed");

    // Should still return commits (the feature branch includes initial commit in its history).
    assert!(!graph.commits.is_empty());
    assert!(graph.branches.contains(&"feature".to_string()));
}

#[test]
fn branch_graph_max_commits_limits_output() {
    let dir = make_temp_repo_with_n_commits(10);
    let log = CommandLog::new(50);
    let provider = GitProvider::new(log);

    let graph = provider
        .branch_graph(dir.path(), &[], None, Some(3))
        .expect("should succeed");

    assert_eq!(graph.commits.len(), 3);
}

#[test]
fn branch_graph_max_commits_none_returns_all() {
    let dir = make_temp_repo_with_n_commits(10);
    let log = CommandLog::new(50);
    let provider = GitProvider::new(log);

    let graph = provider
        .branch_graph(dir.path(), &[], None, None)
        .expect("should succeed");

    assert_eq!(graph.commits.len(), 10);
}

#[test]
fn branch_graph_max_commits_larger_than_total() {
    let dir = make_temp_repo_with_n_commits(5);
    let log = CommandLog::new(50);
    let provider = GitProvider::new(log);

    let graph = provider
        .branch_graph(dir.path(), &[], None, Some(100))
        .expect("should succeed");

    // Should return all 5, not error.
    assert_eq!(graph.commits.len(), 5);
}

#[test]
fn branch_graph_commits_have_parent_hashes() {
    let dir = make_temp_repo_with_n_commits(3);
    let log = CommandLog::new(50);
    let provider = GitProvider::new(log);

    let graph = provider
        .branch_graph(dir.path(), &[], None, None)
        .expect("should succeed");

    // First commit (most recent in topo order) should have a parent.
    assert!(!graph.commits[0].parents.is_empty());
    // Last commit (initial) should have no parents.
    assert!(graph.commits.last().unwrap().parents.is_empty());
}

#[test]
fn branch_graph_commits_have_refs() {
    let dir = make_temp_repo_with_commit();
    let log = CommandLog::new(50);
    let provider = GitProvider::new(log);

    let graph = provider
        .branch_graph(dir.path(), &[], None, None)
        .expect("should succeed");

    // The single commit should be pointed to by the default branch.
    let refs_flat: Vec<&str> = graph
        .commits
        .iter()
        .flat_map(|c| c.refs.iter().map(|r| r.as_str()))
        .collect();
    assert!(
        !refs_flat.is_empty(),
        "at least one commit should have a branch ref"
    );
}

#[test]
fn list_remotes_prefers_origin_first() {
    // When multiple remotes exist, "origin" should always be first in the list
    // regardless of alphabetical order.
    let bare_dir = TempDir::new().expect("create bare dir");
    let bare_path = bare_dir.path();

    Command::new("git")
        .args(["init", "--bare"])
        .current_dir(bare_path)
        .output()
        .expect("git init --bare");

    let dir = TempDir::new().expect("create work dir");
    let path = dir.path();

    Command::new("git")
        .args(["clone", bare_path.to_str().unwrap(), path.to_str().unwrap()])
        .output()
        .expect("git clone");

    // Add a second remote that sorts alphabetically before "origin".
    Command::new("git")
        .args(["remote", "add", "abc-mirror", "https://example.com/mirror.git"])
        .current_dir(path)
        .output()
        .unwrap();

    let log = CommandLog::new(50);
    let provider = GitProvider::new(log);

    let remotes = provider.list_remotes(path).expect("should list remotes");

    assert_eq!(remotes.len(), 2);
    assert_eq!(remotes[0].name, "origin", "origin should be first regardless of alphabetical order");
    assert_eq!(remotes[1].name, "abc-mirror");
}

#[test]
fn remote_branch_status_returns_none_for_unpublished_branch() {
    // When a branch does not exist on a remote, remote_branch_status returns None.
    let bare_origin = TempDir::new().expect("create bare origin");
    let bare_mirror = TempDir::new().expect("create bare mirror");

    Command::new("git")
        .args(["init", "--bare"])
        .current_dir(bare_origin.path())
        .output()
        .expect("git init --bare origin");
    Command::new("git")
        .args(["init", "--bare"])
        .current_dir(bare_mirror.path())
        .output()
        .expect("git init --bare mirror");

    let dir = TempDir::new().expect("create work dir");
    let path = dir.path();

    Command::new("git")
        .args(["clone", bare_origin.path().to_str().unwrap(), path.to_str().unwrap()])
        .output()
        .expect("git clone");

    Command::new("git")
        .args(["config", "user.email", "test@test.com"])
        .current_dir(path)
        .output()
        .unwrap();
    Command::new("git")
        .args(["config", "user.name", "Test"])
        .current_dir(path)
        .output()
        .unwrap();

    // Initial commit pushed only to origin.
    std::fs::write(path.join("file.txt"), "hello").unwrap();
    Command::new("git")
        .args(["add", "."])
        .current_dir(path)
        .output()
        .unwrap();
    Command::new("git")
        .args(["commit", "-m", "initial"])
        .current_dir(path)
        .output()
        .unwrap();
    Command::new("git")
        .args(["push", "origin", "HEAD"])
        .current_dir(path)
        .output()
        .unwrap();

    // Add mirror remote but don't push to it.
    Command::new("git")
        .args(["remote", "add", "mirror", bare_mirror.path().to_str().unwrap()])
        .current_dir(path)
        .output()
        .unwrap();

    let log = CommandLog::new(50);
    let provider = GitProvider::new(log);

    // Branch exists on origin.
    let origin_status = provider.remote_branch_status(path, "origin")
        .expect("should succeed");
    assert!(origin_status.is_some(), "branch should exist on origin");
    assert_eq!(origin_status.unwrap().ahead, 0);

    // Branch does NOT exist on mirror → returns None (unpublished).
    let mirror_status = provider.remote_branch_status(path, "mirror")
        .expect("should succeed");
    assert!(mirror_status.is_none(), "branch should not exist on mirror (unpublished)");
}

#[test]
fn remote_branch_status_shows_ahead_count() {
    // When we have local commits not pushed to a specific remote.
    let bare_origin = TempDir::new().expect("create bare origin");

    Command::new("git")
        .args(["init", "--bare"])
        .current_dir(bare_origin.path())
        .output()
        .expect("git init --bare");

    let dir = TempDir::new().expect("create work dir");
    let path = dir.path();

    Command::new("git")
        .args(["clone", bare_origin.path().to_str().unwrap(), path.to_str().unwrap()])
        .output()
        .expect("git clone");

    Command::new("git")
        .args(["config", "user.email", "test@test.com"])
        .current_dir(path)
        .output()
        .unwrap();
    Command::new("git")
        .args(["config", "user.name", "Test"])
        .current_dir(path)
        .output()
        .unwrap();

    // Push initial commit.
    std::fs::write(path.join("file.txt"), "hello").unwrap();
    Command::new("git")
        .args(["add", "."])
        .current_dir(path)
        .output()
        .unwrap();
    Command::new("git")
        .args(["commit", "-m", "initial"])
        .current_dir(path)
        .output()
        .unwrap();
    Command::new("git")
        .args(["push", "origin", "HEAD"])
        .current_dir(path)
        .output()
        .unwrap();

    // Make a local commit without pushing.
    std::fs::write(path.join("new.txt"), "new").unwrap();
    Command::new("git")
        .args(["add", "."])
        .current_dir(path)
        .output()
        .unwrap();
    Command::new("git")
        .args(["commit", "-m", "local commit"])
        .current_dir(path)
        .output()
        .unwrap();

    let log = CommandLog::new(50);
    let provider = GitProvider::new(log);

    let status = provider.remote_branch_status(path, "origin")
        .expect("should succeed");
    assert!(status.is_some());
    let s = status.unwrap();
    assert_eq!(s.ahead, 1, "should be 1 commit ahead of origin");
    assert_eq!(s.behind, 0);
}

#[test]
fn push_to_secondary_remote_works() {
    // Set up a repo with two bare remotes, push to the secondary one.
    let bare_origin = TempDir::new().expect("create bare origin");
    let bare_mirror = TempDir::new().expect("create bare mirror");

    Command::new("git")
        .args(["init", "--bare"])
        .current_dir(bare_origin.path())
        .output()
        .expect("git init --bare origin");
    Command::new("git")
        .args(["init", "--bare"])
        .current_dir(bare_mirror.path())
        .output()
        .expect("git init --bare mirror");

    let dir = TempDir::new().expect("create work dir");
    let path = dir.path();

    Command::new("git")
        .args(["clone", bare_origin.path().to_str().unwrap(), path.to_str().unwrap()])
        .output()
        .expect("git clone");

    Command::new("git")
        .args(["config", "user.email", "test@test.com"])
        .current_dir(path)
        .output()
        .unwrap();
    Command::new("git")
        .args(["config", "user.name", "Test"])
        .current_dir(path)
        .output()
        .unwrap();

    // Initial commit and push to origin.
    std::fs::write(path.join("file.txt"), "hello").unwrap();
    Command::new("git")
        .args(["add", "."])
        .current_dir(path)
        .output()
        .unwrap();
    Command::new("git")
        .args(["commit", "-m", "initial"])
        .current_dir(path)
        .output()
        .unwrap();
    Command::new("git")
        .args(["push", "origin", "HEAD"])
        .current_dir(path)
        .output()
        .unwrap();

    // Add mirror as a secondary remote.
    Command::new("git")
        .args(["remote", "add", "mirror", bare_mirror.path().to_str().unwrap()])
        .current_dir(path)
        .output()
        .unwrap();

    let log = CommandLog::new(50);
    let provider = GitProvider::new(log);

    // Push to the secondary remote should succeed.
    let result = provider.push(path, "mirror");
    assert!(result.is_ok(), "push to secondary remote should succeed: {:?}", result.err());

    // Verify the commit actually arrived at the mirror.
    let verify = Command::new("git")
        .args(["log", "--oneline", "-1"])
        .current_dir(bare_mirror.path())
        .output()
        .unwrap();
    let log_output = String::from_utf8_lossy(&verify.stdout);
    assert!(
        log_output.contains("initial"),
        "commit should be present in mirror remote, got: {log_output}"
    );
}

#[test]
fn fetch_from_secondary_remote_works() {
    // Set up two bare repos; clone from origin, add mirror, then fetch from mirror
    // after mirror gets a new commit.
    let bare_origin = TempDir::new().expect("create bare origin");
    let bare_mirror = TempDir::new().expect("create bare mirror");

    Command::new("git")
        .args(["init", "--bare"])
        .current_dir(bare_origin.path())
        .output()
        .expect("git init --bare origin");
    Command::new("git")
        .args(["init", "--bare"])
        .current_dir(bare_mirror.path())
        .output()
        .expect("git init --bare mirror");

    let dir = TempDir::new().expect("create work dir");
    let path = dir.path();

    Command::new("git")
        .args(["clone", bare_origin.path().to_str().unwrap(), path.to_str().unwrap()])
        .output()
        .expect("git clone");

    Command::new("git")
        .args(["config", "user.email", "test@test.com"])
        .current_dir(path)
        .output()
        .unwrap();
    Command::new("git")
        .args(["config", "user.name", "Test"])
        .current_dir(path)
        .output()
        .unwrap();

    // Initial commit and push to origin.
    std::fs::write(path.join("file.txt"), "hello").unwrap();
    Command::new("git")
        .args(["add", "."])
        .current_dir(path)
        .output()
        .unwrap();
    Command::new("git")
        .args(["commit", "-m", "initial"])
        .current_dir(path)
        .output()
        .unwrap();
    Command::new("git")
        .args(["push", "origin", "HEAD"])
        .current_dir(path)
        .output()
        .unwrap();

    // Also push to mirror so it has a branch.
    Command::new("git")
        .args(["remote", "add", "mirror", bare_mirror.path().to_str().unwrap()])
        .current_dir(path)
        .output()
        .unwrap();
    Command::new("git")
        .args(["push", "mirror", "HEAD"])
        .current_dir(path)
        .output()
        .unwrap();

    // Simulate a new commit on the mirror (push from another clone).
    let other_dir = TempDir::new().expect("create other work dir");
    let other_path = other_dir.path();
    Command::new("git")
        .args(["clone", bare_mirror.path().to_str().unwrap(), other_path.to_str().unwrap()])
        .output()
        .expect("git clone mirror");
    Command::new("git")
        .args(["config", "user.email", "other@test.com"])
        .current_dir(other_path)
        .output()
        .unwrap();
    Command::new("git")
        .args(["config", "user.name", "Other"])
        .current_dir(other_path)
        .output()
        .unwrap();
    std::fs::write(other_path.join("new.txt"), "new content").unwrap();
    Command::new("git")
        .args(["add", "."])
        .current_dir(other_path)
        .output()
        .unwrap();
    Command::new("git")
        .args(["commit", "-m", "mirror-only commit"])
        .current_dir(other_path)
        .output()
        .unwrap();
    Command::new("git")
        .args(["push"])
        .current_dir(other_path)
        .output()
        .unwrap();

    let log = CommandLog::new(50);
    let provider = GitProvider::new(log);

    // Fetch from the mirror remote should succeed.
    let result = provider.fetch(path, "mirror");
    assert!(result.is_ok(), "fetch from secondary remote should succeed: {:?}", result.err());

    // Verify we now have the mirror's commit in our remote-tracking refs.
    // Use `git branch -r` to discover the mirror branch name dynamically.
    let refs_output = Command::new("git")
        .args(["branch", "-r", "--list", "mirror/*"])
        .current_dir(path)
        .output()
        .unwrap();
    let refs_str = String::from_utf8_lossy(&refs_output.stdout);
    let mirror_branch = refs_str.lines()
        .map(|l| l.trim())
        .find(|l| !l.contains("HEAD"))
        .expect("should have at least one mirror/* tracking branch");

    let verify = Command::new("git")
        .args(["log", "--oneline", mirror_branch])
        .current_dir(path)
        .output()
        .unwrap();
    let log_output = String::from_utf8_lossy(&verify.stdout);
    assert!(
        log_output.contains("mirror-only commit"),
        "mirror-only commit should appear after fetch, got: {log_output}"
    );
}

#[test]
fn branch_graph_local_only_uses_correct_remote() {
    // When multiple remotes exist, local_only_commits should detect commits
    // not pushed to the specified remote (even if pushed to another remote).
    let bare_origin = TempDir::new().expect("create bare origin");
    let bare_mirror = TempDir::new().expect("create bare mirror");

    Command::new("git")
        .args(["init", "--bare"])
        .current_dir(bare_origin.path())
        .output()
        .expect("git init --bare origin");
    Command::new("git")
        .args(["init", "--bare"])
        .current_dir(bare_mirror.path())
        .output()
        .expect("git init --bare mirror");

    let dir = TempDir::new().expect("create work dir");
    let path = dir.path();

    Command::new("git")
        .args(["clone", bare_origin.path().to_str().unwrap(), path.to_str().unwrap()])
        .output()
        .expect("git clone");

    Command::new("git")
        .args(["config", "user.email", "test@test.com"])
        .current_dir(path)
        .output()
        .unwrap();
    Command::new("git")
        .args(["config", "user.name", "Test"])
        .current_dir(path)
        .output()
        .unwrap();

    // Initial commit pushed to both remotes.
    std::fs::write(path.join("file.txt"), "hello").unwrap();
    Command::new("git")
        .args(["add", "."])
        .current_dir(path)
        .output()
        .unwrap();
    Command::new("git")
        .args(["commit", "-m", "initial"])
        .current_dir(path)
        .output()
        .unwrap();
    Command::new("git")
        .args(["push", "origin", "HEAD"])
        .current_dir(path)
        .output()
        .unwrap();

    Command::new("git")
        .args(["remote", "add", "mirror", bare_mirror.path().to_str().unwrap()])
        .current_dir(path)
        .output()
        .unwrap();
    Command::new("git")
        .args(["push", "mirror", "HEAD"])
        .current_dir(path)
        .output()
        .unwrap();

    // Now create a new commit that's ONLY pushed to mirror, not origin.
    std::fs::write(path.join("new.txt"), "new").unwrap();
    Command::new("git")
        .args(["add", "."])
        .current_dir(path)
        .output()
        .unwrap();
    Command::new("git")
        .args(["commit", "-m", "new commit"])
        .current_dir(path)
        .output()
        .unwrap();
    Command::new("git")
        .args(["push", "mirror", "HEAD"])
        .current_dir(path)
        .output()
        .unwrap();
    // Fetch to update remote-tracking refs.
    Command::new("git")
        .args(["fetch", "mirror"])
        .current_dir(path)
        .output()
        .unwrap();

    let log = CommandLog::new(50);
    let provider = GitProvider::new(log);

    // When querying the graph relative to "origin", the new commit should be local-only.
    let graph = provider
        .branch_graph(path, &[], Some("origin"), None)
        .expect("branch_graph should succeed");

    assert!(
        !graph.local_only_commits.is_empty(),
        "new commit should be marked local-only relative to origin"
    );

    // When querying relative to "mirror", the commit should NOT be local-only.
    let log2 = CommandLog::new(50);
    let provider2 = GitProvider::new(log2);
    let graph2 = provider2
        .branch_graph(path, &[], Some("mirror"), None)
        .expect("branch_graph should succeed");

    assert!(
        graph2.local_only_commits.is_empty(),
        "no commits should be local-only relative to mirror (all are pushed there)"
    );
}

#[test]
fn branch_graph_marks_all_commits_local_only_when_branch_not_on_remote() {
    // Scenario: branch is pushed to origin but does NOT exist on mirror at all.
    // All commits unique to that branch should be local-only relative to mirror.
    let bare_origin = TempDir::new().expect("create bare origin");
    let bare_mirror = TempDir::new().expect("create bare mirror");

    Command::new("git")
        .args(["init", "--bare"])
        .current_dir(bare_origin.path())
        .output()
        .expect("git init --bare origin");
    Command::new("git")
        .args(["init", "--bare"])
        .current_dir(bare_mirror.path())
        .output()
        .expect("git init --bare mirror");

    let dir = TempDir::new().expect("create work dir");
    let path = dir.path();

    Command::new("git")
        .args(["clone", bare_origin.path().to_str().unwrap(), path.to_str().unwrap()])
        .output()
        .expect("git clone");

    Command::new("git")
        .args(["config", "user.email", "test@test.com"])
        .current_dir(path)
        .output()
        .unwrap();
    Command::new("git")
        .args(["config", "user.name", "Test"])
        .current_dir(path)
        .output()
        .unwrap();

    // Initial commit pushed to both remotes (on main/master).
    std::fs::write(path.join("file.txt"), "hello").unwrap();
    Command::new("git")
        .args(["add", "."])
        .current_dir(path)
        .output()
        .unwrap();
    Command::new("git")
        .args(["commit", "-m", "initial"])
        .current_dir(path)
        .output()
        .unwrap();
    Command::new("git")
        .args(["push", "origin", "HEAD"])
        .current_dir(path)
        .output()
        .unwrap();

    Command::new("git")
        .args(["remote", "add", "mirror", bare_mirror.path().to_str().unwrap()])
        .current_dir(path)
        .output()
        .unwrap();
    Command::new("git")
        .args(["push", "mirror", "HEAD"])
        .current_dir(path)
        .output()
        .unwrap();
    // Fetch so mirror remote-tracking refs are populated.
    Command::new("git")
        .args(["fetch", "mirror"])
        .current_dir(path)
        .output()
        .unwrap();

    // Create a feature branch, push ONLY to origin (not mirror).
    Command::new("git")
        .args(["checkout", "-b", "feature-x"])
        .current_dir(path)
        .output()
        .unwrap();
    std::fs::write(path.join("feature.txt"), "feature").unwrap();
    Command::new("git")
        .args(["add", "."])
        .current_dir(path)
        .output()
        .unwrap();
    Command::new("git")
        .args(["commit", "-m", "feature commit"])
        .current_dir(path)
        .output()
        .unwrap();
    Command::new("git")
        .args(["push", "origin", "feature-x"])
        .current_dir(path)
        .output()
        .unwrap();
    Command::new("git")
        .args(["fetch", "origin"])
        .current_dir(path)
        .output()
        .unwrap();

    let log = CommandLog::new(50);
    let provider = GitProvider::new(log);

    // Relative to origin: branch is pushed, so no local-only commits.
    let graph_origin = provider
        .branch_graph(path, &[], Some("origin"), None)
        .expect("branch_graph relative to origin");
    assert!(
        graph_origin.local_only_commits.is_empty(),
        "all commits are pushed to origin, none should be local-only"
    );

    // Relative to mirror: branch doesn't exist there at all, so the feature
    // commit should be marked local-only.
    let log2 = CommandLog::new(50);
    let provider2 = GitProvider::new(log2);
    let graph_mirror = provider2
        .branch_graph(path, &[], Some("mirror"), None)
        .expect("branch_graph relative to mirror");
    assert!(
        !graph_mirror.local_only_commits.is_empty(),
        "feature commit should be local-only relative to mirror (branch not published there)"
    );
}

#[test]
fn branch_graph_local_only_skips_branches_without_remote_tracking() {
    // Reproduce: when a local branch has no corresponding remote ref,
    // the local-only detection should not fail with "unknown revision".
    let bare_dir = TempDir::new().expect("create bare dir");
    let bare_path = bare_dir.path();

    // Create a bare "remote" repo with one commit on main.
    Command::new("git")
        .args(["init", "--bare"])
        .current_dir(bare_path)
        .output()
        .expect("git init --bare");

    let dir = TempDir::new().expect("create work dir");
    let path = dir.path();

    // Clone the bare repo so we have an "origin" remote.
    Command::new("git")
        .args(["clone", bare_path.to_str().unwrap(), path.to_str().unwrap()])
        .output()
        .expect("git clone");

    // Configure user for commits.
    Command::new("git")
        .args(["config", "user.email", "test@test.com"])
        .current_dir(path)
        .output()
        .unwrap();
    Command::new("git")
        .args(["config", "user.name", "Test"])
        .current_dir(path)
        .output()
        .unwrap();

    // Create an initial commit and push to origin/main.
    std::fs::write(path.join("file.txt"), "hello").unwrap();
    Command::new("git")
        .args(["add", "."])
        .current_dir(path)
        .output()
        .unwrap();
    Command::new("git")
        .args(["commit", "-m", "initial"])
        .current_dir(path)
        .output()
        .unwrap();
    Command::new("git")
        .args(["push", "origin", "HEAD"])
        .current_dir(path)
        .output()
        .unwrap();

    // Create a local-only branch that has NO remote tracking ref.
    Command::new("git")
        .args(["checkout", "-b", "local-only-branch"])
        .current_dir(path)
        .output()
        .unwrap();
    std::fs::write(path.join("local.txt"), "local").unwrap();
    Command::new("git")
        .args(["add", "."])
        .current_dir(path)
        .output()
        .unwrap();
    Command::new("git")
        .args(["commit", "-m", "local commit"])
        .current_dir(path)
        .output()
        .unwrap();

    let log = CommandLog::new(50);
    let provider = GitProvider::new(log);

    // This should succeed even though origin/local-only-branch doesn't exist.
    let graph = provider
        .branch_graph(path, &[], Some("origin"), None)
        .expect("branch_graph should not fail when some branches lack remote tracking");

    // The local-only branch commit should still be detected or gracefully skipped.
    assert!(graph.commits.len() >= 2);
    assert!(graph.branches.contains(&"local-only-branch".to_string()));

    // The commit on the unpublished branch should be marked local-only.
    assert!(
        !graph.local_only_commits.is_empty(),
        "commits on an unpublished branch should be marked local-only, got: {:?}",
        graph.local_only_commits
    );
}

#[test]
fn fetch_prunes_stale_remote_refs() {
    // Scenario: a branch is pushed to a remote, then deleted from the remote.
    // After fetching, the stale remote-tracking ref should be removed.
    let bare_dir = TempDir::new().expect("create bare dir");

    Command::new("git")
        .args(["init", "--bare"])
        .current_dir(bare_dir.path())
        .output()
        .expect("git init --bare");

    let dir = TempDir::new().expect("create work dir");
    let path = dir.path();

    Command::new("git")
        .args(["clone", bare_dir.path().to_str().unwrap(), path.to_str().unwrap()])
        .output()
        .expect("git clone");

    Command::new("git")
        .args(["config", "user.email", "test@test.com"])
        .current_dir(path)
        .output()
        .unwrap();
    Command::new("git")
        .args(["config", "user.name", "Test"])
        .current_dir(path)
        .output()
        .unwrap();

    // Create initial commit and push.
    std::fs::write(path.join("file.txt"), "hello").unwrap();
    Command::new("git")
        .args(["add", "."])
        .current_dir(path)
        .output()
        .unwrap();
    Command::new("git")
        .args(["commit", "-m", "initial"])
        .current_dir(path)
        .output()
        .unwrap();
    Command::new("git")
        .args(["push", "origin", "HEAD"])
        .current_dir(path)
        .output()
        .unwrap();

    // Create a feature branch and push it.
    Command::new("git")
        .args(["checkout", "-b", "feature-branch"])
        .current_dir(path)
        .output()
        .unwrap();
    std::fs::write(path.join("feature.txt"), "feature").unwrap();
    Command::new("git")
        .args(["add", "."])
        .current_dir(path)
        .output()
        .unwrap();
    Command::new("git")
        .args(["commit", "-m", "feature commit"])
        .current_dir(path)
        .output()
        .unwrap();
    Command::new("git")
        .args(["push", "origin", "feature-branch"])
        .current_dir(path)
        .output()
        .unwrap();

    // Verify the remote-tracking ref exists.
    let verify = Command::new("git")
        .args(["rev-parse", "--verify", "refs/remotes/origin/feature-branch"])
        .current_dir(path)
        .output()
        .unwrap();
    assert!(verify.status.success(), "remote ref should exist after push");

    // Delete the branch on the bare remote directly (simulating server-side deletion).
    Command::new("git")
        .args(["branch", "-D", "feature-branch"])
        .current_dir(bare_dir.path())
        .output()
        .unwrap();

    // Go back to main branch so we're not on the deleted branch.
    let main_branch = {
        let out = Command::new("git")
            .args(["branch", "--list", "main", "master"])
            .current_dir(path)
            .output()
            .unwrap();
        let s = String::from_utf8_lossy(&out.stdout);
        s.lines()
            .next()
            .unwrap_or("master")
            .trim()
            .trim_start_matches("* ")
            .to_string()
    };
    Command::new("git")
        .args(["checkout", &main_branch])
        .current_dir(path)
        .output()
        .unwrap();

    let log = CommandLog::new(50);
    let provider = GitProvider::new(log);

    // Fetch from origin — should prune the stale ref.
    provider.fetch(path, "origin").expect("fetch should succeed");

    // The stale remote-tracking ref should be gone now.
    let verify_after = Command::new("git")
        .args(["rev-parse", "--verify", "refs/remotes/origin/feature-branch"])
        .current_dir(path)
        .output()
        .unwrap();
    assert!(
        !verify_after.status.success(),
        "stale remote ref should be pruned after fetch"
    );
}

#[test]
fn remote_branch_status_detects_unpublished_after_prune() {
    // After fetching with prune, remote_branch_status should correctly
    // return None for branches that were deleted from the remote.
    let bare_dir = TempDir::new().expect("create bare dir");

    Command::new("git")
        .args(["init", "--bare"])
        .current_dir(bare_dir.path())
        .output()
        .expect("git init --bare");

    let dir = TempDir::new().expect("create work dir");
    let path = dir.path();

    Command::new("git")
        .args(["clone", bare_dir.path().to_str().unwrap(), path.to_str().unwrap()])
        .output()
        .expect("git clone");

    Command::new("git")
        .args(["config", "user.email", "test@test.com"])
        .current_dir(path)
        .output()
        .unwrap();
    Command::new("git")
        .args(["config", "user.name", "Test"])
        .current_dir(path)
        .output()
        .unwrap();

    // Create initial commit and push.
    std::fs::write(path.join("file.txt"), "hello").unwrap();
    Command::new("git")
        .args(["add", "."])
        .current_dir(path)
        .output()
        .unwrap();
    Command::new("git")
        .args(["commit", "-m", "initial"])
        .current_dir(path)
        .output()
        .unwrap();
    Command::new("git")
        .args(["push", "origin", "HEAD"])
        .current_dir(path)
        .output()
        .unwrap();

    // Create a feature branch and push it.
    Command::new("git")
        .args(["checkout", "-b", "my-feature"])
        .current_dir(path)
        .output()
        .unwrap();
    std::fs::write(path.join("feature.txt"), "feature").unwrap();
    Command::new("git")
        .args(["add", "."])
        .current_dir(path)
        .output()
        .unwrap();
    Command::new("git")
        .args(["commit", "-m", "feature commit"])
        .current_dir(path)
        .output()
        .unwrap();
    Command::new("git")
        .args(["push", "origin", "my-feature"])
        .current_dir(path)
        .output()
        .unwrap();

    let log = CommandLog::new(50);
    let provider = GitProvider::new(log);

    // Before deletion, status should show the branch exists.
    let status_before = provider
        .remote_branch_status(path, "origin")
        .expect("should succeed");
    assert!(
        status_before.is_some(),
        "branch should be reported as existing before deletion"
    );

    // Delete the branch on the bare remote.
    Command::new("git")
        .args(["branch", "-D", "my-feature"])
        .current_dir(bare_dir.path())
        .output()
        .unwrap();

    // Fetch with prune.
    provider.fetch(path, "origin").expect("fetch should succeed");

    // Now remote_branch_status should return None (branch not on remote).
    let status_after = provider
        .remote_branch_status(path, "origin")
        .expect("should succeed");
    assert!(
        status_after.is_none(),
        "branch should be reported as NOT existing after prune, got: {:?}",
        status_after
    );
}
