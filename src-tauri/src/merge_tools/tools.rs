use std::path::Path;
use std::process::Command;
use std::time::Instant;

use serde::{Deserialize, Serialize};
use specta::Type;

use crate::command_log::CommandLog;
use crate::error::AppError;
use super::MergeTool;

/// Serializable merge tool info sent to the frontend.
#[derive(Debug, Clone, Serialize, Deserialize, Type)]
pub struct MergeToolInfo {
    /// Machine-readable identifier.
    pub id: String,
    /// Human-readable display name.
    pub display_name: String,
    /// Optional icon filename (e.g. "vscode.svg") served from `/icons/merge-tools/`.
    pub icon: Option<String>,
}

// ---------------------------------------------------------------------------
// Helper: check whether an executable exists on PATH
// ---------------------------------------------------------------------------

fn command_exists(name: &str) -> bool {
    #[cfg(target_os = "windows")]
    {
        Command::new("where")
            .arg(name)
            .stdout(std::process::Stdio::null())
            .stderr(std::process::Stdio::null())
            .status()
            .map(|s| s.success())
            .unwrap_or(false)
    }
    #[cfg(not(target_os = "windows"))]
    {
        Command::new("which")
            .arg(name)
            .stdout(std::process::Stdio::null())
            .stderr(std::process::Stdio::null())
            .status()
            .map(|s| s.success())
            .unwrap_or(false)
    }
}

// ---------------------------------------------------------------------------
// Helper: spawn an external tool (fire-and-forget) and record the invocation
// in the command log so failures (e.g. "program not found") are debuggable.
// ---------------------------------------------------------------------------

fn spawn_and_log(
    program: &str,
    args: &[&str],
    cwd: &Path,
    tool_name: &str,
    log: &CommandLog,
) -> Result<(), AppError> {
    let command_str = if args.is_empty() {
        program.to_string()
    } else {
        format!("{program} {}", args.join(" "))
    };
    let cwd_str = cwd.display().to_string();
    let start = Instant::now();
    let result = Command::new(program).args(args).current_dir(cwd).spawn();
    let elapsed_ms = start.elapsed().as_millis() as u32;
    match result {
        Ok(child) => {
            // Don't wait — let the user work in the external tool.
            drop(child);
            let _ = log.record(&command_str, &cwd_str, 0, "", "", elapsed_ms, false);
            Ok(())
        }
        Err(e) => {
            let msg = format!("Failed to open {tool_name}: {e}");
            let _ = log.record(&command_str, &cwd_str, -1, "", &msg, elapsed_ms, false);
            Err(AppError::Io(msg))
        }
    }
}

// ---------------------------------------------------------------------------
// Visual Studio Code
// ---------------------------------------------------------------------------

pub struct VsCodeMergeTool;

impl MergeTool for VsCodeMergeTool {
    fn id(&self) -> &str { "vscode" }
    fn display_name(&self) -> &str { "Visual Studio Code" }
    fn icon(&self) -> Option<&str> { Some("vscode.svg") }

    fn is_available(&self) -> bool {
        command_exists("code")
    }

    fn open_conflicted_file(&self, repo_path: &Path, file_path: &str, log: &CommandLog) -> Result<(), AppError> {
        let full_path = repo_path.join(file_path);
        let full = full_path.to_string_lossy();
        spawn_and_log(
            "code",
            &["--wait", "--merge", &full, &full, &full, &full],
            repo_path,
            "Visual Studio Code",
            log,
        )
    }
}

// ---------------------------------------------------------------------------
// NeoVim
// ---------------------------------------------------------------------------

pub struct NeoVimMergeTool;

impl MergeTool for NeoVimMergeTool {
    fn id(&self) -> &str { "neovim" }
    fn display_name(&self) -> &str { "NeoVim" }
    fn icon(&self) -> Option<&str> { Some("neovim.svg") }

    fn is_available(&self) -> bool {
        command_exists("nvim")
    }

    fn open_conflicted_file(&self, repo_path: &Path, file_path: &str, log: &CommandLog) -> Result<(), AppError> {
        let full_path = repo_path.join(file_path);
        // Open nvim in diff mode in a new terminal window
        open_in_terminal(&["nvim", "-d", &full_path.to_string_lossy()], repo_path, log)
    }
}

