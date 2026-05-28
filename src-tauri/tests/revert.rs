use std::fs;
use std::process::Command;

use cheesegit_lib::command_log::CommandLog;
use cheesegit_lib::vcs::git::GitProvider;
use cheesegit_lib::vcs::traits::VcsProvider;
use cheesegit_lib::vcs::types::{ConflictResolution, RevertResult};
mod test_utils;

use test_utils::{commit_file, make_temp_repo};

#[test]
fn revert_clean() {
    let dir = make_temp_repo();
    let log = CommandLog::new(50);
    let provider = GitProvider::new(log);

    // Create initial commit
    commit_file(dir.path(), "file.txt", "initial content\n", "initial commit");

    // Create a second commit that we'll revert
    let hash = commit_file(dir.path(), "file.txt", "modified content\n", "second commit");

    // Revert the second commit
    let result = provider.revert_commit(dir.path(), &hash).unwrap();
    assert!(matches!(result, RevertResult::Success));

    // The file should be back to its initial content
    let content = fs::read_to_string(dir.path().join("file.txt")).unwrap();
    assert_eq!(content, "initial content\n");
}

#[test]
fn revert_with_conflicts() {
    let dir = make_temp_repo();
    let log = CommandLog::new(50);
    let provider = GitProvider::new(log);

    // Create initial commit
    commit_file(dir.path(), "file.txt", "line1\nline2\nline3\n", "initial commit");

    // Create a second commit modifying line2
    let hash = commit_file(dir.path(), "file.txt", "line1\nchanged_line2\nline3\n", "modify line2");

    // Create a third commit that also modifies line2 (will conflict with revert)
    commit_file(dir.path(), "file.txt", "line1\ndifferent_line2\nline3\n", "also modify line2");

    // Revert the second commit — should conflict
    let result = provider.revert_commit(dir.path(), &hash).unwrap();
    match result {
        RevertResult::Conflict(info) => {
            assert!(!info.conflicted_files.is_empty());
            assert!(info.conflicted_files.contains(&"file.txt".to_string()));
        }
        RevertResult::Success => panic!("Expected conflict but got success"),
    }

    // REVERT_HEAD should exist
    assert!(dir.path().join(".git/REVERT_HEAD").exists());
}

#[test]
fn revert_abort() {
    let dir = make_temp_repo();
    let log = CommandLog::new(50);
    let provider = GitProvider::new(log);

    // Create initial commit
    commit_file(dir.path(), "file.txt", "line1\nline2\nline3\n", "initial commit");

    // Create a second commit
    let hash = commit_file(dir.path(), "file.txt", "line1\nchanged_line2\nline3\n", "modify line2");

    // Create a third commit that conflicts
    commit_file(dir.path(), "file.txt", "line1\ndifferent_line2\nline3\n", "also modify line2");

    // Revert the second commit — conflicts
    let result = provider.revert_commit(dir.path(), &hash).unwrap();
    assert!(matches!(result, RevertResult::Conflict(_)));

    // Abort the revert
    provider.revert_abort(dir.path()).unwrap();

    // REVERT_HEAD should be gone
    assert!(!dir.path().join(".git/REVERT_HEAD").exists());

    // Content should be unchanged (third commit's content)
    let content = fs::read_to_string(dir.path().join("file.txt")).unwrap();
    assert_eq!(content, "line1\ndifferent_line2\nline3\n");
}

#[test]
fn revert_resolve_accept_current() {
    let dir = make_temp_repo();
    let log = CommandLog::new(50);
    let provider = GitProvider::new(log);

    commit_file(dir.path(), "file.txt", "line1\nline2\nline3\n", "initial commit");
    let hash = commit_file(dir.path(), "file.txt", "line1\nchanged_line2\nline3\n", "modify line2");
    commit_file(dir.path(), "file.txt", "line1\ndifferent_line2\nline3\n", "also modify line2");

    let result = provider.revert_commit(dir.path(), &hash).unwrap();
    assert!(matches!(result, RevertResult::Conflict(_)));

    // Resolve accepting current (keep our version — the third commit)
    provider.resolve_conflict(dir.path(), "file.txt", ConflictResolution::AcceptCurrent).unwrap();

    // Continue the revert
    provider.revert_continue(dir.path()).unwrap();

    // REVERT_HEAD should be gone
    assert!(!dir.path().join(".git/REVERT_HEAD").exists());
}

#[test]
fn revert_resolve_accept_incoming() {
    let dir = make_temp_repo();
    let log = CommandLog::new(50);
    let provider = GitProvider::new(log);

    commit_file(dir.path(), "file.txt", "line1\nline2\nline3\n", "initial commit");
    let hash = commit_file(dir.path(), "file.txt", "line1\nchanged_line2\nline3\n", "modify line2");
    commit_file(dir.path(), "file.txt", "line1\ndifferent_line2\nline3\n", "also modify line2");

    let result = provider.revert_commit(dir.path(), &hash).unwrap();
    assert!(matches!(result, RevertResult::Conflict(_)));

    // Resolve accepting incoming (accept the reverted change — original line2)
    provider.resolve_conflict(dir.path(), "file.txt", ConflictResolution::AcceptIncoming).unwrap();

    // Continue the revert
    provider.revert_continue(dir.path()).unwrap();

    assert!(!dir.path().join(".git/REVERT_HEAD").exists());
}

