use std::path::Path;
use std::process::Command;

use serde::{Deserialize, Serialize};
use specta::Type;

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

    fn open_conflicted_file(&self, repo_path: &Path, file_path: &str) -> Result<(), AppError> {
        let full_path = repo_path.join(file_path);
        let child = Command::new("code")
            .args(["--wait", "--merge"])
            .arg(&full_path)
            .arg(&full_path)
            .arg(&full_path)
            .arg(&full_path)
            .current_dir(repo_path)
            .spawn()
            .map_err(|e| AppError::Io(format!("Failed to open VS Code: {e}")))?;
        // Don't wait — let the user work in the external tool
        drop(child);
        Ok(())
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

    fn open_conflicted_file(&self, repo_path: &Path, file_path: &str) -> Result<(), AppError> {
        let full_path = repo_path.join(file_path);
        // Open nvim in diff mode in a new terminal window
        let child = open_in_terminal(&["nvim", "-d", &full_path.to_string_lossy()], repo_path)?;
        drop(child);
        Ok(())
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

    fn open_conflicted_file(&self, repo_path: &Path, file_path: &str) -> Result<(), AppError> {
        let full_path = repo_path.join(file_path);
        let child = open_in_terminal(&["vim", "-d", &full_path.to_string_lossy()], repo_path)?;
        drop(child);
        Ok(())
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

    fn open_conflicted_file(&self, repo_path: &Path, file_path: &str) -> Result<(), AppError> {
        let full_path = repo_path.join(file_path);
        let child = Command::new("rider")
            .args(["merge", &full_path.to_string_lossy(), &full_path.to_string_lossy(), &full_path.to_string_lossy()])
            .current_dir(repo_path)
            .spawn()
            .map_err(|e| AppError::Io(format!("Failed to open Rider: {e}")))?;
        drop(child);
        Ok(())
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

    fn open_conflicted_file(&self, repo_path: &Path, file_path: &str) -> Result<(), AppError> {
        let full_path = repo_path.join(file_path);
        let child = Command::new("idea")
            .args(["merge", &full_path.to_string_lossy(), &full_path.to_string_lossy(), &full_path.to_string_lossy()])
            .current_dir(repo_path)
            .spawn()
            .map_err(|e| AppError::Io(format!("Failed to open IntelliJ IDEA: {e}")))?;
        drop(child);
        Ok(())
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

    fn open_conflicted_file(&self, repo_path: &Path, file_path: &str) -> Result<(), AppError> {
        let full_path = repo_path.join(file_path);
        let child = Command::new("kdiff3")
            .arg(&full_path)
            .current_dir(repo_path)
            .spawn()
            .map_err(|e| AppError::Io(format!("Failed to open KDiff3: {e}")))?;
        drop(child);
        Ok(())
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

    fn open_conflicted_file(&self, repo_path: &Path, file_path: &str) -> Result<(), AppError> {
        let full_path = repo_path.join(file_path);
        let child = Command::new("meld")
            .arg(&full_path)
            .current_dir(repo_path)
            .spawn()
            .map_err(|e| AppError::Io(format!("Failed to open Meld: {e}")))?;
        drop(child);
        Ok(())
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

    fn open_conflicted_file(&self, repo_path: &Path, file_path: &str) -> Result<(), AppError> {
        let full_path = repo_path.join(file_path);
        // Try bcomp first (the "wait" variant), then bcompare
        let cmd = if command_exists("bcomp") { "bcomp" } else { "bcompare" };
        let child = Command::new(cmd)
            .arg(&full_path)
            .current_dir(repo_path)
            .spawn()
            .map_err(|e| AppError::Io(format!("Failed to open Beyond Compare: {e}")))?;
        drop(child);
        Ok(())
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

    fn open_conflicted_file(&self, repo_path: &Path, file_path: &str) -> Result<(), AppError> {
        let full_path = repo_path.join(file_path);
        let child = Command::new("devenv")
            .args(["/Edit", &full_path.to_string_lossy()])
            .current_dir(repo_path)
            .spawn()
            .map_err(|e| AppError::Io(format!("Failed to open Visual Studio: {e}")))?;
        drop(child);
        Ok(())
    }
}

// ---------------------------------------------------------------------------
// Helper: open a terminal-based tool in a new terminal emulator window
// ---------------------------------------------------------------------------

fn open_in_terminal(args: &[&str], cwd: &Path) -> Result<std::process::Child, AppError> {
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
                let mut cmd = Command::new(term);
                for p in prefix {
                    cmd.arg(p);
                }
                cmd.args(args);
                cmd.current_dir(cwd);
                return cmd.spawn()
                    .map_err(|e| AppError::Io(format!("Failed to open terminal {term}: {e}")));
            }
        }
        Err(AppError::Io("No supported terminal emulator found".to_string()))
    }
    #[cfg(target_os = "macos")]
    {
        // Use open -a Terminal with the command
        let full_cmd = args.iter()
            .map(|a| shell_escape::escape(std::borrow::Cow::Borrowed(a)).to_string())
            .collect::<Vec<_>>()
            .join(" ");
        Command::new("open")
            .args(["-a", "Terminal"])
            .arg(&format!("cd {} && {}", cwd.display(), full_cmd))
            .spawn()
            .map_err(|e| AppError::Io(format!("Failed to open Terminal: {e}")))
    }
    #[cfg(target_os = "windows")]
    {
        let mut cmd = Command::new("cmd");
        cmd.arg("/c").arg("start").arg("").args(args);
        cmd.current_dir(cwd);
        cmd.spawn()
            .map_err(|e| AppError::Io(format!("Failed to open terminal: {e}")))
    }
}

/// Return all known merge tool implementations.
pub fn all_tools() -> Vec<Box<dyn MergeTool>> {
    let tools: Vec<Box<dyn MergeTool>> = vec![
        Box::new(VsCodeMergeTool),
        Box::new(NeoVimMergeTool),
        Box::new(VimMergeTool),
        Box::new(RiderMergeTool),
        Box::new(IntelliJMergeTool),
        Box::new(KDiff3MergeTool),
        Box::new(MeldMergeTool),
        Box::new(BeyondCompareMergeTool),
    ];

    #[cfg(target_os = "windows")]
    {
        tools.push(Box::new(VisualStudioMergeTool));
    }

    tools
}
