import { describe, it, expect } from "vitest";
import { render } from "@testing-library/react";
import type { FileDiff, InlineHighlight } from "../../../ipc/bindings";
import type { HighlightToken } from "../hooks/useHighlightedLines";
import { InlineHighlightedLine, SyntaxHighlightedLine, buildUnifiedRows, buildSplitRows } from "./DiffViewShared";

describe("InlineHighlightedLine", () => {
  it("renders plain content when highlights is empty", () => {
    const { container } = render(
      <InlineHighlightedLine content="hello world" highlights={[]} kind="addition" />,
    );
    expect(container.textContent).toBe("hello world");
    expect(container.querySelectorAll("span")).toHaveLength(0);
  });

  it("wraps highlighted spans for additions", () => {
    const highlights: InlineHighlight[] = [{ start: 6, length: 5 }];
    const { container } = render(
      <InlineHighlightedLine content="hello world" highlights={highlights} kind="addition" />,
    );
    expect(container.textContent).toBe("hello world");
    const spans = container.querySelectorAll("span");
    expect(spans).toHaveLength(1);
    expect(spans[0].textContent).toBe("world");
    expect(spans[0].className).toContain("bg-success");
  });

  it("wraps highlighted spans for deletions", () => {
    const highlights: InlineHighlight[] = [{ start: 0, length: 5 }];
    const { container } = render(
      <InlineHighlightedLine content="hello world" highlights={highlights} kind="deletion" />,
    );
    const spans = container.querySelectorAll("span");
    expect(spans).toHaveLength(1);
    expect(spans[0].textContent).toBe("hello");
    expect(spans[0].className).toContain("bg-danger");
  });

  it("handles multiple highlight spans", () => {
    const highlights: InlineHighlight[] = [
      { start: 0, length: 3 },
      { start: 8, length: 4 },
    ];
    const { container } = render(
      <InlineHighlightedLine content="foo bar quux" highlights={highlights} kind="addition" />,
    );
    expect(container.textContent).toBe("foo bar quux");
    const spans = container.querySelectorAll("span");
    expect(spans).toHaveLength(2);
    expect(spans[0].textContent).toBe("foo");
    expect(spans[1].textContent).toBe("quux");
  });
});

describe("buildUnifiedRows passes highlights through", () => {
  it("carries highlight data from DiffLine to UnifiedRow", () => {
    const diff: FileDiff = {
      path: "test.ts",
      hunks: [
        {
          header: "@@ -1,1 +1,1 @@",
          old_start: 1,
          new_start: 1,
          lines: [
            {
              kind: "Deletion",
              content: "return a + b;",
              old_lineno: 1,
              new_lineno: null,
              highlights: [{ start: 9, length: 1 }],
            },
            {
              kind: "Addition",
              content: "return a - b;",
              old_lineno: null,
              new_lineno: 1,
              highlights: [{ start: 9, length: 1 }],
            },
          ],
        },
      ],
    };

    const rows = buildUnifiedRows(diff);
    // Skip hunk-header row
    const delRow = rows.find((r) => r.kind === "deletion");
    const addRow = rows.find((r) => r.kind === "addition");

    expect(delRow).toBeDefined();
    expect(delRow!.highlights).toEqual([{ start: 9, length: 1 }]);
    expect(addRow).toBeDefined();
    expect(addRow!.highlights).toEqual([{ start: 9, length: 1 }]);
  });

  it("context and hunk-header rows have empty highlights", () => {
    const diff: FileDiff = {
      path: "test.ts",
      hunks: [
        {
          header: "@@ -1,1 +1,1 @@",
          old_start: 1,
          new_start: 1,
          lines: [
            {
              kind: "Context",
              content: "unchanged",
              old_lineno: 1,
              new_lineno: 1,
              highlights: [],
            },
          ],
        },
      ],
    };

    const rows = buildUnifiedRows(diff);
    expect(rows[0].kind).toBe("hunk-header");
    expect(rows[0].highlights).toEqual([]);
    expect(rows[1].kind).toBe("context");
    expect(rows[1].highlights).toEqual([]);
  });
});

