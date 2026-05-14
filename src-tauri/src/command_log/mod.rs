use std::sync::{Arc, Mutex};

use serde::{Deserialize, Serialize};
use specta::Type;
use tauri::{AppHandle, Emitter};
use tracing::trace;

/// A single recorded git CLI invocation.
#[derive(Debug, Clone, Serialize, Deserialize, Type)]
pub struct CommandEntry {
    pub timestamp: String,
    pub command: String,
    pub cwd: String,
    pub exit_code: i32,
    pub stdout: String,
    pub stderr: String,
    /// How long the command took to execute, in milliseconds.
    pub elapsed_ms: u32,
    /// Whether this command was a background/periodic operation (e.g. polling).
    pub is_background: bool,
}

/// Thread-safe ring buffer that records recent git CLI invocations.
///
/// Cloning produces a handle to the *same* underlying buffer.
#[derive(Debug, Clone)]
pub struct CommandLog {
    entries: Arc<Mutex<Vec<CommandEntry>>>,
    app: Arc<Mutex<Option<AppHandle>>>,
    capacity: usize,
}

impl CommandLog {
    pub fn new(capacity: usize) -> Self {
        Self {
            entries: Arc::new(Mutex::new(Vec::with_capacity(capacity))),
            app: Arc::new(Mutex::new(None)),
            capacity,
        }
    }

    /// Set the app handle so the log can emit events to the frontend.
    pub fn set_app_handle(&self, handle: AppHandle) {
        *self.app.lock().expect("app handle lock poisoned") = Some(handle);
    }

    /// Return a snapshot of all recorded entries.
    pub fn entries(&self) -> Vec<CommandEntry> {
        self.entries.lock().expect("command log lock poisoned").clone()
    }

    /// Record a command execution.
    pub fn record(
        &self,
        command: &str,
        cwd: &str,
        exit_code: i32,
        stdout: &str,
        stderr: &str,
        elapsed_ms: u32,
        is_background: bool,
    ) {
        let entry = CommandEntry {
            timestamp: chrono::Utc::now().to_rfc3339(),
            command: command.to_string(),
            cwd: cwd.to_string(),
            exit_code,
            stdout: stdout.to_string(),
            stderr: stderr.to_string(),
            elapsed_ms,
            is_background,
        };

        trace!(cmd = %entry.command, cwd = %entry.cwd, code = entry.exit_code, "command logged");

        let mut entries = self.entries.lock().expect("command log lock poisoned");
        if entries.len() >= self.capacity {
            entries.remove(0);
        }
        entries.push(entry);
        drop(entries);

        // Notify the frontend
        if let Some(app) = self.app.lock().expect("app handle lock poisoned").as_ref() {
            let _ = app.emit("command-log-updated", ());
        }
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn record_and_retrieve() {
        let log = CommandLog::new(10);
        log.record("git status", "/tmp/repo", 0, "clean\n", "", 42, false);

        let entries = log.entries();
        assert_eq!(entries.len(), 1);
        assert_eq!(entries[0].command, "git status");
        assert_eq!(entries[0].cwd, "/tmp/repo");
        assert_eq!(entries[0].exit_code, 0);
        assert_eq!(entries[0].stdout, "clean\n");
        assert_eq!(entries[0].stderr, "");
        assert_eq!(entries[0].elapsed_ms, 42);
        assert!(!entries[0].is_background);
    }

    #[test]
    fn respects_capacity() {
        let log = CommandLog::new(3);
        for i in 0..5 {
            log.record(&format!("cmd {i}"), "/tmp", i, "", "", 10, false);
        }

        let entries = log.entries();
        assert_eq!(entries.len(), 3);
        // Oldest entries should have been evicted
        assert_eq!(entries[0].command, "cmd 2");
        assert_eq!(entries[1].command, "cmd 3");
        assert_eq!(entries[2].command, "cmd 4");
    }

    #[test]
    fn clone_shares_state() {
        let log = CommandLog::new(10);
        let log2 = log.clone();

        log.record("first", "/a", 0, "", "", 5, false);
        log2.record("second", "/b", 1, "", "", 8, true);

        let entries = log.entries();
        assert_eq!(entries.len(), 2);
        assert_eq!(entries[0].command, "first");
        assert_eq!(entries[1].command, "second");

        // Both handles see the same data
        assert_eq!(log2.entries().len(), 2);
    }

    #[test]
    fn empty_log_returns_empty_vec() {
        let log = CommandLog::new(10);
        assert!(log.entries().is_empty());
    }

    #[test]
    fn timestamps_are_populated() {
        let log = CommandLog::new(10);
        log.record("git log", "/tmp", 0, "", "", 1, false);

        let entries = log.entries();
        assert!(!entries[0].timestamp.is_empty());
        // Should be valid RFC 3339
        assert!(chrono::DateTime::parse_from_rfc3339(&entries[0].timestamp).is_ok());
    }
}
