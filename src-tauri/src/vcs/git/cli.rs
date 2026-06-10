use std::path::Path;
use std::process::Command;
use std::sync::Mutex;
use std::thread;
use std::time::{Duration, Instant};

use tracing::{debug, warn};

use crate::command_log::CommandLog;
use crate::error::AppError;

/// Cached SSH passphrase. When set, git remote operations will use SSH_ASKPASS
/// to feed this passphrase to SSH, bypassing agents/pinentry. On Linux this
/// uses setsid + a memfd-backed script; on Windows it uses a temp shell script
/// run by Git-for-Windows' bundled ssh.
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

    if let Err(e) = log.record(
        &cmd_string,
        &cwd.display().to_string(),
        exit_code,
        &stdout,
        &stderr,
        elapsed_ms,
        is_background,
    ) {
        // Log recording failed, but continue execution
        warn!(error = %e, "failed to record git command in log");
    }

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
#[cfg(all(unix, not(target_os = "macos")))]
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
#[cfg(all(unix, not(target_os = "macos")))]
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
#[cfg(all(unix, not(target_os = "macos")))]
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

#[cfg(all(unix, not(target_os = "macos")))]
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

#[cfg(any(not(unix), target_os = "macos"))]
fn run_git_with_askpass(
    cwd: &Path,
    args: &[&str],
    passphrase: &str,
) -> Result<std::process::Output, AppError> {
    #[cfg(windows)]
    {
        return run_git_with_askpass_windows(cwd, args, passphrase);
    }
    #[cfg(not(windows))]
    {
        // Fallback: just run in batch mode on other non-unix targets (e.g. macOS).
        let _ = passphrase;
        run_git_batch_mode(cwd, args)
    }
}

/// Run git with an SSH_ASKPASS script supplying the passphrase on Windows.
///
/// Git for Windows bundles its own MSYS2 `ssh`, which honours a `#!/bin/sh`
/// shebang and runs the askpass script via its bundled `sh`. Setting
/// `SSH_ASKPASS_REQUIRE=force` makes ssh use the askpass program even though a
/// GUI app has no controlling terminal, so there is no interactive prompt to
/// fall back to.
///
/// IMPORTANT: We must force git to use Git-for-Windows' bundled MSYS `ssh.exe`
/// via `GIT_SSH_COMMAND`. Otherwise git may pick Windows' native OpenSSH
/// (`C:\Windows\System32\OpenSSH\ssh.exe`), which cannot resolve an MSYS-style
/// path (`/c/...`) nor run a `#!/bin/sh` askpass script — it fails with
/// "ssh_askpass: exec(...): No such file or directory" and falls back to
/// public-key-only auth (Permission denied).
///
/// The MSYS `ssh` exec's the askpass program through the Cygwin/MSYS runtime,
/// which only understands POSIX-style paths, so the `SSH_ASKPASS` value is
/// converted to MSYS form (`/c/Users/.../x.sh`).
///
/// Security: the script (containing the passphrase) is written to a uniquely
/// named file in the temp directory and deleted immediately after the git
/// command returns.
#[cfg(windows)]
fn run_git_with_askpass_windows(
    cwd: &Path,
    args: &[&str],
    passphrase: &str,
) -> Result<std::process::Output, AppError> {
    use std::io::Write;

    // Build the askpass script content (single-quote escaping for shell).
    // Cygwin/MSYS treats any file beginning with `#!` as executable, so the
    // script does not need an explicit Windows execute bit.
    let escaped = passphrase.replace('\'', "'\\''");
    let script_content = format!("#!/bin/sh\nprintf '%s\\n' '{}'\n", escaped);

    // Unique per-process temp script path (native Windows path for std::fs).
    let mut script_path = std::env::temp_dir();
    script_path.push(format!("cheesegit-askpass-{}.sh", std::process::id()));

    {
        let mut file = std::fs::File::create(&script_path)
            .map_err(|e| AppError::Io(format!("failed to write askpass script: {e}")))?;
        file.write_all(script_content.as_bytes())
            .map_err(|e| AppError::Io(format!("failed to write askpass script: {e}")))?;
    }

    // ssh (MSYS) needs a POSIX-style path, not a Windows path.
    let msys_path = to_msys_path(&script_path);

    let mut cmd = Command::new("git");
    cmd.args(args)
        .current_dir(cwd)
        .stdin(std::process::Stdio::null())
        .env("GIT_TERMINAL_PROMPT", "0")
        .env("SSH_ASKPASS", &msys_path)
        .env("SSH_ASKPASS_REQUIRE", "force")
        // DISPLAY is required by some older ssh builds to consider askpass.
        .env("DISPLAY", "localhost:0");

    // Force Git-for-Windows' bundled MSYS ssh so the MSYS path + shell-script
    // askpass actually work (Windows' native OpenSSH cannot use either).
    if let Some(ssh) = find_git_bundled_ssh() {
        // GIT_SSH_COMMAND is parsed with shell quoting; use forward slashes and
        // quote to tolerate spaces (e.g. "C:/Program Files/Git/...").
        let ssh_fwd = ssh.to_string_lossy().replace('\\', "/");
        cmd.env("GIT_SSH_COMMAND", format!("\"{ssh_fwd}\""));
    }

    let result = cmd
        .output()
        .map_err(|e| AppError::Io(format!("failed to spawn git: {e}")));

    // Remove the script regardless of the git outcome.
    let _ = std::fs::remove_file(&script_path);

    result
}