describe("buildSplitRows passes highlights through", () => {
  it("carries highlight data to left/right sides", () => {
    const diff: FileDiff = {
      path: "test.ts",
      hunks: [
        {
          header: "@@ -1,1 +1,1 @@",
          old_start: 1,
          new_start: 1,
          lines: [
            {
              kind: "Deletion",
              content: "foo(a)",
              old_lineno: 1,
              new_lineno: null,
              highlights: [{ start: 4, length: 1 }],
            },
            {
              kind: "Addition",
              content: "foo(x)",
              old_lineno: null,
              new_lineno: 1,
              highlights: [{ start: 4, length: 1 }],
            },
          ],
        },
      ],
    };

    const rows = buildSplitRows(diff);
    // Skip hunk-header row (index 0)
    const dataRow = rows[1];
    expect(dataRow.left.kind).toBe("deletion");
    expect(dataRow.left.highlights).toEqual([{ start: 4, length: 1 }]);
    expect(dataRow.right.kind).toBe("addition");
    expect(dataRow.right.highlights).toEqual([{ start: 4, length: 1 }]);
  });
});

describe("SyntaxHighlightedLine", () => {
  it("renders syntax-colored spans with no highlights", () => {
    const tokens: HighlightToken[] = [
      { content: "return", category: "keyword" },
      { content: " a + b;", category: "variable" },
    ];
    const { container } = render(
      <SyntaxHighlightedLine tokens={tokens} highlights={[]} kind="addition" />,
    );
    expect(container.textContent).toBe("return a + b;");
    const spans = container.querySelectorAll("span");
    expect(spans).toHaveLength(2);
    expect(spans[0].style.color).toBe("var(--cg-syntax-keyword)");
    expect(spans[1].style.color).toBe("var(--cg-syntax-variable)");
  });

  it("preserves syntax colors while adding highlight background for additions", () => {
    // "return a + b;" with highlight on "+" (at index 9, length 1)
    const tokens: HighlightToken[] = [
      { content: "return", category: "keyword" },
      { content: " a + b;", category: "variable" },
    ];
    const highlights: InlineHighlight[] = [{ start: 9, length: 1 }];
    const { container } = render(
      <SyntaxHighlightedLine tokens={tokens} highlights={highlights} kind="addition" />,
    );
    expect(container.textContent).toBe("return a + b;");
    // The second token " a + b;" should be split at the highlight boundary
    const spans = container.querySelectorAll("span");
    // "return" (no hl) | " a " (no hl) | "+" (hl) | " b;" (no hl)
    expect(spans.length).toBeGreaterThanOrEqual(3);
    // All spans should have syntax color
    for (const span of spans) {
      expect(span.style.color).toBeTruthy();
    }
    // The highlighted span should have the bg-success class
    const hlSpan = Array.from(spans).find((s) => s.textContent === "+");
    expect(hlSpan).toBeDefined();
    expect(hlSpan!.className).toContain("bg-success");
    expect(hlSpan!.style.color).toBe("var(--cg-syntax-variable)");
  });

  it("preserves syntax colors while adding highlight background for deletions", () => {
    const tokens: HighlightToken[] = [
      { content: "const", category: "keyword" },
      { content: " x", category: "variable" },
    ];
    const highlights: InlineHighlight[] = [{ start: 6, length: 1 }];
    const { container } = render(
      <SyntaxHighlightedLine tokens={tokens} highlights={highlights} kind="deletion" />,
    );
    expect(container.textContent).toBe("const x");
    const hlSpan = Array.from(container.querySelectorAll("span")).find(
      (s) => s.textContent === "x",
    );
    expect(hlSpan).toBeDefined();
    expect(hlSpan!.className).toContain("bg-danger");
    expect(hlSpan!.style.color).toBe("var(--cg-syntax-variable)");
  });

  it("handles highlight spanning across multiple tokens", () => {
    // "ab cd" where tokens are ["ab", " cd"] and highlight is [1..4] covering "b c"
    const tokens: HighlightToken[] = [
      { content: "ab", category: "keyword" },
      { content: " cd", category: "variable" },
    ];
    const highlights: InlineHighlight[] = [{ start: 1, length: 3 }];
    const { container } = render(
      <SyntaxHighlightedLine tokens={tokens} highlights={highlights} kind="addition" />,
    );
    expect(container.textContent).toBe("ab cd");
    const spans = container.querySelectorAll("span");
    // "a" (plain) | "b" (hl, keyword) | " c" (hl, variable) | "d" (plain)
    expect(spans).toHaveLength(4);
    const hlSpans = Array.from(spans).filter((s) => s.className.includes("bg-success"));
    expect(hlSpans).toHaveLength(2);
    expect(hlSpans[0].textContent).toBe("b");
    expect(hlSpans[1].textContent).toBe(" c");
  });
});
