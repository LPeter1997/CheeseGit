use std::collections::HashSet;

use serde::{Deserialize, Serialize};
use specta::Type;
use syntect::parsing::{ParseState, Scope, ScopeStack, ScopeStackOp, SyntaxSet};

use crate::error::AppError;

/// Maximum content size (in chars) we will tokenize.
/// Files larger than this skip syntax highlighting entirely.
const MAX_HIGHLIGHT_CHARS: usize = 500_000;

/// Maximum line count we will tokenize.
/// Files with more lines than this skip syntax highlighting entirely.
const MAX_HIGHLIGHT_LINES: usize = 10_000;

/// Semantic token category for frontend theming.
#[derive(Debug, Clone, Copy, PartialEq, Eq, Serialize, Deserialize, Type)]
#[serde(rename_all = "lowercase")]
pub enum TokenCategory {
    Keyword,
    String,
    Comment,
    Number,
    Operator,
    Function,
    Type,
    Variable,
    Punctuation,
    Tag,
    Attribute,
    Meta,
    Plain,
}

/// A syntax token within a single line — carries only length and category.
/// The frontend reconstructs the actual text content from the line string.
#[derive(Debug, Clone, Serialize, Deserialize, Type)]
pub struct SyntaxToken {
    /// Length of this token in characters (Unicode scalar values).
    pub length: u32,
    /// Semantic category for theming.
    pub category: TokenCategory,
}

/// Scope-to-category mapping entry, ordered by specificity.
struct ScopePattern {
    scope: Scope,
    category: TokenCategory,
}

/// Syntax highlighter backed by syntect.
/// Holds the syntax set and pre-computed scope patterns.
pub struct SyntaxHighlighter {
    syntax_set: SyntaxSet,
    patterns: Vec<ScopePattern>,
}

impl SyntaxHighlighter {
    pub fn new() -> Self {
        let syntax_set = SyntaxSet::load_defaults_newlines();

        // Pre-build scope patterns (order matters — more specific first).
        let raw_patterns: &[(&str, TokenCategory)] = &[
            ("comment", TokenCategory::Comment),
            ("string", TokenCategory::String),
            ("constant.numeric", TokenCategory::Number),
            ("constant.character", TokenCategory::String),
            ("constant.language", TokenCategory::Keyword),
            ("keyword.operator", TokenCategory::Operator),
            ("keyword", TokenCategory::Keyword),
            ("storage.type", TokenCategory::Keyword),
            ("storage.modifier", TokenCategory::Keyword),
            ("storage", TokenCategory::Keyword),
            ("entity.name.function", TokenCategory::Function),
            ("entity.name.type", TokenCategory::Type),
            ("entity.name.tag", TokenCategory::Tag),
            ("entity.other.attribute-name", TokenCategory::Attribute),
            ("entity.other.inherited-class", TokenCategory::Type),
            ("entity.name", TokenCategory::Function),
            ("variable.function", TokenCategory::Function),
            ("variable", TokenCategory::Variable),
            ("support.function", TokenCategory::Function),
            ("support.type", TokenCategory::Type),
            ("support.class", TokenCategory::Type),
            ("punctuation", TokenCategory::Punctuation),
            ("meta.tag", TokenCategory::Tag),
        ];

        let patterns = raw_patterns
            .iter()
            .filter_map(|(s, cat)| Scope::new(s).ok().map(|scope| ScopePattern { scope, category: *cat }))
            .collect();

        Self { syntax_set, patterns }
    }

    /// Tokenize the given content according to the file extension.
    /// Returns one `Vec<SyntaxToken>` per line (split by `\n`).
    ///
    /// When `needed_lines` is `Some`, only lines whose indices are in the set
    /// get full token detail; other lines receive a single plain token.  The
    /// parse state still processes every line so syntax scopes stay correct.
    pub fn tokenize(
        &self,
        file_path: &str,
        content: &str,
        needed_lines: Option<&HashSet<usize>>,
    ) -> Result<Vec<Vec<SyntaxToken>>, AppError> {
        // Skip highlighting for very large files.
        if content.len() > MAX_HIGHLIGHT_CHARS {
            return Ok(self.plain_tokens(content));
        }

        let lines: Vec<&str> = content.split('\n').collect();

        // Skip highlighting for files with too many lines.
        if lines.len() > MAX_HIGHLIGHT_LINES {
            return Ok(self.plain_tokens(content));
        }

        let syntax = self.find_syntax(file_path);
        let mut state = ParseState::new(syntax);
        let mut scope_stack = ScopeStack::new();
        let mut result = Vec::with_capacity(lines.len());

        for (i, line) in lines.iter().enumerate() {
            // syntect expects newline-terminated lines.
            let line_for_parse = format!("{}\n", line);
            let ops = state
                .parse_line(&line_for_parse, &self.syntax_set)
                .map_err(|e| AppError::Other(format!("Syntax parse error: {e}")))?;

            let need_detail = needed_lines.map_or(true, |set| set.contains(&i));

            if need_detail {
                let tokens = self.ops_to_tokens(line, &ops, &mut scope_stack);
                result.push(tokens);
            } else {
                // Apply scope ops for state correctness, emit a plain token.
                self.apply_ops(&ops, &mut scope_stack);
                let char_len = line.chars().count() as u32;
                result.push(if char_len > 0 {
                    vec![SyntaxToken { length: char_len, category: TokenCategory::Plain }]
                } else {
                    vec![]
                });
            }
        }

        Ok(result)
    }