/// Locate Git-for-Windows' bundled MSYS `ssh.exe`.
///
/// Tries to derive it from `git --exec-path` (which lives under the Git install
/// tree), then falls back to common install locations. Cached after the first
/// lookup.
#[cfg(windows)]
fn find_git_bundled_ssh() -> Option<std::path::PathBuf> {
    use std::path::PathBuf;
    use std::sync::OnceLock;

    static CACHE: OnceLock<Option<PathBuf>> = OnceLock::new();

    CACHE
        .get_or_init(|| {
            // `git --exec-path` -> e.g. C:/Program Files/Git/mingw64/libexec/git-core
            // The bundled ssh lives at <git_root>/usr/bin/ssh.exe.
            if let Ok(out) = Command::new("git").arg("--exec-path").output() {
                if out.status.success() {
                    let raw = String::from_utf8_lossy(&out.stdout);
                    let exec_path = PathBuf::from(raw.trim().replace('/', "\\"));
                    // Ascend until we find a sibling usr/bin/ssh.exe.
                    for ancestor in exec_path.ancestors() {
                        let cand = ancestor.join("usr").join("bin").join("ssh.exe");
                        if cand.exists() {
                            return Some(cand);
                        }
                    }
                }
            }

            // Fall back to common install locations.
            for base in [
                r"C:\Program Files\Git",
                r"C:\Program Files (x86)\Git",
            ] {
                let cand = Path::new(base).join("usr").join("bin").join("ssh.exe");
                if cand.exists() {
                    return Some(cand);
                }
            }

            None
        })
        .clone()
}

/// Convert a native Windows path into an MSYS/Cygwin POSIX path.
///
/// `C:\Users\me\Temp\x.sh` -> `/c/Users/me/Temp/x.sh`
///
/// Git for Windows mounts drives at `/<drive letter>/` by default, which is
/// what its bundled `ssh`/`sh` expect when exec'ing a program.
#[cfg(windows)]
fn to_msys_path(path: &Path) -> String {
    let s = path.to_string_lossy();
    let bytes = s.as_bytes();
    if s.len() >= 2 && bytes[1] == b':' && (bytes[0] as char).is_ascii_alphabetic() {
        let drive = (bytes[0] as char).to_ascii_lowercase();
        let rest = s[2..].replace('\\', "/");
        format!("/{drive}{rest}")
    } else {
        s.replace('\\', "/")
    }
}
