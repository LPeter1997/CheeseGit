import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen, fireEvent, act } from "@testing-library/react";
import type { FileDiff, LineSelection } from "../../../ipc/bindings";

// Mock the highlighting hook to return tokens synchronously.
vi.mock("../hooks/useHighlightedLines", () => ({
  useHighlightedLines: (_filePath: string, content: string) => ({
    lines: content.split("\n").map((line: string) => ({
      tokens: [{ content: line, color: "" }],
    })),
    bg: "#fff",
  }),
}));

import { FileViewer } from "./FileViewer";

/** Flush all pending microtasks/promises. */
async function flushPromises() {
  await act(async () => {
    await new Promise((resolve) => setTimeout(resolve, 0));
  });
}

// ── Test data ───────────────────────────────────────────────────

function makeDiff(): FileDiff {
  return {
    path: "test.ts",
    hunks: [
      {
        header: "@@ -1,4 +1,6 @@",
        old_start: 1,
        new_start: 1,
        lines: [
          { kind: "Context", content: "line1", old_lineno: 1, new_lineno: 1, highlights: [] },
          { kind: "Deletion", content: "old2", old_lineno: 2, new_lineno: null, highlights: [] },
          { kind: "Deletion", content: "old3", old_lineno: 3, new_lineno: null, highlights: [] },
          { kind: "Addition", content: "new2", old_lineno: null, new_lineno: 2, highlights: [] },
          { kind: "Addition", content: "new3", old_lineno: null, new_lineno: 3, highlights: [] },
          { kind: "Addition", content: "new4", old_lineno: null, new_lineno: 4, highlights: [] },
          { kind: "Context", content: "line4", old_lineno: 4, new_lineno: 5, highlights: [] },
        ],
      },
    ],
  };
}

const fileContent = "line1\nnew2\nnew3\nnew4\nline4\n";

// ── Unified view tests ──────────────────────────────────────────

describe("FileViewer unified staging", () => {
  let onStageLines: ReturnType<typeof vi.fn<(selections: LineSelection[]) => void>>;

  beforeEach(() => {
    onStageLines = vi.fn();
  });

  it("calls onStageLines with hunk selections when hunk button clicked", async () => {
    render(
      <FileViewer
        filePath="test.ts"
        content={fileContent}
        diff={makeDiff()}
        viewMode="unified"
        onStageLines={onStageLines}
      />,
    );

    await flushPromises();

    const hunkBtn = screen.getAllByTitle("Stage hunk")[0];
    fireEvent.click(hunkBtn);

    expect(onStageLines).toHaveBeenCalledTimes(1);
    const selections: LineSelection[] = onStageLines.mock.calls[0][0];
    // Should include all change lines in the hunk (indices 1-5: 2 dels + 3 adds).
    expect(selections).toHaveLength(5);
    expect(selections).toContainEqual({ hunk_index: 0, line_index: 1 });
    expect(selections).toContainEqual({ hunk_index: 0, line_index: 4 });
  });

  it("calls onStageLines with single line selection when line button clicked", async () => {
    render(
      <FileViewer
        filePath="test.ts"
        content={fileContent}
        diff={makeDiff()}
        viewMode="unified"
        onStageLines={onStageLines}
      />,
    );

    await flushPromises();

    const lineButtons = screen.getAllByTitle("Stage line");
    fireEvent.click(lineButtons[0]);

    expect(onStageLines).toHaveBeenCalledTimes(1);
    const selections: LineSelection[] = onStageLines.mock.calls[0][0];
    expect(selections).toHaveLength(1);
    expect(selections[0].hunk_index).toBe(0);
  });

  it("calls onStageLines with group selections when group button clicked", async () => {
    render(
      <FileViewer
        filePath="test.ts"
        content={fileContent}
        diff={makeDiff()}
        viewMode="unified"
        onStageLines={onStageLines}
      />,
    );

    await flushPromises();

    const groupButtons = screen.getAllByTitle("Stage group");
    fireEvent.click(groupButtons[0]);

    expect(onStageLines).toHaveBeenCalledTimes(1);
    const selections: LineSelection[] = onStageLines.mock.calls[0][0];
    // The group is the consecutive block of changes (2 dels + 3 adds = 5 lines).
    expect(selections.length).toBeGreaterThan(1);
    expect(selections.every((s) => s.hunk_index === 0)).toBe(true);
  });

  it("calls onUnstageLines when configured for unstaging", async () => {
    const onUnstageLines = vi.fn();

    render(
      <FileViewer
        filePath="test.ts"
        content={fileContent}
        diff={makeDiff()}
        viewMode="unified"
        onUnstageLines={onUnstageLines}
      />,
    );

    await flushPromises();

    fireEvent.click(screen.getAllByTitle("Unstage hunk")[0]);
    expect(onUnstageLines).toHaveBeenCalledTimes(1);

    fireEvent.click(screen.getAllByTitle("Unstage line")[0]);
    expect(onUnstageLines).toHaveBeenCalledTimes(2);
  });
});

