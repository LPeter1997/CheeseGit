use std::path::Path;
use std::sync::Mutex;

use crate::error::AppError;
use super::MergeTool;
use super::tools::{self, MergeToolInfo};

/// Service locator that discovers available merge tools at startup
/// and allows opening files in the selected tool.
pub struct MergeToolRegistry {
    /// All known tools (available or not), populated once at creation.
    tools: Vec<Box<dyn MergeTool>>,
    /// Ids of tools that were available at the last scan.
    available_ids: Mutex<Vec<String>>,
}

impl MergeToolRegistry {
    /// Create a new registry and scan for available tools.
    pub fn new() -> Self {
        let tools = tools::all_tools();
        let available_ids: Vec<String> = tools
            .iter()
            .filter(|t| t.is_available())
            .map(|t| t.id().to_string())
            .collect();

        Self {
            tools,
            available_ids: Mutex::new(available_ids),
        }
    }

    /// Re-scan the system for available tools (e.g. if the user installed one).
    pub fn rescan(&self) {
        let ids: Vec<String> = self
            .tools
            .iter()
            .filter(|t| t.is_available())
            .map(|t| t.id().to_string())
            .collect();
        if let Ok(mut guard) = self.available_ids.lock() {
            *guard = ids;
        }
    }

    /// Return info about all currently available merge tools.
    pub fn available_tools(&self) -> Vec<MergeToolInfo> {
        let ids = self.available_ids.lock().unwrap_or_else(|e| e.into_inner());
        self.tools
            .iter()
            .filter(|t| ids.contains(&t.id().to_string()))
            .map(|t| MergeToolInfo {
                id: t.id().to_string(),
                display_name: t.display_name().to_string(),
                icon: t.icon().map(|s| s.to_string()),
            })
            .collect()
    }

    /// Open a conflicted file using the tool with the given id.
    pub fn open_in_tool(
        &self,
        tool_id: &str,
        repo_path: &Path,
        file_path: &str,
    ) -> Result<(), AppError> {
        let ids = self.available_ids.lock().unwrap_or_else(|e| e.into_inner());
        if !ids.contains(&tool_id.to_string()) {
            return Err(AppError::Other(format!(
                "Merge tool '{}' is not available",
                tool_id,
            )));
        }
        drop(ids);

        let tool = self
            .tools
            .iter()
            .find(|t| t.id() == tool_id)
            .ok_or_else(|| AppError::Other(format!("Unknown merge tool: {tool_id}")))?;

        tool.open_conflicted_file(repo_path, file_path)
    }

    /// Resolve the preferred tool: if the given preference is still available
    /// return it, otherwise return the first available tool id, or None.
    pub fn resolve_preferred(&self, preferred_id: Option<&str>) -> Option<String> {
        let ids = self.available_ids.lock().unwrap_or_else(|e| e.into_inner());

        if let Some(pref) = preferred_id {
            if ids.contains(&pref.to_string()) {
                return Some(pref.to_string());
            }
        }

        ids.first().cloned()
    }

    /// Create a registry from a pre-built list of tools (for testing).
    #[cfg(test)]
    pub fn from_tools(tools: Vec<Box<dyn MergeTool>>) -> Self {
        let available_ids: Vec<String> = tools
            .iter()
            .filter(|t| t.is_available())
            .map(|t| t.id().to_string())
            .collect();

        Self {
            tools,
            available_ids: Mutex::new(available_ids),
        }
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    use std::path::Path;
    use std::sync::atomic::{AtomicBool, Ordering};

    /// A fake merge tool for testing.
    struct FakeTool {
        id: &'static str,
        name: &'static str,
        available: AtomicBool,
        opened: Mutex<Vec<String>>,
    }

    impl FakeTool {
        fn new(id: &'static str, name: &'static str, available: bool) -> Self {
            Self {
                id,
                name,
                available: AtomicBool::new(available),
                opened: Mutex::new(Vec::new()),
            }
        }
    }

    impl MergeTool for FakeTool {
        fn id(&self) -> &str { self.id }
        fn display_name(&self) -> &str { self.name }
        fn is_available(&self) -> bool { self.available.load(Ordering::Relaxed) }
        fn open_conflicted_file(&self, _repo_path: &Path, file_path: &str) -> Result<(), AppError> {
            self.opened.lock().unwrap().push(file_path.to_string());
            Ok(())
        }
    }

    fn make_registry(tools: Vec<(&'static str, &'static str, bool)>) -> MergeToolRegistry {
        let boxed: Vec<Box<dyn MergeTool>> = tools
            .into_iter()
            .map(|(id, name, avail)| Box::new(FakeTool::new(id, name, avail)) as Box<dyn MergeTool>)
            .collect();
        MergeToolRegistry::from_tools(boxed)
    }

    #[test]
    fn empty_registry_returns_no_tools() {
        let registry = make_registry(vec![]);
        assert!(registry.available_tools().is_empty());
        assert_eq!(registry.resolve_preferred(None), None);
    }

    #[test]
    fn only_available_tools_are_listed() {
        let registry = make_registry(vec![
            ("a", "Tool A", true),
            ("b", "Tool B", false),
            ("c", "Tool C", true),
        ]);
        let tools = registry.available_tools();
        assert_eq!(tools.len(), 2);
        assert_eq!(tools[0].id, "a");
        assert_eq!(tools[1].id, "c");
    }

    #[test]
    fn resolve_preferred_returns_matching_available_tool() {
        let registry = make_registry(vec![
            ("a", "Tool A", true),
            ("b", "Tool B", true),
        ]);
        assert_eq!(registry.resolve_preferred(Some("b")), Some("b".to_string()));
    }

    #[test]
    fn resolve_preferred_falls_back_when_pref_unavailable() {
        let registry = make_registry(vec![
            ("a", "Tool A", true),
            ("b", "Tool B", false),
        ]);
        // Prefer "b" but it's unavailable, falls back to "a"
        assert_eq!(registry.resolve_preferred(Some("b")), Some("a".to_string()));
    }

    #[test]
    fn resolve_preferred_falls_back_when_no_preference() {
        let registry = make_registry(vec![
            ("x", "Tool X", true),
        ]);
        assert_eq!(registry.resolve_preferred(None), Some("x".to_string()));
    }

    #[test]
    fn resolve_preferred_returns_none_when_nothing_available() {
        let registry = make_registry(vec![
            ("a", "Tool A", false),
        ]);
        assert_eq!(registry.resolve_preferred(Some("a")), None);
        assert_eq!(registry.resolve_preferred(None), None);
    }

    #[test]
    fn open_in_tool_fails_for_unavailable_tool() {
        let registry = make_registry(vec![
            ("a", "Tool A", false),
        ]);
        let result = registry.open_in_tool("a", Path::new("/repo"), "file.txt");
        assert!(result.is_err());
    }

    #[test]
    fn open_in_tool_fails_for_unknown_tool() {
        let registry = make_registry(vec![
            ("a", "Tool A", true),
        ]);
        let result = registry.open_in_tool("nonexistent", Path::new("/repo"), "file.txt");
        assert!(result.is_err());
    }

    #[test]
    fn open_in_tool_succeeds_for_available_tool() {
        let registry = make_registry(vec![
            ("a", "Tool A", true),
        ]);
        let result = registry.open_in_tool("a", Path::new("/repo"), "file.txt");
        assert!(result.is_ok());
    }

    #[test]
    fn registry_creates_without_panic() {
        let registry = MergeToolRegistry::new();
        let _tools = registry.available_tools();
    }
}
