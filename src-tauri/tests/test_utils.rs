#![allow(dead_code)]

use std::path::Path;
use std::process::{Command, Output};

use tempfile::TempDir;

pub fn git(path: &Path, args: &[&str]) -> Output {
    Command::new("git")
        .args(args)
        .current_dir(path)
        .output()
        .expect("failed to run git command")
}

pub fn git_ok(path: &Path, args: &[&str]) -> Output {
    let output = git(path, args);
    assert!(
        output.status.success(),
        "git {:?} failed:\nstdout: {}\nstderr: {}",
        args,
        String::from_utf8_lossy(&output.stdout),
        String::from_utf8_lossy(&output.stderr)
    );
    output
}

pub fn configure_test_user(path: &Path, email: &str, name: &str) {
    git_ok(path, &["config", "user.email", email]);
    git_ok(path, &["config", "user.name", name]);
    git_ok(path, &["config", "commit.gpgsign", "false"]);
    git_ok(path, &["config", "tag.gpgSign", "false"]);
}

pub fn make_temp_repo() -> TempDir {
    let dir = TempDir::new().expect("failed to create temp dir");
    git_ok(dir.path(), &["init"]);
    configure_test_user(dir.path(), "test@test.com", "Test User");
    dir
}

pub fn make_temp_repo_with_commit() -> TempDir {
    let dir = make_temp_repo();
    let path = dir.path();

    std::fs::write(path.join("hello.txt"), "line1\nline2\nline3\nline4\n")
        .expect("failed to write initial test file");
    git_ok(path, &["add", "."]);
    git_ok(path, &["commit", "-m", "initial commit"]);

    dir
}

pub fn commit_file(path: &Path, filename: &str, content: &str, message: &str) -> String {
    std::fs::write(path.join(filename), content).expect("failed to write test file");
    git_ok(path, &["add", filename]);
    git_ok(path, &["commit", "-m", message]);

    let output = git_ok(path, &["rev-parse", "HEAD"]);
    String::from_utf8(output.stdout)
        .expect("rev-parse output was not UTF-8")
        .trim()
        .to_string()
}

pub fn current_branch(path: &Path) -> String {
    let output = git_ok(path, &["rev-parse", "--abbrev-ref", "HEAD"]);
    String::from_utf8(output.stdout)
        .expect("branch output was not UTF-8")
        .trim()
        .to_string()
}

pub fn init_bare_repo() -> TempDir {
    let dir = TempDir::new().expect("failed to create bare temp dir");
    git_ok(dir.path(), &["init", "--bare"]);
    dir
}

pub fn clone_to_temp(remote_path: &Path) -> TempDir {
    let dir = TempDir::new().expect("failed to create clone temp dir");
    let remote = remote_path
        .to_str()
        .expect("remote path should be valid UTF-8 in tests");
    let local = dir
        .path()
        .to_str()
        .expect("local path should be valid UTF-8 in tests");
    git_ok(std::path::Path::new("."), &["clone", remote, local]);
    dir
}

pub fn commit_all(path: &Path, message: &str) {
    git_ok(path, &["add", "."]);
    git_ok(path, &["commit", "-m", message]);
}
