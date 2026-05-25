use std::path::Path;
use std::process::Command;
use std::sync::Mutex;
use std::thread;
use std::time::{Duration, Instant};

use tracing::{debug, warn};

use crate::command_log::CommandLog;
use crate::error::AppError;

/// Cached SSH passphrase. When set, git remote operations will use SSH_ASKPASS
/// with setsid to feed this passphrase to SSH, bypassing agents/pinentry.
static SSH_PASSPHRASE: Mutex<Option<String>> = Mutex::new(None);

/// Store (or clear) the SSH passphrase used for remote operations.
pub fn set_ssh_passphrase(passphrase: Option<String>) {
    *SSH_PASSPHRASE.lock().unwrap() = passphrase;
}

/// Result of running a git CLI command.
pub struct GitOutput {
    pub exit_code: i32,
    pub stdout: String,
    pub stderr: String,
}

/// Execute a git command in the given working directory and record it in the
/// command log.
pub fn run_git(cwd: &Path, args: &[&str], log: &CommandLog) -> Result<GitOutput, AppError> {
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
    const MAX_RETRIES: u32 = 3;
    const RETRY_DELAY_MS: u64 = 150;

    let cmd_string = format!("git {}", args.join(" "));
    debug!(cmd = %cmd_string, cwd = %cwd.display(), "running git command");

    let start = Instant::now();

    // Check if we have a cached passphrase for SSH
    let passphrase = SSH_PASSPHRASE.lock().unwrap().clone();

    let mut last_output = None;

    for attempt in 0..=MAX_RETRIES {
        let output = if let Some(ref pp) = passphrase {
            run_git_with_askpass(cwd, args, pp)?
        } else {
            run_git_batch_mode(cwd, args)?
        };

        let stderr = String::from_utf8_lossy(&output.stderr);
        let is_lock_error = !output.status.success()
            && stderr.contains("index.lock")
            && stderr.contains("File exists");

        if !is_lock_error || attempt == MAX_RETRIES {
            last_output = Some(output);
            break;
        }

        // Lock contention — wait briefly and retry.
        warn!(
            cmd = %cmd_string,
            attempt = attempt + 1,
            "index.lock contention, retrying"
        );
        thread::sleep(Duration::from_millis(RETRY_DELAY_MS * (attempt as u64 + 1)));
    }

    let output = last_output.unwrap();
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

/// Run git with BatchMode=yes (fast-fail if SSH auth needs interaction).
fn run_git_batch_mode(cwd: &Path, args: &[&str]) -> Result<std::process::Output, AppError> {
    Command::new("git")
        .args(args)
        .current_dir(cwd)
        .stdin(std::process::Stdio::null())
        .env("GIT_TERMINAL_PROMPT", "0")
        .env("GIT_SSH_COMMAND", "ssh -o BatchMode=yes")
        .env("SSH_ASKPASS", "")
        .output()
        .map_err(|e| AppError::Io(format!("failed to spawn git: {e}")))
}

/// Run git with SSH_ASKPASS providing the passphrase. Uses setsid so SSH
/// (spawned by git) has no controlling terminal and is forced to use askpass.
///
/// Security: The askpass script containing the passphrase is stored in a memfd
/// (anonymous memory-backed fd) and never written to any filesystem. It is only
/// accessible via /proc/PID/fd/N by our process and its children.
#[cfg(unix)]
fn run_git_with_askpass(
    cwd: &Path,
    args: &[&str],
    passphrase: &str,
) -> Result<std::process::Output, AppError> {
    // Build the askpass script content (single-quote escaping for shell)
    let escaped = passphrase.replace('\'', "'\\''");
    let script_content = format!("#!/bin/sh\nprintf '%s\\n' '{}'\n", escaped);

    // Try memfd_create first (never touches disk), fall back to tmpfs
    let askpass_path = match create_memfd_askpass(&script_content) {
        Ok((path, fd)) => {
            // Keep fd alive for the duration of this scope
            let _keep_alive = fd;
            return run_git_with_askpass_path(cwd, args, &path);
        }
        Err(_) => {
            // Fall back to tmpfs file
            create_tmpfs_askpass(&script_content)?
        }
    };

    let result = run_git_with_askpass_path(cwd, args, &askpass_path);

    // Clean up tmpfs fallback immediately
    let _ = std::fs::remove_file(&askpass_path);

    result
}

/// Create the askpass script as a memfd (anonymous, never on any filesystem).
/// Returns the /proc path and an OwnedFd that must be kept alive.
#[cfg(unix)]
fn create_memfd_askpass(
    content: &str,
) -> Result<(String, std::os::fd::OwnedFd), AppError> {
    use std::io::Write;
    use std::os::fd::{AsRawFd, FromRawFd, OwnedFd};

    let name = b"cheesegit-askpass\0";
    // MFD_CLOEXEC is NOT set — we need children to access via /proc/PID/fd/N
    let fd = unsafe { libc::memfd_create(name.as_ptr() as *const _, 0) };
    if fd < 0 {
        return Err(AppError::Io("memfd_create failed".to_string()));
    }
    let owned = unsafe { OwnedFd::from_raw_fd(fd) };

    // Write the script content
    let mut file = std::fs::File::from(owned);
    file.write_all(content.as_bytes())
        .map_err(|e| AppError::Io(format!("memfd write failed: {e}")))?;

    // Use /proc/PID/fd/N so any child process can access it via our procfs entry
    let raw_fd = file.as_raw_fd();
    let path = format!("/proc/{}/fd/{}", std::process::id(), raw_fd);

    // Convert back to OwnedFd to keep it alive
    let owned: OwnedFd = file.into();

    Ok((path, owned))
}

/// Fallback: create the askpass script in XDG_RUNTIME_DIR (tmpfs, RAM-only).
#[cfg(unix)]
fn create_tmpfs_askpass(content: &str) -> Result<String, AppError> {
    use std::os::unix::fs::PermissionsExt;

    let base_dir = std::env::var("XDG_RUNTIME_DIR")
        .unwrap_or_else(|_| std::env::temp_dir().to_string_lossy().into_owned());
    let script_path = format!("{}/cheesegit-askpass-{}", base_dir, std::process::id());

    std::fs::write(&script_path, content)
        .map_err(|e| AppError::Io(format!("failed to write askpass script: {e}")))?;
    std::fs::set_permissions(&script_path, std::fs::Permissions::from_mode(0o700))
        .map_err(|e| AppError::Io(format!("failed to set script permissions: {e}")))?;

    Ok(script_path)
}

#[cfg(unix)]
fn run_git_with_askpass_path(
    cwd: &Path,
    args: &[&str],
    askpass_path: &str,
) -> Result<std::process::Output, AppError> {
    use std::os::unix::process::CommandExt;

    let display = std::env::var("DISPLAY").unwrap_or_else(|_| ":0".to_string());

    let mut cmd = Command::new("git");
    cmd.args(args)
        .current_dir(cwd)
        .stdin(std::process::Stdio::null())
        .env("GIT_TERMINAL_PROMPT", "0")
        .env("SSH_ASKPASS", askpass_path)
        .env("SSH_ASKPASS_REQUIRE", "force")
        .env("DISPLAY", &display);

    // SAFETY: setsid() is async-signal-safe. Detaches from the controlling
    // terminal so SSH (spawned by git) is forced to use SSH_ASKPASS.
    unsafe {
        cmd.pre_exec(|| {
            libc::setsid();
            Ok(())
        });
    }

    cmd.output()
        .map_err(|e| AppError::Io(format!("failed to spawn git: {e}")))
}

#[cfg(not(unix))]
fn run_git_with_askpass(
    cwd: &Path,
    args: &[&str],
    _passphrase: &str,
) -> Result<std::process::Output, AppError> {
    // Fallback: just run in batch mode on non-unix
    run_git_batch_mode(cwd, args)
}
