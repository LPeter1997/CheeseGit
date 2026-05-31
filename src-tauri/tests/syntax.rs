use cheesegit_lib::syntax::{SyntaxHighlighter, TokenCategory};

#[test]
fn tokenizes_rust_code() {
    let hl = SyntaxHighlighter::new();
    let content = "fn main() {\n    let x = 42;\n}\n";
    let result = hl.tokenize("main.rs", content, None).unwrap();

    // Should produce 4 lines (split by \n: "fn main() {", "    let x = 42;", "}", "")
    assert_eq!(result.len(), 4);

    // First line should have tokens including a keyword ("fn")
    let first_line = &result[0];
    assert!(!first_line.is_empty(), "first line should have tokens");
    let categories: Vec<_> = first_line.iter().map(|t| t.category).collect();
    assert!(
        categories.contains(&TokenCategory::Keyword),
        "expected Keyword in first line, got {:?}",
        categories,
    );

    // Second line should have keyword ("let") and number ("42")
    let second_line = &result[1];
    let categories: Vec<_> = second_line.iter().map(|t| t.category).collect();
    assert!(
        categories.contains(&TokenCategory::Keyword),
        "expected Keyword in second line, got {:?}",
        categories,
    );
    assert!(
        categories.contains(&TokenCategory::Number),
        "expected Number in second line, got {:?}",
        categories,
    );

    // Verify total char lengths match line lengths
    for (i, line) in content.split('\n').enumerate() {
        let token_len: u32 = result[i].iter().map(|t| t.length).sum();
        let char_count = line.chars().count() as u32;
        assert_eq!(
            token_len, char_count,
            "token length mismatch on line {}: expected {} got {}",
            i, char_count, token_len,
        );
    }
}

#[test]
fn tokenizes_javascript_code() {
    let hl = SyntaxHighlighter::new();
    let content = "const x = \"hello\";\n// comment\n";
    let result = hl.tokenize("app.js", content, None).unwrap();

    assert_eq!(result.len(), 3);

    // First line should have keyword, string
    let cats: Vec<_> = result[0].iter().map(|t| t.category).collect();
    assert!(cats.contains(&TokenCategory::Keyword), "expected keyword, got {:?}", cats);
    assert!(cats.contains(&TokenCategory::String), "expected string, got {:?}", cats);

    // Second line should be a comment
    let cats: Vec<_> = result[1].iter().map(|t| t.category).collect();
    assert!(cats.contains(&TokenCategory::Comment), "expected comment, got {:?}", cats);
}

#[test]
fn tsx_falls_back_to_typescript() {
    let hl = SyntaxHighlighter::new();
    let content = "const x: number = 42;\n";
    let result = hl.tokenize("Component.tsx", content, None).unwrap();

    assert_eq!(result.len(), 2);
    // Should not panic or return empty — tsx falls back to ts
    let cats: Vec<_> = result[0].iter().map(|t| t.category).collect();
    assert!(cats.contains(&TokenCategory::Keyword), "expected keyword for tsx, got {:?}", cats);
}

#[test]
fn unknown_extension_falls_back_to_plain() {
    let hl = SyntaxHighlighter::new();
    let content = "hello world\nfoo bar\n";
    let result = hl.tokenize("data.xyz", content, None).unwrap();

    assert_eq!(result.len(), 3);
    // All tokens should be Plain
    for line_tokens in &result {
        for token in line_tokens {
            assert_eq!(token.category, TokenCategory::Plain);
        }
    }
}

#[test]
fn empty_content_produces_one_empty_line() {
    let hl = SyntaxHighlighter::new();
    let result = hl.tokenize("empty.rs", "", None).unwrap();
    assert_eq!(result.len(), 1);
    assert!(result[0].is_empty());
}

#[test]
fn large_content_skips_highlighting() {
    let hl = SyntaxHighlighter::new();
    // Create content larger than MAX_HIGHLIGHT_CHARS (500_000)
    let large = "x".repeat(600_000);
    let result = hl.tokenize("big.rs", &large, None).unwrap();
    // Should still return lines, all plain
    assert!(!result.is_empty());
    for line_tokens in &result {
        for token in line_tokens {
            assert_eq!(token.category, TokenCategory::Plain);
        }
    }
}

#[test]
fn token_lengths_sum_to_line_char_count() {
    let hl = SyntaxHighlighter::new();
    let content = "pub struct Foo {\n    bar: String,\n    baz: u32,\n}\n";
    let result = hl.tokenize("types.rs", content, None).unwrap();
    let lines: Vec<&str> = content.split('\n').collect();

    for (i, line) in lines.iter().enumerate() {
        let expected: u32 = line.chars().count() as u32;
        let actual: u32 = result[i].iter().map(|t| t.length).sum();
        assert_eq!(
            actual, expected,
            "line {} '{}': expected {} chars, got {}",
            i, line, expected, actual,
        );
    }
}

#[test]
fn python_code_tokenizes() {
    let hl = SyntaxHighlighter::new();
    let content = "def greet(name):\n    return f\"Hello {name}\"\n";
    let result = hl.tokenize("app.py", content, None).unwrap();

    assert_eq!(result.len(), 3);
    let cats: Vec<_> = result[0].iter().map(|t| t.category).collect();
    assert!(cats.contains(&TokenCategory::Keyword), "expected keyword in python, got {:?}", cats);
}

#[test]
fn html_with_tags() {
    let hl = SyntaxHighlighter::new();
    let content = "<div class=\"foo\">Hello</div>\n";
    let result = hl.tokenize("page.html", content, None).unwrap();

    assert_eq!(result.len(), 2);
    let cats: Vec<_> = result[0].iter().map(|t| t.category).collect();
    assert!(cats.contains(&TokenCategory::Tag), "expected tag in html, got {:?}", cats);
}

#[test]
fn adjacent_same_category_tokens_merged() {
    let hl = SyntaxHighlighter::new();
    // Plain text should produce at most one token per line
    let content = "hello world foo bar baz\n";
    let result = hl.tokenize("readme.txt", content, None).unwrap();

    assert_eq!(result.len(), 2);
    // Plain text should have exactly 1 merged token per non-empty line
    assert_eq!(result[0].len(), 1);
    assert_eq!(result[0][0].category, TokenCategory::Plain);
    assert_eq!(result[0][0].length, 23); // "hello world foo bar baz".len() == 23
}
