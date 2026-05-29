use std::fs;
use std::process::Command;

use cheesegit_lib::command_log::CommandLog;
use cheesegit_lib::vcs::git::GitProvider;
use cheesegit_lib::vcs::traits::VcsProvider;
use cheesegit_lib::vcs::types::{ConflictResolution, MergeResult, RevertResult};

mod test_utils;

use test_utils::{commit_file, make_temp_repo};

/// Test AcceptCurrent strategy: keeps the current (main) branch version
#[test]
fn merge_conflict_accept_current_strategy() {
    let dir = make_temp_repo();
    let log = CommandLog::new(50);
    let provider = GitProvider::new(log);

    // Create initial shared file on main
    commit_file(dir.path(), "shared.txt", "main content", "initial commit");

    // Create feature branch and modify same file
    Command::new("git")
        .args(["checkout", "-b", "feature"])
        .current_dir(dir.path())
        .output()
        .unwrap();
    commit_file(dir.path(), "shared.txt", "feature content", "feature commit");

    // Switch back to main and modify the file differently
    Command::new("git")
        .args(["checkout", "-"])
        .current_dir(dir.path())
        .output()
        .unwrap();
    commit_file(dir.path(), "shared.txt", "main modified content", "main commit");

    // Merge should produce conflict
    let merge_result = provider
        .merge_branch(dir.path(), "feature")
        .expect("merge should be attempted");

    assert!(matches!(merge_result, MergeResult::Conflict { .. }));

    // Resolve using AcceptCurrent (keep main version)
    provider
        .resolve_conflict(dir.path(), "shared.txt", ConflictResolution::AcceptCurrent)
        .expect("resolve should succeed");

    // Verify file contains main's version
    let content = fs::read_to_string(dir.path().join("shared.txt")).unwrap();
    assert_eq!(content, "main modified content");

    // Continue the merge
    provider
        .merge_continue(dir.path(), "Merge feature into main")
        .expect("merge continue should succeed");

    // Verify merge completed (MERGE_HEAD gone)
    assert!(!dir.path().join(".git").join("MERGE_HEAD").exists());
}

/// Test AcceptIncoming strategy: keeps the incoming (feature) branch version
#[test]
fn merge_conflict_accept_incoming_strategy() {
    let dir = make_temp_repo();
    let log = CommandLog::new(50);
    let provider = GitProvider::new(log);

    // Create initial shared file on main
    commit_file(dir.path(), "shared.txt", "main content", "initial commit");

    // Create feature branch and modify same file
    Command::new("git")
        .args(["checkout", "-b", "feature"])
        .current_dir(dir.path())
        .output()
        .unwrap();
    commit_file(dir.path(), "shared.txt", "feature content", "feature commit");

    // Switch back to main and modify the file differently
    Command::new("git")
        .args(["checkout", "-"])
        .current_dir(dir.path())
        .output()
        .unwrap();
    commit_file(dir.path(), "shared.txt", "main modified content", "main commit");

    // Merge should produce conflict
    let merge_result = provider
        .merge_branch(dir.path(), "feature")
        .expect("merge should be attempted");

    assert!(matches!(merge_result, MergeResult::Conflict { .. }));

    // Resolve using AcceptIncoming (keep feature version)
    provider
        .resolve_conflict(dir.path(), "shared.txt", ConflictResolution::AcceptIncoming)
        .expect("resolve should succeed");

    // Verify file contains feature's version
    let content = fs::read_to_string(dir.path().join("shared.txt")).unwrap();
    assert_eq!(content, "feature content");

    // Continue the merge
    provider
        .merge_continue(dir.path(), "Merge feature into main")
        .expect("merge continue should succeed");

    // Verify merge completed
    assert!(!dir.path().join(".git").join("MERGE_HEAD").exists());
}

/// Test AcceptBoth strategy: keeps both versions with conflict markers removed
#[test]
fn merge_conflict_accept_both_strategy() {
    let dir = make_temp_repo();
    let log = CommandLog::new(50);
    let provider = GitProvider::new(log);

    // Create initial shared file on main
    commit_file(dir.path(), "shared.txt", "main content\n", "initial commit");

    // Create feature branch and modify same file
    Command::new("git")
        .args(["checkout", "-b", "feature"])
        .current_dir(dir.path())
        .output()
        .unwrap();
    commit_file(dir.path(), "shared.txt", "feature content\n", "feature commit");

    // Switch back to main and modify the file differently
    Command::new("git")
        .args(["checkout", "-"])
        .current_dir(dir.path())
        .output()
        .unwrap();
    commit_file(dir.path(), "shared.txt", "main modified content\n", "main commit");

    // Merge should produce conflict
    let merge_result = provider
        .merge_branch(dir.path(), "feature")
        .expect("merge should be attempted");

    assert!(matches!(merge_result, MergeResult::Conflict { .. }));

    // Resolve using AcceptBoth (keep both versions)
    provider
        .resolve_conflict(dir.path(), "shared.txt", ConflictResolution::AcceptBoth)
        .expect("resolve should succeed");

    // Verify file contains both versions (markers removed)
    let content = fs::read_to_string(dir.path().join("shared.txt")).unwrap();
    assert!(content.contains("main modified content"));
    assert!(content.contains("feature content"));
    assert!(!content.contains("<<<<<<< ")); // No conflict markers
    assert!(!content.contains("======= ")); // No conflict markers

    // Continue the merge
    provider
        .merge_continue(dir.path(), "Merge feature into main")
        .expect("merge continue should succeed");

    // Verify merge completed
    assert!(!dir.path().join(".git").join("MERGE_HEAD").exists());
}

