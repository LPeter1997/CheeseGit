use std::collections::HashSet;
use std::sync::Arc;

use crate::error::AppError;
use crate::syntax::{SyntaxHighlighter, SyntaxToken};

use super::spawn_blocking;

/// Tokenize the given file content for syntax highlighting.
/// Returns one array of tokens per line (split by `\n`).
///
/// When `needed_lines` is provided, only those line indices get full token
/// detail; the rest receive a single plain token.  This dramatically
/// reduces work for large files where only a diff subset is displayed.
#[tauri::command]
#[specta::specta]
pub async fn tokenize_content(
    file_path: String,
    content: String,
    needed_lines: Option<Vec<u32>>,
    highlighter: tauri::State<'_, Arc<SyntaxHighlighter>>,
) -> Result<Vec<Vec<SyntaxToken>>, AppError> {
    let highlighter = highlighter.inner().clone();
    spawn_blocking(move || {
        let needed: Option<HashSet<usize>> =
            needed_lines.map(|v| v.into_iter().map(|n| n as usize).collect());
        highlighter.tokenize(&file_path, &content, needed.as_ref())
    })
    .await
}
