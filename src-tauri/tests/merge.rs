use std::fs;
use std::process::Command;

use cheesegit_lib::command_log::CommandLog;
use cheesegit_lib::vcs::git::GitProvider;
use cheesegit_lib::vcs::traits::VcsProvider;
use cheesegit_lib::vcs::types::{ConflictResolution, MergeResult};
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

    // Configure user for commits
    Command::new("git")
        .args(["config", "user.email", "test@test.com"])
        .current_dir(dir.path())
        .output()
        .unwrap();
    Command::new("git")
        .args(["config", "user.name", "Test User"])
        .current_dir(dir.path())
        .output()
        .unwrap();

    dir
}

/// Helper: create a file, stage, and commit it.
fn commit_file(dir: &TempDir, filename: &str, content: &str, message: &str) {
    fs::write(dir.path().join(filename), content).unwrap();
    Command::new("git")
        .args(["add", filename])
        .current_dir(dir.path())
        .output()
        .unwrap();
    Command::new("git")
        .args(["commit", "-m", message])
        .current_dir(dir.path())
        .output()
        .unwrap();
}

#[test]
fn merge_fast_forward() {
    let dir = make_temp_repo();
    let log = CommandLog::new(50);
    let provider = GitProvider::new(log);

    // Create initial commit on main
    commit_file(&dir, "file.txt", "initial content", "initial commit");

    // Create a branch and add a commit
    Command::new("git")
        .args(["checkout", "-b", "feature"])
        .current_dir(dir.path())
        .output()
        .unwrap();
    commit_file(&dir, "feature.txt", "feature content", "feature commit");

    // Switch back to main/master
    let branch_output = Command::new("git")
        .args(["branch", "--show-current"])
        .current_dir(dir.path())
        .output()
        .unwrap();
    // We're on "feature", switch to the default branch
    Command::new("git")
        .args(["checkout", "-"])
        .current_dir(dir.path())
        .output()
        .unwrap();

    // Merge feature → should fast-forward
    let result = provider
        .merge_branch(dir.path(), "feature")
        .expect("merge should succeed");
    assert!(
        matches!(result, MergeResult::Success),
        "expected fast-forward merge success, got: {:?}",
        result
    );

    // Verify the file exists after merge
    assert!(dir.path().join("feature.txt").exists());
    let _ = branch_output;
}

#[test]
fn merge_clean_no_conflicts() {
    let dir = make_temp_repo();
    let log = CommandLog::new(50);
    let provider = GitProvider::new(log);

    // Create initial commit on main
    commit_file(&dir, "base.txt", "base", "initial");

    // Create feature branch
    Command::new("git")
        .args(["checkout", "-b", "feature"])
        .current_dir(dir.path())
        .output()
        .unwrap();
    commit_file(&dir, "feature.txt", "feature work", "feature commit");

    // Go back to default branch and make a different change
    Command::new("git")
        .args(["checkout", "-"])
        .current_dir(dir.path())
        .output()
        .unwrap();
    commit_file(&dir, "main.txt", "main work", "main commit");

    // Merge feature — different files, no conflict
    let result = provider
        .merge_branch(dir.path(), "feature")
        .expect("merge should succeed");
    assert!(
        matches!(result, MergeResult::Success),
        "expected clean merge, got: {:?}",
        result
    );

    // Both files should exist
    assert!(dir.path().join("feature.txt").exists());
    assert!(dir.path().join("main.txt").exists());
}

#[test]
fn merge_with_conflicts() {
    let dir = make_temp_repo();
    let log = CommandLog::new(50);
    let provider = GitProvider::new(log);

    // Create initial commit
    commit_file(&dir, "shared.txt", "line 1\nline 2\nline 3\n", "initial");

    // Create feature branch with conflicting change
    Command::new("git")
        .args(["checkout", "-b", "feature"])
        .current_dir(dir.path())
        .output()
        .unwrap();
    commit_file(
        &dir,
        "shared.txt",
        "line 1\nfeature change\nline 3\n",
        "feature edit",
    );

    // Go back and make conflicting change on main
    Command::new("git")
        .args(["checkout", "-"])
        .current_dir(dir.path())
        .output()
        .unwrap();
    commit_file(
        &dir,
        "shared.txt",
        "line 1\nmain change\nline 3\n",
        "main edit",
    );

    // Merge feature — should conflict
    let result = provider
        .merge_branch(dir.path(), "feature")
        .expect("merge command should not error");

    match result {
        MergeResult::Conflict(info) => {
            assert_eq!(info.incoming_branch, "feature");
            assert!(info.conflicted_files.contains(&"shared.txt".to_string()));
        }
        MergeResult::Success => panic!("expected conflict, got success"),
    }
}

#[test]
fn merge_abort_restores_state() {
    let dir = make_temp_repo();
    let log = CommandLog::new(50);
    let provider = GitProvider::new(log);

    // Create conflict scenario
    commit_file(&dir, "shared.txt", "original\n", "initial");
    Command::new("git")
        .args(["checkout", "-b", "feature"])
        .current_dir(dir.path())
        .output()
        .unwrap();
    commit_file(&dir, "shared.txt", "feature version\n", "feature");
    Command::new("git")
        .args(["checkout", "-"])
        .current_dir(dir.path())
        .output()
        .unwrap();
    commit_file(&dir, "shared.txt", "main version\n", "main");

    // Merge → conflict
    let result = provider.merge_branch(dir.path(), "feature").unwrap();
    assert!(matches!(result, MergeResult::Conflict(_)));

    // Abort
    provider.merge_abort(dir.path()).expect("abort should succeed");

    // File should be back to main version
    let content = fs::read_to_string(dir.path().join("shared.txt")).unwrap();
    assert_eq!(content, "main version\n");

    // MERGE_HEAD should no longer exist
    assert!(!dir.path().join(".git").join("MERGE_HEAD").exists());
}

