use std::path::Path;
use std::process::Command;
use std::time::Instant;

use tracing::debug;

use crate::command_log::CommandLog;
use crate::error::AppError;

/// Result of running a git CLI command.
pub struct GitOutput {
    pub exit_code: i32,
    pub stdout: String,
    pub stderr: String,
}

/// Execute a git command in the given working directory and record it in the
/// command log.
pub fn run_git(
    cwd: &Path,
    args: &[&str],
    log: &CommandLog,
) -> Result<GitOutput, AppError> {
    run_git_opts(cwd, args, log, false)
}

/// Execute a git command marked as a background/periodic operation.
/// These commands can be filtered out in the command log UI.
pub fn run_git_background(
    cwd: &Path,
    args: &[&str],
    log: &CommandLog,
) -> Result<GitOutput, AppError> {
    run_git_opts(cwd, args, log, true)
}

fn run_git_opts(
    cwd: &Path,
    args: &[&str],
    log: &CommandLog,
    is_background: bool,
) -> Result<GitOutput, AppError> {
    let cmd_string = format!("git {}", args.join(" "));
    debug!(cmd = %cmd_string, cwd = %cwd.display(), "running git command");

    let start = Instant::now();

    let output = Command::new("git")
        .args(args)
        .current_dir(cwd)
        .output()
        .map_err(|e| AppError::Io(format!("failed to spawn git: {e}")))?;

    let elapsed_ms = start.elapsed().as_millis() as u32;

    let exit_code = output.status.code().unwrap_or(-1);
    let stdout = String::from_utf8_lossy(&output.stdout).into_owned();
    let stderr = String::from_utf8_lossy(&output.stderr).into_owned();

    log.record(
        &cmd_string,
        &cwd.display().to_string(),
        exit_code,
        &stdout,
        &stderr,
        elapsed_ms,
        is_background,
    );

    Ok(GitOutput {
        exit_code,
        stdout,
        stderr,
    })
}