// ── Split view tests ────────────────────────────────────────────

describe("FileViewer split staging", () => {
  let onStageLines: ReturnType<typeof vi.fn<(selections: LineSelection[]) => void>>;

  beforeEach(() => {
    onStageLines = vi.fn();
  });

  it("left side hunk button stages only deletions", async () => {
    render(
      <FileViewer
        filePath="test.ts"
        content={fileContent}
        diff={makeDiff()}
        viewMode="split"
        onStageLines={onStageLines}
      />,
    );

    await flushPromises();

    // In split view, there are hunk buttons on both sides.
    // The first "Stage hunk" should be the left side (deletions).
    const hunkButtons = screen.getAllByTitle("Stage hunk");
    fireEvent.click(hunkButtons[0]);

    expect(onStageLines).toHaveBeenCalledTimes(1);
    const selections: LineSelection[] = onStageLines.mock.calls[0][0];
    // Left side should only contain deletions (line indices 1 and 2).
    expect(selections).toHaveLength(2);
    selections.forEach((s) => {
      const line = makeDiff().hunks[0].lines[s.line_index];
      expect(line.kind).toBe("Deletion");
    });
  });

  it("right side hunk button stages only additions", async () => {
    render(
      <FileViewer
        filePath="test.ts"
        content={fileContent}
        diff={makeDiff()}
        viewMode="split"
        onStageLines={onStageLines}
      />,
    );

    await flushPromises();

    const hunkButtons = screen.getAllByTitle("Stage hunk");
    // The second hunk button should be the right side (additions).
    fireEvent.click(hunkButtons[1]);

    expect(onStageLines).toHaveBeenCalledTimes(1);
    const selections: LineSelection[] = onStageLines.mock.calls[0][0];
    // Right side should only contain additions (line indices 3, 4, 5).
    expect(selections).toHaveLength(3);
    selections.forEach((s) => {
      const line = makeDiff().hunks[0].lines[s.line_index];
      expect(line.kind).toBe("Addition");
    });
  });

  it("line button stages only that single line", async () => {
    render(
      <FileViewer
        filePath="test.ts"
        content={fileContent}
        diff={makeDiff()}
        viewMode="split"
        onStageLines={onStageLines}
      />,
    );

    await flushPromises();

    const lineButtons = screen.getAllByTitle("Stage line");
    fireEvent.click(lineButtons[0]);

    expect(onStageLines).toHaveBeenCalledTimes(1);
    const selections: LineSelection[] = onStageLines.mock.calls[0][0];
    expect(selections).toHaveLength(1);
  });

  it("group button stages correct subset of changes per side", async () => {
    render(
      <FileViewer
        filePath="test.ts"
        content={fileContent}
        diff={makeDiff()}
        viewMode="split"
        onStageLines={onStageLines}
      />,
    );

    await flushPromises();

    const groupButtons = screen.getAllByTitle("Stage group");
    // First group button is on the left side (deletions group).
    fireEvent.click(groupButtons[0]);

    expect(onStageLines).toHaveBeenCalledTimes(1);
    const selections: LineSelection[] = onStageLines.mock.calls[0][0];
    // Should only be deletions from the left side.
    expect(selections.length).toBeGreaterThan(0);
    selections.forEach((s) => {
      const line = makeDiff().hunks[0].lines[s.line_index];
      expect(line.kind).toBe("Deletion");
    });
  });

  it("unstage operations work in split view", async () => {
    const onUnstageLines = vi.fn();

    render(
      <FileViewer
        filePath="test.ts"
        content={fileContent}
        diff={makeDiff()}
        viewMode="split"
        onUnstageLines={onUnstageLines}
      />,
    );

    await flushPromises();

    fireEvent.click(screen.getAllByTitle("Unstage line")[0]);
    expect(onUnstageLines).toHaveBeenCalledTimes(1);

    const selections: LineSelection[] = onUnstageLines.mock.calls[0][0];
    expect(selections).toHaveLength(1);
  });
});

// ── Content correctness tests ───────────────────────────────────

describe("FileViewer displays correct content for deletions", () => {
  it("deletion lines show old content, not tokenized new file content (unified)", async () => {
    render(
      <FileViewer
        filePath="test.ts"
        content={fileContent}
        diff={makeDiff()}
        viewMode="unified"
      />,
    );

    await flushPromises();

    // The deletion lines have content "old2" and "old3".
    // They should be visible — NOT replaced by "new2"/"new3" from the tokenized file.
    expect(screen.getByText("old2")).toBeInTheDocument();
    expect(screen.getByText("old3")).toBeInTheDocument();
  });

  it("deletion lines show old content, not tokenized new file content (split)", async () => {
    render(
      <FileViewer
        filePath="test.ts"
        content={fileContent}
        diff={makeDiff()}
        viewMode="split"
      />,
    );

    await flushPromises();

    // The deletion lines have content "old2" and "old3".
    expect(screen.getByText("old2")).toBeInTheDocument();
    expect(screen.getByText("old3")).toBeInTheDocument();
  });
});
