use std::process::Command;

use cheesegit_lib::command_log::CommandLog;
use cheesegit_lib::vcs::git::GitProvider;
use cheesegit_lib::vcs::traits::VcsProvider;
use tempfile::TempDir;
mod test_utils;

use test_utils::{current_branch, make_temp_repo_with_commit};

#[test]
fn undo_last_commit_restores_message_and_staged_changes() {
    let dir = make_temp_repo_with_commit();
    let path = dir.path();
    let provider = GitProvider::new(CommandLog::new(100));

    std::fs::write(path.join("hello.txt"), "line1 changed\nline2\nline3\nline4\n").unwrap();
    provider.stage_files(path, &["hello.txt"]).unwrap();
    provider
        .commit(path, "feat: update hello", "body line 1\nbody line 2", false)
        .unwrap();

    let restored = provider.undo_last_commit(path).unwrap();

    assert_eq!(restored.summary, "feat: update hello");
    assert_eq!(restored.description, "body line 1\nbody line 2");

    let status = provider.status(path).unwrap();
    assert!(status.unstaged.is_empty());
    assert!(status.staged.iter().any(|e| e.path == "hello.txt"));

    let log = provider.commit_log(path, 1).unwrap();
    assert_eq!(log[0].summary, "initial commit");
}

#[test]
fn undo_last_commit_rejects_if_latest_commit_is_pushed() {
    let local = make_temp_repo_with_commit();
    let local_path = local.path();
    let provider = GitProvider::new(CommandLog::new(200));

    let remote = TempDir::new().unwrap();
    let init_remote = Command::new("git")
        .args(["init", "--bare"])
        .current_dir(remote.path())
        .output()
        .unwrap();
    assert!(init_remote.status.success());

    let add_remote = Command::new("git")
        .args(["remote", "add", "origin", remote.path().to_string_lossy().as_ref()])
        .current_dir(local_path)
        .output()
        .unwrap();
    assert!(add_remote.status.success());

    let branch = current_branch(local_path);
    let first_push = Command::new("git")
        .args(["push", "-u", "origin", &branch])
        .current_dir(local_path)
        .output()
        .unwrap();
    assert!(first_push.status.success());

    std::fs::write(local_path.join("hello.txt"), "line1\nline2\nline3 changed\nline4\n").unwrap();
    provider.stage_files(local_path, &["hello.txt"]).unwrap();
    provider
        .commit(local_path, "feat: pushed commit", "", false)
        .unwrap();

    let second_push = Command::new("git")
        .args(["push", "origin", &branch])
        .current_dir(local_path)
        .output()
        .unwrap();
    assert!(second_push.status.success());

    let result = provider.undo_last_commit(local_path);
    assert!(result.is_err());
    let message = format!("{}", result.unwrap_err());
    assert!(message.contains("already pushed"));
}

#[test]
fn undo_last_commit_allows_when_no_remote_configured() {
    let dir = make_temp_repo_with_commit();
    let path = dir.path();
    let provider = GitProvider::new(CommandLog::new(100));

    std::fs::write(path.join("hello.txt"), "local change only\n").unwrap();
    provider.stage_files(path, &["hello.txt"]).unwrap();
    provider
        .commit(path, "feat: local only", "", false)
        .unwrap();

    let result = provider.undo_last_commit(path);
    assert!(result.is_ok(), "undo should succeed when no remotes are configured");
}

#[test]
fn undo_last_commit_allows_when_remote_exists_but_commit_is_unpushed() {
    let local = make_temp_repo_with_commit();
    let local_path = local.path();
    let provider = GitProvider::new(CommandLog::new(200));

    let remote = TempDir::new().unwrap();
    let init_remote = Command::new("git")
        .args(["init", "--bare"])
        .current_dir(remote.path())
        .output()
        .unwrap();
    assert!(init_remote.status.success());

    let add_remote = Command::new("git")
        .args(["remote", "add", "origin", remote.path().to_string_lossy().as_ref()])
        .current_dir(local_path)
        .output()
        .unwrap();
    assert!(add_remote.status.success());

    let branch = current_branch(local_path);
    let first_push = Command::new("git")
        .args(["push", "-u", "origin", &branch])
        .current_dir(local_path)
        .output()
        .unwrap();
    assert!(first_push.status.success());

    std::fs::write(local_path.join("hello.txt"), "unpushed local commit\n").unwrap();
    provider.stage_files(local_path, &["hello.txt"]).unwrap();
    provider
        .commit(local_path, "feat: local ahead", "", false)
        .unwrap();

    let result = provider.undo_last_commit(local_path);
    assert!(
        result.is_ok(),
        "undo should succeed when remote exists but latest commit is not pushed"
    );
}

#[test]
fn undo_last_commit_preserves_partial_staging_state() {
    let dir = make_temp_repo_with_commit();
    let path = dir.path();
    let provider = GitProvider::new(CommandLog::new(200));

    // Create a partially-staged state for a single file:
    // 1) stage first change, 2) edit again without staging.
    std::fs::write(path.join("hello.txt"), "line1 changed\nline2\nline3\nline4\n").unwrap();
    provider.stage_files(path, &["hello.txt"]).unwrap();
    std::fs::write(path.join("hello.txt"), "line1 changed\nline2\nline3 changed\nline4\n").unwrap();

    let partial_status = provider.status(path).unwrap();
    assert!(partial_status.staged.iter().any(|e| e.path == "hello.txt"));
    assert!(partial_status.unstaged.iter().any(|e| e.path == "hello.txt"));

    provider
        .commit(path, "feat: partial", "partial body", false)
        .unwrap();

    let after_commit = provider.status(path).unwrap();
    assert!(after_commit.staged.is_empty());
    assert!(after_commit.unstaged.iter().any(|e| e.path == "hello.txt"));

    let restored = provider.undo_last_commit(path).unwrap();
    assert_eq!(restored.summary, "feat: partial");
    assert_eq!(restored.description, "partial body");

    let after_undo = provider.status(path).unwrap();
    assert!(after_undo.staged.iter().any(|e| e.path == "hello.txt"));
    assert!(after_undo.unstaged.iter().any(|e| e.path == "hello.txt"));
}
