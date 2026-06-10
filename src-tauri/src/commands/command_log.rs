use crate::command_log::{CommandEntry, CommandLog};
use crate::error::AppError;

/// Return all recorded git CLI invocations.
#[tauri::command]
#[specta::specta]
pub fn get_command_log(
    log: tauri::State<'_, CommandLog>,
) -> Result<Vec<CommandEntry>, AppError> {
    log.entries()
}

/// Format the full command log as plain text and write it to `path`.
/// Used by the "Export" action in the command log panel.
#[tauri::command]
#[specta::specta]
pub fn export_command_log(
    path: String,
    log: tauri::State<'_, CommandLog>,
) -> Result<(), AppError> {
    let entries = log.entries()?;
    let mut text = String::new();

    for entry in &entries {
        text.push_str(&format!("[{}] $ {}\n", entry.timestamp, entry.command));
        text.push_str(&format!("cwd: {}\n", entry.cwd));
        text.push_str(&format!(
            "exit code: {}  ({} ms)\n",
            entry.exit_code, entry.elapsed_ms
        ));
        if !entry.stdout.is_empty() {
            text.push_str("--- stdout ---\n");
            text.push_str(&entry.stdout);
            if !entry.stdout.ends_with('\n') {
                text.push('\n');
            }
        }
        if !entry.stderr.is_empty() {
            text.push_str("--- stderr ---\n");
            text.push_str(&entry.stderr);
            if !entry.stderr.ends_with('\n') {
                text.push('\n');
            }
        }
        text.push('\n');
    }

    std::fs::write(&path, text)
        .map_err(|e| AppError::Io(format!("failed to write command log to {path}: {e}")))
}
