use std::fs;
use std::path::PathBuf;
use std::sync::Mutex;

use serde::{Deserialize, Serialize};
use specta::Type;
use tauri::{AppHandle, Manager};

use crate::error::AppError;

/// Persisted application state (survives across app restarts).
#[derive(Debug, Clone, Serialize, Deserialize, Type)]
pub struct AppState {
    /// Paths of repositories that were open.
    #[serde(default)]
    pub open_repos: Vec<String>,
    /// Index of the active tab.
    #[serde(default)]
    pub active_index: i32,
    /// Last-used parent folder for creating/cloning repositories.
    #[serde(default)]
    pub last_parent_folder: Option<String>,
    /// Version the user chose to skip (won't be prompted again).
    #[serde(default)]
    pub skipped_version: Option<String>,
    /// Release notes to show in "What's new" dialog on next startup.
    #[serde(default)]
    pub pending_changelog: Option<String>,
    /// Version associated with the pending changelog.
    #[serde(default)]
    pub pending_changelog_version: Option<String>,
    /// Whether the user dismissed the Linux desktop entry registration prompt.
    #[serde(default)]
    pub dismiss_desktop_entry: bool,
    /// Preferred UI theme.
    #[serde(default)]
    pub theme: Option<String>,
}

impl Default for AppState {
    fn default() -> Self {
        Self {
            open_repos: Vec::new(),
            active_index: -1,
            last_parent_folder: None,
            skipped_version: None,
            pending_changelog: None,
            pending_changelog_version: None,
            dismiss_desktop_entry: false,
            theme: None,
        }
    }
}

/// Manages loading and saving of persistent app state.
pub struct AppStateManager {
    state: Mutex<AppState>,
    path: PathBuf,
}

impl AppStateManager {
    /// Create a manager that reads/writes `state.json`.
    ///
    /// The path can be overridden with the `CHEESEGIT_APPSTATE_PATH` environment
    /// variable (useful for e2e tests to avoid touching the real user state).
    /// Otherwise falls back to `state.json` inside the platform app config dir.
    pub fn new(app: &AppHandle) -> Self {
        let path = match std::env::var("CHEESEGIT_APPSTATE_PATH") {
            Ok(p) if !p.is_empty() => PathBuf::from(p),
            _ => {
                let config_dir = app
                    .path()
                    .app_config_dir()
                    .expect("failed to resolve app config dir");
                config_dir.join("state.json")
            }
        };
        let state = Self::load_from(&path);

        Self {
            state: Mutex::new(state),
            path,
        }
    }

    /// Get a snapshot of the current state.
    pub fn get(&self) -> Result<AppState, AppError> {
        self.state
            .lock()
            .map(|guard| guard.clone())
            .map_err(|e| AppError::LockPoisoned(format!("state lock poisoned: {}", e)))
    }

    /// Replace the state and persist to disk.
    pub fn save(&self, new_state: AppState) -> Result<(), AppError> {
        *self
            .state
            .lock()
            .map_err(|e| AppError::LockPoisoned(format!("state lock poisoned: {}", e)))? = new_state;
        self.persist()
    }

    fn persist(&self) -> Result<(), AppError> {
        let state = self
            .state
            .lock()
            .map_err(|e| AppError::LockPoisoned(format!("state lock poisoned: {}", e)))?
            .clone();

        // Ensure parent directory exists.
        if let Some(parent) = self.path.parent() {
            fs::create_dir_all(parent)
                .map_err(|e| AppError::Io(format!("failed to create app-state directory: {e}")))?;
        }

        let json = serde_json::to_string_pretty(&state)
            .map_err(|e| AppError::Other(format!("failed to serialize app state: {e}")))?;
        fs::write(&self.path, json)
            .map_err(|e| AppError::Io(format!("failed to write app state: {e}")))?;
        Ok(())
    }

    fn load_from(path: &PathBuf) -> AppState {
        match fs::read_to_string(path) {
            Ok(contents) => serde_json::from_str(&contents).unwrap_or_default(),
            Err(_) => AppState::default(),
        }
    }
}