#[test]
fn resolve_conflict_accept_current() {
    let dir = make_temp_repo();
    let log = CommandLog::new(50);
    let provider = GitProvider::new(log);

    // Create conflict
    commit_file(&dir, "file.txt", "original\n", "initial");
    Command::new("git")
        .args(["checkout", "-b", "feature"])
        .current_dir(dir.path())
        .output()
        .unwrap();
    commit_file(&dir, "file.txt", "feature version\n", "feature");
    Command::new("git")
        .args(["checkout", "-"])
        .current_dir(dir.path())
        .output()
        .unwrap();
    commit_file(&dir, "file.txt", "main version\n", "main");

    let result = provider.merge_branch(dir.path(), "feature").unwrap();
    assert!(matches!(result, MergeResult::Conflict(_)));

    // Resolve with accept current (ours)
    provider
        .resolve_conflict(dir.path(), "file.txt", ConflictResolution::AcceptCurrent)
        .expect("resolve should succeed");

    let content = fs::read_to_string(dir.path().join("file.txt")).unwrap();
    assert_eq!(content, "main version\n");
}

#[test]
fn resolve_conflict_accept_incoming() {
    let dir = make_temp_repo();
    let log = CommandLog::new(50);
    let provider = GitProvider::new(log);

    // Create conflict
    commit_file(&dir, "file.txt", "original\n", "initial");
    Command::new("git")
        .args(["checkout", "-b", "feature"])
        .current_dir(dir.path())
        .output()
        .unwrap();
    commit_file(&dir, "file.txt", "feature version\n", "feature");
    Command::new("git")
        .args(["checkout", "-"])
        .current_dir(dir.path())
        .output()
        .unwrap();
    commit_file(&dir, "file.txt", "main version\n", "main");

    let result = provider.merge_branch(dir.path(), "feature").unwrap();
    assert!(matches!(result, MergeResult::Conflict(_)));

    // Resolve with accept incoming (theirs)
    provider
        .resolve_conflict(dir.path(), "file.txt", ConflictResolution::AcceptIncoming)
        .expect("resolve should succeed");

    let content = fs::read_to_string(dir.path().join("file.txt")).unwrap();
    assert_eq!(content, "feature version\n");
}

#[test]
fn resolve_conflict_accept_both() {
    let dir = make_temp_repo();
    let log = CommandLog::new(50);
    let provider = GitProvider::new(log);

    // Create conflict
    commit_file(&dir, "file.txt", "original\n", "initial");
    Command::new("git")
        .args(["checkout", "-b", "feature"])
        .current_dir(dir.path())
        .output()
        .unwrap();
    commit_file(&dir, "file.txt", "feature version\n", "feature");
    Command::new("git")
        .args(["checkout", "-"])
        .current_dir(dir.path())
        .output()
        .unwrap();
    commit_file(&dir, "file.txt", "main version\n", "main");

    let result = provider.merge_branch(dir.path(), "feature").unwrap();
    assert!(matches!(result, MergeResult::Conflict(_)));

    // Resolve with accept both
    provider
        .resolve_conflict(dir.path(), "file.txt", ConflictResolution::AcceptBoth)
        .expect("resolve should succeed");

    let content = fs::read_to_string(dir.path().join("file.txt")).unwrap();
    // Both sides should be present (conflict markers stripped, both kept)
    assert!(content.contains("main version"));
    assert!(content.contains("feature version"));
    // No conflict markers should remain
    assert!(!content.contains("<<<<<<<"));
    assert!(!content.contains(">>>>>>>"));
    assert!(!content.contains("======="));
}

#[test]
fn merge_conflicts_returns_empty_when_no_merge() {
    let dir = make_temp_repo();
    let log = CommandLog::new(50);
    let provider = GitProvider::new(log);

    commit_file(&dir, "file.txt", "content\n", "initial");

    let info = provider
        .merge_conflicts(dir.path())
        .expect("should succeed");
    assert!(info.conflicted_files.is_empty());
    assert!(info.incoming_branch.is_empty());
}

#[test]
fn merge_continue_after_resolving_all_conflicts() {
    let dir = make_temp_repo();
    let log = CommandLog::new(50);
    let provider = GitProvider::new(log);

    // Create conflict
    commit_file(&dir, "file.txt", "original\n", "initial");
    Command::new("git")
        .args(["checkout", "-b", "feature"])
        .current_dir(dir.path())
        .output()
        .unwrap();
    commit_file(&dir, "file.txt", "feature version\n", "feature");
    Command::new("git")
        .args(["checkout", "-"])
        .current_dir(dir.path())
        .output()
        .unwrap();
    commit_file(&dir, "file.txt", "main version\n", "main");

    let result = provider.merge_branch(dir.path(), "feature").unwrap();
    assert!(matches!(result, MergeResult::Conflict(_)));

    // Resolve
    provider
        .resolve_conflict(dir.path(), "file.txt", ConflictResolution::AcceptCurrent)
        .unwrap();

    // Finalize
    provider
        .merge_continue(dir.path(), "Merge branch 'feature'")
        .expect("merge continue should succeed");

    // MERGE_HEAD should be gone
    assert!(!dir.path().join(".git").join("MERGE_HEAD").exists());

    // Should be back on a clean state
    let status = provider.status(dir.path()).unwrap();
    assert!(status.staged.is_empty());
    assert!(status.unstaged.is_empty());
}
