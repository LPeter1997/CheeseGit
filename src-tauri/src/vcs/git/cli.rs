use std::path::Path;
use std::process::Command;

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
    let cmd_string = format!("git {}", args.join(" "));
    debug!(cmd = %cmd_string, cwd = %cwd.display(), "running git command");

    let output = Command::new("git")
        .args(args)
        .current_dir(cwd)
        .output()
        .map_err(|e| AppError::Io(format!("failed to spawn git: {e}")))?;

    let exit_code = output.status.code().unwrap_or(-1);
    let stdout = String::from_utf8_lossy(&output.stdout).into_owned();
    let stderr = String::from_utf8_lossy(&output.stderr).into_owned();

    log.record(&cmd_string, &cwd.display().to_string(), exit_code, &stdout, &stderr);

    Ok(GitOutput {
        exit_code,
        stdout,
        stderr,
    })
}
