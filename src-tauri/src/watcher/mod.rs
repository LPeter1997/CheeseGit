use std::collections::HashMap;
use std::path::{Path, PathBuf};
use std::sync::Mutex;
use std::time::Duration;

use notify_debouncer_mini::{new_debouncer, DebouncedEventKind, Debouncer};
use notify_debouncer_mini::notify::{RecommendedWatcher, RecursiveMode};
use tauri::{AppHandle, Emitter};

/// Payload emitted when files change in a watched repository.
#[derive(Debug, Clone, serde::Serialize)]
pub struct RepoChangedEvent {
    pub repo_path: String,
}

struct WatcherEntry {
    _debouncer: Debouncer<RecommendedWatcher>,
    ref_count: usize,
}

/// Manages filesystem watchers for open repositories.
pub struct RepoWatcherManager {
    watchers: Mutex<HashMap<PathBuf, WatcherEntry>>,
}

impl Default for RepoWatcherManager {
    fn default() -> Self {
        Self::new()
    }
}

impl RepoWatcherManager {
    pub fn new() -> Self {
        Self {
            watchers: Mutex::new(HashMap::new()),
        }
    }

    /// Start watching a repository's working directory.
    /// Uses reference counting so multiple callers can watch the same repo.
    /// Emits `repo-files-changed` events to the frontend when files change.
    pub fn start_watching(&self, repo_path: &Path, app_handle: AppHandle) -> Result<(), String> {
        let mut watchers = self.watchers.lock().map_err(|e| e.to_string())?;

        // Increment ref count if already watching
        if let Some(entry) = watchers.get_mut(repo_path) {
            entry.ref_count += 1;
            return Ok(());
        }

        let repo_path_str = repo_path.to_string_lossy().to_string();

        let mut debouncer = new_debouncer(
            Duration::from_millis(500),
            move |events: Result<Vec<notify_debouncer_mini::DebouncedEvent>, notify_debouncer_mini::notify::Error>| {
                if let Ok(events) = events {
                    // Only emit if there are actual file changes (not just metadata)
                    let has_changes = events.iter().any(|e| e.kind == DebouncedEventKind::Any);
                    if has_changes {
                        let _ = app_handle.emit(
                            "repo-files-changed",
                            RepoChangedEvent { repo_path: repo_path_str.clone() },
                        );
                    }
                }
            },
        ).map_err(|e| format!("Failed to create watcher: {e}"))?;

        debouncer
            .watcher()
            .watch(repo_path, RecursiveMode::Recursive)
            .map_err(|e| format!("Failed to watch path: {e}"))?;

        watchers.insert(repo_path.to_path_buf(), WatcherEntry {
            _debouncer: debouncer,
            ref_count: 1,
        });
        Ok(())
    }

    /// Stop watching a repository (decrements ref count, removes watcher when zero).
    pub fn stop_watching(&self, repo_path: &Path) -> Result<(), String> {
        let mut watchers = self.watchers.lock().map_err(|e| e.to_string())?;
        if let Some(entry) = watchers.get_mut(repo_path) {
            entry.ref_count -= 1;
            if entry.ref_count == 0 {
                watchers.remove(repo_path);
            }
        }
        Ok(())
    }
}