/// Test AcceptCurrent with revert (inverse: keeps incoming = previous version)
#[test]
fn revert_conflict_accept_current_strategy() {
    let dir = make_temp_repo();
    let log = CommandLog::new(50);
    let provider = GitProvider::new(log);

    // Create commit with some content
    commit_file(dir.path(), "file.txt", "original content", "original commit");

    // Modify and commit
    commit_file(dir.path(), "file.txt", "modified content", "modification commit");

    // Make another modification that will conflict with revert
    commit_file(dir.path(), "file.txt", "further modification", "another commit");

    // Get the hash of the middle commit (the one we want to revert)
    let commits_output = Command::new("git")
        .args(["rev-list", "HEAD"])
        .current_dir(dir.path())
        .output()
        .unwrap();
    let commit_stdout = String::from_utf8_lossy(&commits_output.stdout);
    let commit_hashes: Vec<&str> = commit_stdout
        .trim()
        .lines()
        .collect();
    let revert_hash = commit_hashes[1].to_string(); // The "modification commit"

    // Revert should produce conflict (since we modified the file again after)
    let revert_result = provider
        .revert_commit(dir.path(), &revert_hash)
        .expect("revert should be attempted");

    assert!(matches!(revert_result, RevertResult::Conflict { .. }));

    // Resolve using AcceptCurrent (keep the current working tree version)
    provider
        .resolve_conflict(dir.path(), "file.txt", ConflictResolution::AcceptCurrent)
        .expect("resolve should succeed");

    // Verify file content is preserved (kept the "further modification")
    let content = fs::read_to_string(dir.path().join("file.txt")).unwrap();
    assert_eq!(content, "further modification");

    // Continue the revert
    provider
        .revert_continue(dir.path())
        .expect("revert continue should succeed");

    // Verify revert completed
    assert!(!dir.path().join(".git").join("REVERT_HEAD").exists());
}

/// Test AcceptIncoming with revert (inverse: keeps previous commit version)
#[test]
fn revert_conflict_accept_incoming_strategy() {
    let dir = make_temp_repo();
    let log = CommandLog::new(50);
    let provider = GitProvider::new(log);

    // Create commit with some content
    commit_file(dir.path(), "file.txt", "original content", "original commit");

    // Modify and commit
    commit_file(dir.path(), "file.txt", "modified content", "modification commit");

    // Make another modification that will conflict with revert
    commit_file(dir.path(), "file.txt", "further modification", "another commit");

    // Get the hash of the middle commit (the one we want to revert)
    let commits_output = Command::new("git")
        .args(["rev-list", "HEAD"])
        .current_dir(dir.path())
        .output()
        .unwrap();
    let commit_stdout = String::from_utf8_lossy(&commits_output.stdout);
    let commit_hashes: Vec<&str> = commit_stdout
        .trim()
        .lines()
        .collect();
    let revert_hash = commit_hashes[1].to_string(); // The "modification commit"

    // Revert should produce conflict
    let revert_result = provider
        .revert_commit(dir.path(), &revert_hash)
        .expect("revert should be attempted");

    assert!(matches!(revert_result, RevertResult::Conflict { .. }));

    // Resolve using AcceptIncoming (keep the previous version = undo the revert)
    provider
        .resolve_conflict(dir.path(), "file.txt", ConflictResolution::AcceptIncoming)
        .expect("resolve should succeed");

    // Verify file has the version BEFORE the modification being reverted
    let content = fs::read_to_string(dir.path().join("file.txt")).unwrap();
    assert_eq!(content, "original content");

    // Continue the revert
    provider
        .revert_continue(dir.path())
        .expect("revert continue should succeed");

    // Verify revert completed
    assert!(!dir.path().join(".git").join("REVERT_HEAD").exists());
}

