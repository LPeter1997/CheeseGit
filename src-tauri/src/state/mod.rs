use std::fs;
use std::path::PathBuf;
use std::sync::Mutex;

use serde::{Deserialize, Serialize};
use specta::Type;
use tauri::{AppHandle, Manager};

/// Persisted application state (survives across app restarts).
#[derive(Debug, Clone, Serialize, Deserialize, Type)]
pub struct AppState {
    /// Paths of repositories that were open.
    #[serde(default)]
    pub open_repos: Vec<String>,
    /// Index of the active tab.
    #[serde(default)]
    pub active_index: i32,
}

impl Default for AppState {
    fn default() -> Self {
        Self {
            open_repos: Vec::new(),
            active_index: -1,
        }
    }
}

/// Manages loading and saving of persistent app state.
pub struct AppStateManager {
    state: Mutex<AppState>,
    path: PathBuf,
}

impl AppStateManager {
    /// Create a manager that reads/writes `state.json` in the app config dir.
    pub fn new(app: &AppHandle) -> Self {
        let config_dir = app
            .path()
            .app_config_dir()
            .expect("failed to resolve app config dir");

        let path = config_dir.join("state.json");
        let state = Self::load_from(&path);

        Self {
            state: Mutex::new(state),
            path,
        }
    }

    /// Get a snapshot of the current state.
    pub fn get(&self) -> AppState {
        self.state.lock().expect("state lock poisoned").clone()
    }

    /// Replace the state and persist to disk.
    pub fn save(&self, new_state: AppState) {
        *self.state.lock().expect("state lock poisoned") = new_state;
        self.persist();
    }

    fn persist(&self) {
        let state = self.state.lock().expect("state lock poisoned").clone();

        // Ensure parent directory exists.
        if let Some(parent) = self.path.parent() {
            let _ = fs::create_dir_all(parent);
        }

        let json = serde_json::to_string_pretty(&state).expect("failed to serialize state");
        let _ = fs::write(&self.path, json);
    }

    fn load_from(path: &PathBuf) -> AppState {
        match fs::read_to_string(path) {
            Ok(contents) => serde_json::from_str(&contents).unwrap_or_default(),
            Err(_) => AppState::default(),
        }
    }
}
