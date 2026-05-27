use crate::error::AppError;
use crate::state::{AppState, AppStateManager};

/// Return the persisted app state (repos, active tab, etc.).
#[tauri::command]
#[specta::specta]
pub fn get_app_state(
    manager: tauri::State<'_, AppStateManager>,
) -> Result<AppState, AppError> {
    manager.get()
}

/// Save the current app state to disk.
#[tauri::command]
#[specta::specta]
pub fn save_app_state(
    state: AppState,
    manager: tauri::State<'_, AppStateManager>,
) -> Result<(), AppError> {
    manager.save(state)
}