    /// Find the best syntax definition for a file path.
    fn find_syntax(&self, file_path: &str) -> &syntect::parsing::SyntaxReference {
        let ext = file_path
            .rsplit('.')
            .next()
            .unwrap_or("")
            .to_lowercase();

        // Try the exact extension first.
        if let Some(syn) = self.syntax_set.find_syntax_by_extension(&ext) {
            return syn;
        }

        // Fallback mappings for extensions syntect doesn't know.
        let fallback = match ext.as_str() {
            "tsx" | "mts" | "cts" | "ts" => "js",
            "jsx" | "mjs" | "cjs" => "js",
            "svelte" | "vue" => "html",
            "fish" => "sh",
            "zsh" => "sh",
            "dockerfile" => "sh",
            "makefile" | "mk" => "sh",
            "lock" => "json",
            "jsonc" | "json5" => "json",
            _ => "",
        };

        if !fallback.is_empty() {
            if let Some(syn) = self.syntax_set.find_syntax_by_extension(fallback) {
                return syn;
            }
        }

        self.syntax_set.find_syntax_plain_text()
    }

    /// Apply scope stack operations without building tokens.
    /// Used for non-needed lines to keep the scope state correct.
    fn apply_ops(&self, ops: &[(usize, ScopeStackOp)], scope_stack: &mut ScopeStack) {
        for (_, op) in ops {
            scope_stack.apply(op).ok();
        }
    }

    /// Convert syntect scope operations for a single line into `SyntaxToken`s.
    fn ops_to_tokens(
        &self,
        line: &str,
        ops: &[(usize, ScopeStackOp)],
        scope_stack: &mut ScopeStack,
    ) -> Vec<SyntaxToken> {
        let line_bytes = line.as_bytes();
        let line_byte_len = line_bytes.len();
        let mut tokens: Vec<SyntaxToken> = Vec::new();
        let mut byte_pos: usize = 0;

        for (offset, op) in ops {
            let offset = (*offset).min(line_byte_len);
            if offset > byte_pos {
                let category = self.classify_scope(scope_stack);
                let char_len = line[byte_pos..offset].chars().count() as u32;
                if char_len > 0 {
                    // Merge with previous token if same category.
                    if let Some(last) = tokens.last_mut() {
                        if last.category == category {
                            last.length += char_len;
                        } else {
                            tokens.push(SyntaxToken {
                                length: char_len,
                                category,
                            });
                        }
                    } else {
                        tokens.push(SyntaxToken {
                            length: char_len,
                            category,
                        });
                    }
                }
                byte_pos = offset;
            }
            scope_stack.apply(op).ok();
        }

        // Remaining text after the last scope operation.
        if byte_pos < line_byte_len {
            let category = self.classify_scope(scope_stack);
            let char_len = line[byte_pos..].chars().count() as u32;
            if char_len > 0 {
                if let Some(last) = tokens.last_mut() {
                    if last.category == category {
                        last.length += char_len;
                    } else {
                        tokens.push(SyntaxToken {
                            length: char_len,
                            category,
                        });
                    }
                } else {
                    tokens.push(SyntaxToken {
                        length: char_len,
                        category,
                    });
                }
            }
        }

        tokens
    }

    /// Map the current scope stack to a semantic category.
    fn classify_scope(&self, scope_stack: &ScopeStack) -> TokenCategory {
        let scopes = scope_stack.as_slice();
        // Walk from most specific (last) to least specific.
        for scope in scopes.iter().rev() {
            for pattern in &self.patterns {
                if pattern.scope.is_prefix_of(*scope) {
                    return pattern.category;
                }
            }
        }
        TokenCategory::Plain
    }

    /// Produce plain (un-highlighted) tokens for oversized files.
    fn plain_tokens(&self, content: &str) -> Vec<Vec<SyntaxToken>> {
        content
            .split('\n')
            .map(|line| {
                let char_len = line.chars().count() as u32;
                if char_len == 0 {
                    vec![]
                } else {
                    vec![SyntaxToken {
                        length: char_len,
                        category: TokenCategory::Plain,
                    }]
                }
            })
            .collect()
    }
}
