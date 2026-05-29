import { describe, it, expect } from "vitest";
import { render } from "@testing-library/react";
import type { FileDiff, InlineHighlight } from "../../../ipc/bindings";
import { InlineHighlightedLine, buildUnifiedRows, buildSplitRows } from "./DiffViewShared";

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