#[test]
fn revert_resolve_accept_both() {
    let dir = make_temp_repo();
    let log = CommandLog::new(50);
    let provider = GitProvider::new(log);

    commit_file(dir.path(), "file.txt", "line1\nline2\nline3\n", "initial commit");
    let hash = commit_file(dir.path(), "file.txt", "line1\nchanged_line2\nline3\n", "modify line2");
    commit_file(dir.path(), "file.txt", "line1\ndifferent_line2\nline3\n", "also modify line2");

    let result = provider.revert_commit(dir.path(), &hash).unwrap();
    assert!(matches!(result, RevertResult::Conflict(_)));

    // Resolve accepting both
    provider.resolve_conflict(dir.path(), "file.txt", ConflictResolution::AcceptBoth).unwrap();

    // Continue the revert
    provider.revert_continue(dir.path()).unwrap();

    assert!(!dir.path().join(".git/REVERT_HEAD").exists());
}

#[test]
fn revert_merge_conflicts_detects_revert_state() {
    let dir = make_temp_repo();
    let log = CommandLog::new(50);
    let provider = GitProvider::new(log);

    commit_file(dir.path(), "file.txt", "line1\nline2\nline3\n", "initial commit");
    let hash = commit_file(
        dir.path(),
        "file.txt",
        "line1\nchanged_line2\nline3\n",
        "modify line2",
    );
    commit_file(
        dir.path(),
        "file.txt",
        "line1\ndifferent_line2\nline3\n",
        "also modify line2",
    );

    // Start a conflicting revert
    let result = provider.revert_commit(dir.path(), &hash).unwrap();
    assert!(matches!(result, RevertResult::Conflict(_)));

    // merge_conflicts() should detect the revert state too
    let info = provider.merge_conflicts(dir.path()).unwrap();
    assert!(!info.conflicted_files.is_empty());
    assert!(info.conflicted_files.contains(&"file.txt".to_string()));

    // Clean up
    provider.revert_abort(dir.path()).unwrap();
}

#[test]
fn revert_no_op_when_nothing_to_revert() {
    let dir = make_temp_repo();
    let log = CommandLog::new(50);
    let provider = GitProvider::new(log);

    commit_file(dir.path(), "file.txt", "content\n", "initial commit");

    // Reverting a commit that introduces no conflict and only adds content should work
    let hash = commit_file(dir.path(), "added.txt", "new file\n", "add new file");

    let result = provider.revert_commit(dir.path(), &hash).unwrap();
    assert!(matches!(result, RevertResult::Success));

    // The added file should be gone after reverting
    assert!(!dir.path().join("added.txt").exists());
}

#[test]
fn revert_merge_commit() {
    let dir = make_temp_repo();
    let log = CommandLog::new(50);
    let provider = GitProvider::new(log);

    // Create initial commit on main
    commit_file(dir.path(), "file.txt", "initial\n", "initial commit");

    // Create a feature branch with a change
    Command::new("git")
        .args(["checkout", "-b", "feature"])
        .current_dir(dir.path())
        .output()
        .unwrap();
    commit_file(dir.path(), "feature.txt", "feature content\n", "add feature file");

    // Switch back to main/master and merge
    let main_branch = {
        let output = Command::new("git")
            .args(["branch", "--list", "main"])
            .current_dir(dir.path())
            .output()
            .unwrap();
        if String::from_utf8_lossy(&output.stdout).trim().is_empty() {
            "master"
        } else {
            "main"
        }
    };
    Command::new("git")
        .args(["checkout", main_branch])
        .current_dir(dir.path())
        .output()
        .unwrap();
    let merge_output = Command::new("git")
        .args(["merge", "feature", "--no-edit"])
        .current_dir(dir.path())
        .output()
        .unwrap();
    assert!(merge_output.status.success(), "merge failed");

    // feature.txt should exist after merge
    assert!(dir.path().join("feature.txt").exists());

    // Get the merge commit hash
    let hash_output = Command::new("git")
        .args(["rev-parse", "HEAD"])
        .current_dir(dir.path())
        .output()
        .unwrap();
    let merge_hash = String::from_utf8(hash_output.stdout).unwrap().trim().to_string();

    // Revert the merge commit — should automatically use -m 1
    let result = provider.revert_commit(dir.path(), &merge_hash).unwrap();
    assert!(matches!(result, RevertResult::Success));

    // feature.txt should be gone after reverting the merge
    assert!(!dir.path().join("feature.txt").exists());
}