// ---------------------------------------------------------------------------
// Vim
// ---------------------------------------------------------------------------

pub struct VimMergeTool;

impl MergeTool for VimMergeTool {
    fn id(&self) -> &str { "vim" }
    fn display_name(&self) -> &str { "Vim" }
    fn icon(&self) -> Option<&str> { Some("vim.svg") }

    fn is_available(&self) -> bool {
        command_exists("vim")
    }

    fn open_conflicted_file(&self, repo_path: &Path, file_path: &str, log: &CommandLog) -> Result<(), AppError> {
        let full_path = repo_path.join(file_path);
        open_in_terminal(&["vim", "-d", &full_path.to_string_lossy()], repo_path, log)
    }
}

// ---------------------------------------------------------------------------
// JetBrains Rider
// ---------------------------------------------------------------------------

pub struct RiderMergeTool;

impl MergeTool for RiderMergeTool {
    fn id(&self) -> &str { "rider" }
    fn display_name(&self) -> &str { "JetBrains Rider" }
    fn icon(&self) -> Option<&str> { Some("rider.svg") }

    fn is_available(&self) -> bool {
        command_exists("rider")
    }

    fn open_conflicted_file(&self, repo_path: &Path, file_path: &str, log: &CommandLog) -> Result<(), AppError> {
        let full_path = repo_path.join(file_path);
        let full = full_path.to_string_lossy();
        spawn_and_log(
            "rider",
            &["merge", &full, &full, &full],
            repo_path,
            "Rider",
            log,
        )
    }
}

// ---------------------------------------------------------------------------
// JetBrains IntelliJ IDEA
// ---------------------------------------------------------------------------

pub struct IntelliJMergeTool;

impl MergeTool for IntelliJMergeTool {
    fn id(&self) -> &str { "intellij" }
    fn display_name(&self) -> &str { "IntelliJ IDEA" }
    fn icon(&self) -> Option<&str> { Some("intellij.svg") }

    fn is_available(&self) -> bool {
        command_exists("idea")
    }

    fn open_conflicted_file(&self, repo_path: &Path, file_path: &str, log: &CommandLog) -> Result<(), AppError> {
        let full_path = repo_path.join(file_path);
        let full = full_path.to_string_lossy();
        spawn_and_log(
            "idea",
            &["merge", &full, &full, &full],
            repo_path,
            "IntelliJ IDEA",
            log,
        )
    }
}

// ---------------------------------------------------------------------------
// KDiff3
// ---------------------------------------------------------------------------

pub struct KDiff3MergeTool;

impl MergeTool for KDiff3MergeTool {
    fn id(&self) -> &str { "kdiff3" }
    fn display_name(&self) -> &str { "KDiff3" }

    fn is_available(&self) -> bool {
        command_exists("kdiff3")
    }

    fn open_conflicted_file(&self, repo_path: &Path, file_path: &str, log: &CommandLog) -> Result<(), AppError> {
        let full_path = repo_path.join(file_path);
        spawn_and_log(
            "kdiff3",
            &[&full_path.to_string_lossy()],
            repo_path,
            "KDiff3",
            log,
        )
    }
}

// ---------------------------------------------------------------------------
// Meld
// ---------------------------------------------------------------------------

pub struct MeldMergeTool;

impl MergeTool for MeldMergeTool {
    fn id(&self) -> &str { "meld" }
    fn display_name(&self) -> &str { "Meld" }

    fn is_available(&self) -> bool {
        command_exists("meld")
    }

    fn open_conflicted_file(&self, repo_path: &Path, file_path: &str, log: &CommandLog) -> Result<(), AppError> {
        let full_path = repo_path.join(file_path);
        spawn_and_log(
            "meld",
            &[&full_path.to_string_lossy()],
            repo_path,
            "Meld",
            log,
        )
    }
}

// ---------------------------------------------------------------------------
// Beyond Compare
// ---------------------------------------------------------------------------

pub struct BeyondCompareMergeTool;

impl MergeTool for BeyondCompareMergeTool {
    fn id(&self) -> &str { "beyondcompare" }
    fn display_name(&self) -> &str { "Beyond Compare" }

