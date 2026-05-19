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
}
