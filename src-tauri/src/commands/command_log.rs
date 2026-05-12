use crate::command_log::{CommandEntry, CommandLog};

/// Return all recorded git CLI invocations.
#[tauri::command]
#[specta::specta]
pub fn get_command_log(log: tauri::State<'_, CommandLog>) -> Vec<CommandEntry> {
    log.entries()
}