/// Test AcceptBoth with revert
#[test]
fn revert_conflict_accept_both_strategy() {
    let dir = make_temp_repo();
    let log = CommandLog::new(50);
    let provider = GitProvider::new(log);

    // Create commit with some content
    commit_file(dir.path(), "file.txt", "original content\n", "original commit");

    // Modify and commit
    commit_file(dir.path(), "file.txt", "modified content\n", "modification commit");

    // Make another modification that will conflict with revert
    commit_file(dir.path(), "file.txt", "further modification\n", "another commit");

    // Get the hash of the middle commit (the one we want to revert)
    let commits_output = Command::new("git")
        .args(["rev-list", "HEAD"])
        .current_dir(dir.path())
        .output()
        .unwrap();
    let commit_stdout = String::from_utf8_lossy(&commits_output.stdout);
    let commit_hashes: Vec<&str> = commit_stdout
        .trim()
        .lines()
        .collect();
    let revert_hash = commit_hashes[1].to_string(); // The "modification commit"

    // Revert should produce conflict
    let revert_result = provider
        .revert_commit(dir.path(), &revert_hash)
        .expect("revert should be attempted");

    assert!(matches!(revert_result, RevertResult::Conflict { .. }));

    // Resolve using AcceptBoth (keep both versions)
    provider
        .resolve_conflict(dir.path(), "file.txt", ConflictResolution::AcceptBoth)
        .expect("resolve should succeed");

    // Verify file has both versions (conflict markers removed)
    let content = fs::read_to_string(dir.path().join("file.txt")).unwrap();
    assert!(content.contains("original content"));
    assert!(content.contains("further modification"));
    assert!(!content.contains("<<<<<<< ")); // No conflict markers

    // Continue the revert
    provider
        .revert_continue(dir.path())
        .expect("revert continue should succeed");

    // Verify revert completed
    assert!(!dir.path().join(".git").join("REVERT_HEAD").exists());
}

/// Test multiple conflicting files with different strategies
#[test]
fn merge_multiple_files_different_strategies() {
    let dir = make_temp_repo();
    let log = CommandLog::new(50);
    let provider = GitProvider::new(log);

    // Create base commits with multiple files
    commit_file(dir.path(), "file1.txt", "main1", "initial");
    commit_file(dir.path(), "file2.txt", "main2", "initial");

    // Create feature branch and modify both files
    Command::new("git")
        .args(["checkout", "-b", "feature"])
        .current_dir(dir.path())
        .output()
        .unwrap();
    commit_file(dir.path(), "file1.txt", "feature1", "feature");
    commit_file(dir.path(), "file2.txt", "feature2", "feature");

    // Switch back to main and modify both files differently
    Command::new("git")
        .args(["checkout", "-"])
        .current_dir(dir.path())
        .output()
        .unwrap();
    commit_file(dir.path(), "file1.txt", "main1-modified", "main");
    commit_file(dir.path(), "file2.txt", "main2-modified", "main");

    // Merge should produce conflicts
    let merge_result = provider
        .merge_branch(dir.path(), "feature")
        .expect("merge should be attempted");

    assert!(matches!(merge_result, MergeResult::Conflict { .. }));

    // Resolve first file with AcceptCurrent
    provider
        .resolve_conflict(dir.path(), "file1.txt", ConflictResolution::AcceptCurrent)
        .expect("resolve should succeed");

    // Resolve second file with AcceptIncoming
    provider
        .resolve_conflict(dir.path(), "file2.txt", ConflictResolution::AcceptIncoming)
        .expect("resolve should succeed");

    // Continue merge
    provider
        .merge_continue(dir.path(), "Merge with mixed strategies")
        .expect("merge continue should succeed");

    // Verify both resolutions applied correctly
    assert_eq!(
        fs::read_to_string(dir.path().join("file1.txt")).unwrap(),
        "main1-modified"
    );
    assert_eq!(
        fs::read_to_string(dir.path().join("file2.txt")).unwrap(),
        "feature2"
    );

    // Verify merge completed
    assert!(!dir.path().join(".git").join("MERGE_HEAD").exists());
}

/// Test external conflict resolution (file resolved outside of VCS)
#[test]
fn merge_conflict_externally_resolved() {
    let dir = make_temp_repo();
    let log = CommandLog::new(50);
    let provider = GitProvider::new(log);

    // Create conflicting scenario
    commit_file(dir.path(), "file.txt", "main content", "initial");
    Command::new("git")
        .args(["checkout", "-b", "feature"])
        .current_dir(dir.path())
        .output()
        .unwrap();
    commit_file(dir.path(), "file.txt", "feature content", "feature");

    Command::new("git")
        .args(["checkout", "-"])
        .current_dir(dir.path())
        .output()
        .unwrap();
    commit_file(dir.path(), "file.txt", "main modified", "main");

    // Trigger merge
    let merge_result = provider
        .merge_branch(dir.path(), "feature")
        .expect("merge should be attempted");

    assert!(matches!(merge_result, MergeResult::Conflict { .. }));

    // Simulate external resolution by writing content and staging manually
    fs::write(dir.path().join("file.txt"), "externally resolved content").unwrap();
    Command::new("git")
        .args(["add", "file.txt"])
        .current_dir(dir.path())
        .output()
        .unwrap();

    // Verify conflict_counts reflects the external resolution
    let conflict_counts = provider
        .conflict_counts(dir.path())
        .expect("should get conflict counts");

    // After adding, the file should have 0 conflict markers
    let counts = conflict_counts
        .iter()
        .find(|c| c.path == "file.txt")
        .map(|c| c.conflict_count)
        .unwrap_or(0);
    assert_eq!(counts, 0);

    // Continue merge should succeed
    provider
        .merge_continue(dir.path(), "Merge with external resolution")
        .expect("merge continue should succeed");

    assert!(!dir.path().join(".git").join("MERGE_HEAD").exists());
}
