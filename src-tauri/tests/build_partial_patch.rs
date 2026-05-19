use cheesegit_lib::vcs::git::build_partial_patch;
use cheesegit_lib::vcs::types::{DiffHunk, DiffLine, DiffLineKind, FileDiff, LineSelection};

fn ctx(content: &str, old: u32, new: u32) -> DiffLine {
    DiffLine {
        kind: DiffLineKind::Context,
        content: content.to_string(),
        old_lineno: Some(old),
        new_lineno: Some(new),
    }
}

fn add(content: &str, new: u32) -> DiffLine {
    DiffLine {
        kind: DiffLineKind::Addition,
        content: content.to_string(),
        old_lineno: None,
        new_lineno: Some(new),
    }
}

fn del(content: &str, old: u32) -> DiffLine {
    DiffLine {
        kind: DiffLineKind::Deletion,
        content: content.to_string(),
        old_lineno: Some(old),
        new_lineno: None,
    }
}

#[test]
fn stage_single_addition() {
    let diff = FileDiff {
        path: "file.txt".to_string(),
        hunks: vec![DiffHunk {
            header: "@@ -1,3 +1,4 @@".to_string(),
            old_start: 1,
            new_start: 1,
            lines: vec![
                ctx("line1", 1, 1),
                add("new line", 2),
                ctx("line2", 2, 3),
                ctx("line3", 3, 4),
            ],
        }],
    };

    let selections = vec![LineSelection {
        hunk_index: 0,
        line_index: 1,
    }];

    let patch = build_partial_patch("file.txt", &diff, &selections, false);

    assert!(patch.contains("--- a/file.txt"));
    assert!(patch.contains("+++ b/file.txt"));
    assert!(patch.contains("@@ -1,3 +1,4 @@"));
    assert!(patch.contains("+new line\n"));
    assert!(patch.contains(" line1\n"));
}

#[test]
fn stage_single_deletion() {
    let diff = FileDiff {
        path: "file.txt".to_string(),
        hunks: vec![DiffHunk {
            header: "@@ -1,3 +1,2 @@".to_string(),
            old_start: 1,
            new_start: 1,
            lines: vec![ctx("line1", 1, 1), del("removed", 2), ctx("line3", 3, 2)],
        }],
    };

    let selections = vec![LineSelection {
        hunk_index: 0,
        line_index: 1,
    }];

    let patch = build_partial_patch("file.txt", &diff, &selections, false);

    assert!(patch.contains("@@ -1,3 +1,2 @@"));
    assert!(patch.contains("-removed\n"));
}

#[test]
fn unselected_addition_is_omitted() {
    let diff = FileDiff {
        path: "file.txt".to_string(),
        hunks: vec![DiffHunk {
            header: "@@ -1,2 +1,4 @@".to_string(),
            old_start: 1,
            new_start: 1,
            lines: vec![
                ctx("line1", 1, 1),
                add("selected", 2),
                add("not selected", 3),
                ctx("line2", 2, 4),
            ],
        }],
    };

    // Only select the first addition.
    let selections = vec![LineSelection {
        hunk_index: 0,
        line_index: 1,
    }];

    let patch = build_partial_patch("file.txt", &diff, &selections, false);

    assert!(patch.contains("+selected\n"));
    assert!(!patch.contains("+not selected"));
    // old_count=2, new_count=3 (2 context + 1 addition)
    assert!(patch.contains("@@ -1,2 +1,3 @@"));
}

#[test]
fn unselected_deletion_becomes_context() {
    let diff = FileDiff {
        path: "file.txt".to_string(),
        hunks: vec![DiffHunk {
            header: "@@ -1,3 +1,1 @@".to_string(),
            old_start: 1,
            new_start: 1,
            lines: vec![
                del("keep as context", 1),
                del("actually remove", 2),
                ctx("line3", 3, 1),
            ],
        }],
    };

    // Only select the second deletion.
    let selections = vec![LineSelection {
        hunk_index: 0,
        line_index: 1,
    }];

    let patch = build_partial_patch("file.txt", &diff, &selections, false);

    // "keep as context" becomes a context line.
    assert!(patch.contains(" keep as context\n"));
    assert!(patch.contains("-actually remove\n"));
    // old_count=3 (2 deletions counted as old + 1 context), new_count=2 (unselected del becomes context + original context)
    assert!(patch.contains("@@ -1,3 +1,2 @@"));
}

