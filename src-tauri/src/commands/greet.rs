/// A simple greeting command to verify IPC works end-to-end.
#[tauri::command]
#[specta::specta]
pub fn greet(name: String) -> String {
    format!("Hello, {}! Welcome to CheeseGit.", name)
}
