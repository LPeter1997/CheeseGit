use cheesegit_lib::command_log::CommandLog;
use cheesegit_lib::vcs::git::GitProvider;
use cheesegit_lib::vcs::traits::VcsProvider;

mod test_utils;
use test_utils::{commit_file, current_branch, git_ok, init_bare_repo, make_temp_repo_with_commit};

#[test]
fn list_remote_branches_returns_only_remote_only() {
    // Set up a bare "remote" repo with some branches.
    let bare = init_bare_repo();

    // Clone it as a working repo and push a commit.
    let local = make_temp_repo_with_commit();
    let default_branch = current_branch(local.path());
    git_ok(
        local.path(),
        &["remote", "add", "origin", bare.path().to_str().unwrap()],
    );
    git_ok(local.path(), &["push", "-u", "origin", "HEAD"]);

    // Create a local branch that also exists on the remote.
    git_ok(local.path(), &["checkout", "-b", "feature/local"]);
    commit_file(
        local.path(),
        "feature.txt",
        "feature content",
        "feat: add feature file",
    );
    git_ok(local.path(), &["push", "-u", "origin", "feature/local"]);

    // Go back to the default branch.
    git_ok(local.path(), &["checkout", &default_branch]);

    // Create remote-only branches in the bare repo.
    let main_hash = String::from_utf8(
        git_ok(bare.path(), &["rev-parse", &default_branch]).stdout,
    )
    .unwrap()
    .trim()
    .to_string();
    git_ok(
        bare.path(),
        &["branch", "feature/remote-only-a", &main_hash],
    );
    git_ok(
        bare.path(),
        &["branch", "feature/remote-only-b", &main_hash],
    );

    // Fetch so local knows about the remote branches.
    git_ok(local.path(), &["fetch", "origin"]);

    let log = CommandLog::new(50);
    let provider = GitProvider::new(log);

    let remote_branches = provider
        .list_remote_branches(local.path())
        .expect("should succeed");

    let names: Vec<&str> = remote_branches.iter().map(|b| b.name.as_str()).collect();

    // Should contain the remote-only branches.
    assert!(
        names.contains(&"feature/remote-only-a"),
        "expected feature/remote-only-a in {:?}",
        names
    );
    assert!(
        names.contains(&"feature/remote-only-b"),
        "expected feature/remote-only-b in {:?}",
        names
    );

    // Should NOT contain branches that exist locally.
    assert!(
        !names.contains(&default_branch.as_str()),
        "default branch should be excluded (exists locally)"
    );
    assert!(
        !names.contains(&"feature/local"),
        "feature/local should be excluded (exists locally)"
    );

    // All should have remote = "origin".
    for b in &remote_branches {
        assert_eq!(b.remote, "origin");
        assert!(!b.last_commit_date.is_empty());
    }
}

#[test]
fn list_remote_branches_empty_when_no_remotes() {
    let local = make_temp_repo_with_commit();

    let log = CommandLog::new(50);
    let provider = GitProvider::new(log);

    let remote_branches = provider
        .list_remote_branches(local.path())
        .expect("should succeed");

    assert!(remote_branches.is_empty());
}

#[test]
fn switch_to_remote_branch_creates_local_tracking() {
    let bare = init_bare_repo();
    let local = make_temp_repo_with_commit();
    let default_branch = current_branch(local.path());
    git_ok(
        local.path(),
        &["remote", "add", "origin", bare.path().to_str().unwrap()],
    );
    git_ok(local.path(), &["push", "-u", "origin", "HEAD"]);

    // Create a remote-only branch.
    let main_hash = String::from_utf8(
        git_ok(bare.path(), &["rev-parse", &default_branch]).stdout,
    )
    .unwrap()
    .trim()
    .to_string();
    git_ok(
        bare.path(),
        &["branch", "feature/remote-checkout", &main_hash],
    );
    git_ok(local.path(), &["fetch", "origin"]);

    let log = CommandLog::new(50);
    let provider = GitProvider::new(log);

    // Verify it appears in remote branches.
    let remote_branches = provider
        .list_remote_branches(local.path())
        .expect("should list remote branches");
    let names: Vec<&str> = remote_branches.iter().map(|b| b.name.as_str()).collect();
    assert!(names.contains(&"feature/remote-checkout"));

    // Switch to the remote branch (git switch creates a local tracking branch).
    provider
        .switch_branch(local.path(), "feature/remote-checkout")
        .expect("switch should succeed");

    // Now it should be the current branch.
    let branches = provider
        .list_branches(local.path())
        .expect("should list branches");
    let current = branches.iter().find(|b| b.is_current);
    assert_eq!(
        current.map(|b| b.name.as_str()),
        Some("feature/remote-checkout")
    );

    // And it should no longer appear in remote-only branches.
    let remote_branches = provider
        .list_remote_branches(local.path())
        .expect("should list remote branches after checkout");
    let names: Vec<&str> = remote_branches.iter().map(|b| b.name.as_str()).collect();
    assert!(
        !names.contains(&"feature/remote-checkout"),
        "should no longer be remote-only after checkout"
    );
}
