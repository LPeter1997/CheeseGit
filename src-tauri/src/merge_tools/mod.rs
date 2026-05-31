mod registry;
mod tools;

pub use registry::MergeToolRegistry;
pub use tools::MergeToolInfo;

use std::path::Path;

use crate::error::AppError;

/// A merge tool that can open conflicted files for manual resolution.
pub trait MergeTool: Send + Sync {
    /// A unique machine-readable identifier (e.g. "vscode", "neovim").
    fn id(&self) -> &str;

    /// Human-readable display name (e.g. "Visual Studio Code").
    fn display_name(&self) -> &str;

    /// Optional icon filename (e.g. "vscode.svg") relative to `/icons/merge-tools/`.
    fn icon(&self) -> Option<&str> { None }

    /// Check whether this tool is installed and available on the system.
    fn is_available(&self) -> bool;

    /// Open a conflicted file in this merge tool.
    /// `repo_path` is the repository root and `file_path` is relative to it.
    fn open_conflicted_file(
        &self,
        repo_path: &Path,
        file_path: &str,
    ) -> Result<(), AppError>;
}