    fn is_available(&self) -> bool {
        command_exists("bcomp") || command_exists("bcompare")
    }

    fn open_conflicted_file(&self, repo_path: &Path, file_path: &str, log: &CommandLog) -> Result<(), AppError> {
        let full_path = repo_path.join(file_path);
        // Try bcomp first (the "wait" variant), then bcompare
        let cmd = if command_exists("bcomp") { "bcomp" } else { "bcompare" };
        spawn_and_log(
            cmd,
            &[&full_path.to_string_lossy()],
            repo_path,
            "Beyond Compare",
            log,
        )
    }
}

// ---------------------------------------------------------------------------
// Visual Studio (Windows only)
// ---------------------------------------------------------------------------

#[cfg(target_os = "windows")]
pub struct VisualStudioMergeTool;

#[cfg(target_os = "windows")]
impl MergeTool for VisualStudioMergeTool {
    fn id(&self) -> &str { "visualstudio" }
    fn display_name(&self) -> &str { "Visual Studio" }
    fn icon(&self) -> Option<&str> { Some("visualstudio.svg") }

    fn is_available(&self) -> bool {
        // devenv.exe is on PATH when VS is installed with CLI tools
        command_exists("devenv")
    }

    fn open_conflicted_file(&self, repo_path: &Path, file_path: &str, log: &CommandLog) -> Result<(), AppError> {
        let full_path = repo_path.join(file_path);
        spawn_and_log(
            "devenv",
            &["/Edit", &full_path.to_string_lossy()],
            repo_path,
            "Visual Studio",
            log,
        )
    }
}

// ---------------------------------------------------------------------------
// Helper: open a terminal-based tool in a new terminal emulator window
// ---------------------------------------------------------------------------

fn open_in_terminal(args: &[&str], cwd: &Path, log: &CommandLog) -> Result<(), AppError> {
    #[cfg(target_os = "linux")]
    {
        // Try common terminal emulators in order of popularity
        let terminals = [
            ("ptyxis", vec!["--"]),
            ("gnome-terminal", vec!["--"]),
            ("konsole", vec!["-e"]),
            ("xfce4-terminal", vec!["-e"]),
            ("alacritty", vec!["-e"]),
            ("kitty", vec!["--"]),
            ("wezterm", vec!["start", "--"]),
            ("foot", vec!["--"]),
            ("xterm", vec!["-e"]),
            ("x-terminal-emulator", vec!["-e"]),
        ];
        for (term, prefix) in &terminals {
            if command_exists(term) {
                let mut full_args: Vec<&str> = prefix.clone();
                full_args.extend_from_slice(args);
                return spawn_and_log(term, &full_args, cwd, &format!("terminal {term}"), log);
            }
        }
        let msg = "No supported terminal emulator found".to_string();
        let _ = log.record("(merge tool) open terminal", &cwd.display().to_string(), -1, "", &msg, 0, false);
        Err(AppError::Io(msg))
    }
    #[cfg(target_os = "macos")]
    {
        // Use open -a Terminal with the command
        let full_cmd = args.iter()
            .map(|a| shell_escape::escape(std::borrow::Cow::Borrowed(a)).to_string())
            .collect::<Vec<_>>()
            .join(" ");
        let inner = format!("cd {} && {}", cwd.display(), full_cmd);
        spawn_and_log("open", &["-a", "Terminal", &inner], cwd, "Terminal", log)
    }
    #[cfg(target_os = "windows")]
    {
        let mut full_args: Vec<&str> = vec!["/c", "start", ""];
        full_args.extend_from_slice(args);
        spawn_and_log("cmd", &full_args, cwd, "terminal", log)
    }
}

/// Return all known merge tool implementations.
pub fn all_tools() -> Vec<Box<dyn MergeTool>> {
    vec![
        Box::new(VsCodeMergeTool),
        Box::new(NeoVimMergeTool),
        Box::new(VimMergeTool),
        Box::new(RiderMergeTool),
        Box::new(IntelliJMergeTool),
        Box::new(KDiff3MergeTool),
        Box::new(MeldMergeTool),
        Box::new(BeyondCompareMergeTool),
        #[cfg(target_os = "windows")]
        Box::new(VisualStudioMergeTool),
    ]
}
