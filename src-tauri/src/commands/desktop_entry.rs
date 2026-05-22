use crate::desktop_entry::DesktopEntryStatus;
use crate::error::AppError;

/// Check the status of the Linux desktop entry.
#[tauri::command]
#[specta::specta]
pub fn check_desktop_entry_status() -> Result<DesktopEntryStatus, AppError> {
    crate::desktop_entry::check_desktop_entry()
}

/// Register or update the Linux desktop entry.
#[tauri::command]
#[specta::specta]
pub fn register_desktop_entry() -> Result<(), AppError> {
    crate::desktop_entry::register_desktop_entry()
}
