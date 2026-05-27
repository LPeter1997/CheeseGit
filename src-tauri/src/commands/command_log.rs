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
