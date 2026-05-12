use std::process::Command;

use cheesegit_lib::command_log::CommandLog;
use cheesegit_lib::vcs::git::GitProvider;
use cheesegit_lib::vcs::traits::VcsProvider;
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

    let info = provider.open_repository(dir.path()).expect("should succeed");

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

    provider.open_repository(dir.path()).expect("should succeed");

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