#[test]
fn empty_selections_produce_empty_patch_body() {
    let diff = FileDiff {
        path: "file.txt".to_string(),
        hunks: vec![DiffHunk {
            header: "@@ -1,2 +1,3 @@".to_string(),
            old_start: 1,
            new_start: 1,
            lines: vec![ctx("a", 1, 1), add("b", 2), ctx("c", 2, 3)],
        }],
    };

    let patch = build_partial_patch("file.txt", &diff, &[], false);

    // Header is present but no hunk (because no selection in the hunk).
    assert!(patch.contains("--- a/file.txt"));
    assert!(!patch.contains("@@"));
}

#[test]
fn multiple_hunks_only_selected_emitted() {
    let diff = FileDiff {
        path: "file.txt".to_string(),
        hunks: vec![
            DiffHunk {
                header: "@@ -1,2 +1,3 @@".to_string(),
                old_start: 1,
                new_start: 1,
                lines: vec![ctx("a", 1, 1), add("b", 2), ctx("c", 2, 3)],
            },
            DiffHunk {
                header: "@@ -10,2 +11,3 @@".to_string(),
                old_start: 10,
                new_start: 11,
                lines: vec![ctx("x", 10, 11), add("y", 12), ctx("z", 11, 13)],
            },
        ],
    };

    // Only select from second hunk.
    let selections = vec![LineSelection {
        hunk_index: 1,
        line_index: 1,
    }];

    let patch = build_partial_patch("file.txt", &diff, &selections, false);

    // Should not contain first hunk.
    assert!(!patch.contains("@@ -1,"));
    assert!(patch.contains("@@ -10,"));
    assert!(patch.contains("+y\n"));
}

// ── Reverse (unstaging) tests ───────────────────────────────────

#[test]
fn unstage_single_addition() {
    // Staged diff shows an addition — unstaging should produce a patch
    // that when reverse-applied removes it from the index.
    let diff = FileDiff {
        path: "file.txt".to_string(),
        hunks: vec![DiffHunk {
            header: "@@ -1,2 +1,3 @@".to_string(),
            old_start: 1,
            new_start: 1,
            lines: vec![
                ctx("line1", 1, 1),
                add("staged line", 2),
                ctx("line2", 2, 3),
            ],
        }],
    };

    let selections = vec![LineSelection {
        hunk_index: 0,
        line_index: 1,
    }];

    let patch = build_partial_patch("file.txt", &diff, &selections, true);

    assert!(patch.contains("+staged line\n"));
    // old=2 context, new=3 (2 context + 1 addition)
    assert!(patch.contains("@@ -1,2 +1,3 @@"));
}

#[test]
fn unstage_one_addition_among_multiple() {
    // Two additions staged, unstage only one. The other should become context.
    let diff = FileDiff {
        path: "file.txt".to_string(),
        hunks: vec![DiffHunk {
            header: "@@ -1,1 +1,3 @@".to_string(),
            old_start: 1,
            new_start: 1,
            lines: vec![
                ctx("line1", 1, 1),
                add("keep staged", 2),
                add("unstage this", 3),
            ],
        }],
    };

    // Only select the second addition for unstaging.
    let selections = vec![LineSelection {
        hunk_index: 0,
        line_index: 2,
    }];

    let patch = build_partial_patch("file.txt", &diff, &selections, true);

    // "keep staged" should become context (it stays in index).
    assert!(patch.contains(" keep staged\n"));
    assert!(patch.contains("+unstage this\n"));
    // old_count=2 (context + non-selected addition as context), new_count=3
    assert!(patch.contains("@@ -1,2 +1,3 @@"));
}

#[test]
fn unstage_deletion_omits_non_selected_deletions() {
    // Staged diff has deletions. When unstaging, non-selected deletions
    // don't exist in the index and should be omitted.
    let diff = FileDiff {
        path: "file.txt".to_string(),
        hunks: vec![DiffHunk {
            header: "@@ -1,3 +1,1 @@".to_string(),
            old_start: 1,
            new_start: 1,
            lines: vec![
                del("unstage this", 1),
                del("keep staged", 2),
                ctx("line3", 3, 1),
            ],
        }],
    };

    // Only select the first deletion for unstaging.
    let selections = vec![LineSelection {
        hunk_index: 0,
        line_index: 0,
    }];

    let patch = build_partial_patch("file.txt", &diff, &selections, true);

    assert!(patch.contains("-unstage this\n"));
    // "keep staged" is omitted (doesn't exist in index).
    assert!(!patch.contains("keep staged"));
    // old_count=1 (selected deletion), new_count=1 (context)
    assert!(patch.contains("@@ -1,2 +1,1 @@"));
}
