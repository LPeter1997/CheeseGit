//! Linux desktop entry registration.
//!
//! On Linux this creates/updates `.desktop` files so that CheeseGit shows up
//! in application launchers. On other platforms the commands are no-ops.

use serde::{Deserialize, Serialize};
use specta::Type;

use crate::error::AppError;

/// Status of the desktop entry relative to the running application.
#[derive(Debug, Clone, Serialize, Deserialize, Type)]
pub enum DesktopEntryStatus {
    /// Not applicable on this platform.
    NotApplicable,
    /// No desktop entry exists (user never registered or deleted it).
    Missing,
    /// A desktop entry exists and the Exec path matches the running binary.
    Current,
    /// A desktop entry exists but points to a different binary location.
    Stale,
}

/// Check the status of the CheeseGit desktop entry.
#[cfg(target_os = "linux")]
pub fn check_desktop_entry() -> Result<DesktopEntryStatus, AppError> {
    use std::fs;

    let desktop_path = desktop_file_path()?;

    if !desktop_path.exists() {
        return Ok(DesktopEntryStatus::Missing);
    }

    let contents = fs::read_to_string(&desktop_path)
        .map_err(|e| AppError::Io(format!("failed to read desktop file: {e}")))?;

    let current_exe = std::env::current_exe()
        .map_err(|e| AppError::Io(format!("failed to get current exe: {e}")))?;
    let current_exe_str = current_exe.to_string_lossy();

    // Check if the Exec line matches
    for line in contents.lines() {
        if let Some(exec_value) = line.strip_prefix("Exec=") {
            // The Exec line may have arguments like %U after the binary path
            let exec_bin = exec_value.split_whitespace().next().unwrap_or("");
            if exec_bin == current_exe_str.as_ref() {
                return Ok(DesktopEntryStatus::Current);
            } else {
                return Ok(DesktopEntryStatus::Stale);
            }
        }
    }

    // Desktop file exists but has no Exec line — treat as stale
    Ok(DesktopEntryStatus::Stale)
}

#[cfg(not(target_os = "linux"))]
pub fn check_desktop_entry() -> Result<DesktopEntryStatus, AppError> {
    Ok(DesktopEntryStatus::NotApplicable)
}

/// Register (or update) the desktop entry and icon for CheeseGit.
#[cfg(target_os = "linux")]
pub fn register_desktop_entry() -> Result<(), AppError> {
    use std::fs;

    let icon_path = install_icon()?;
    let desktop_path = desktop_file_path()?;

    let current_exe = std::env::current_exe()
        .map_err(|e| AppError::Io(format!("failed to get current exe: {e}")))?;

    let contents = format!(
        "[Desktop Entry]\n\
         Name=CheeseGit\n\
         Comment=Cross-platform version control UI\n\
         Exec={exe}\n\
         Icon={icon}\n\
         Terminal=false\n\
         Type=Application\n\
         Categories=Development;RevisionControl;\n\
         StartupWMClass=cheesegit\n",
        exe = current_exe.to_string_lossy(),
        icon = icon_path.to_string_lossy(),
    );

    if let Some(parent) = desktop_path.parent() {
        fs::create_dir_all(parent)
            .map_err(|e| AppError::Io(format!("failed to create desktop entry dir: {e}")))?;
    }

    fs::write(&desktop_path, contents)
        .map_err(|e| AppError::Io(format!("failed to write desktop file: {e}")))?;

    Ok(())
}

#[cfg(not(target_os = "linux"))]
pub fn register_desktop_entry() -> Result<(), AppError> {
    Ok(())
}

/// Return the path where we place the `.desktop` file.
#[cfg(target_os = "linux")]
fn desktop_file_path() -> Result<std::path::PathBuf, AppError> {
    let base = xdg::BaseDirectories::new()
        .map_err(|e| AppError::Io(format!("failed to determine XDG dirs: {e}")))?;

    Ok(base.get_data_home().join("applications/cheesegit.desktop"))
}

/// Install the SVG icon to the appropriate XDG icon location and return its path.
#[cfg(target_os = "linux")]
fn install_icon() -> Result<std::path::PathBuf, AppError> {
    use std::fs;

    let base = xdg::BaseDirectories::new()
        .map_err(|e| AppError::Io(format!("failed to determine XDG dirs: {e}")))?;

    let icon_dir = base.get_data_home().join("icons/hicolor/scalable/apps");

    fs::create_dir_all(&icon_dir)
        .map_err(|e| AppError::Io(format!("failed to create icon dir: {e}")))?;

    let icon_path = icon_dir.join("cheesegit.svg");

    // The SVG is bundled at compile time from the assets directory.
    let svg_bytes = include_bytes!("../../icons/cheesegit.svg");
    fs::write(&icon_path, svg_bytes)
        .map_err(|e| AppError::Io(format!("failed to write icon: {e}")))?;

    Ok(icon_path)
}
