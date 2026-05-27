import type { ThemedToken } from "shiki";
import type { FileDiff, DiffLine } from "../../../ipc/bindings";
import type { TokenizedLine } from "../hooks/useHighlightedLines";

/** Fixed row height for virtualized diff lines. */
export const ROW_HEIGHT = 24;
/** Number of extra rows rendered above/below the visible viewport. */
export const OVERSCAN = 20;

/** Render a single line of tokens. */
export function TokenLine({ tokens }: { tokens: ThemedToken[] }) {
  if (tokens.length === 0) return <>{"\n"}</>;
  return (
    <>
      {tokens.map((token, j) => (
        <span key={j} style={{ color: token.color }}>
          {token.content}
        </span>
      ))}
    </>
  );
}

/** Unified view row type. */
export interface UnifiedRow {
  kind: "context" | "addition" | "deletion" | "hunk-header";
  content: string;
  oldLineno: number | null;
  newLineno: number | null;
  /** Index into the tokenized lines array for the relevant content. */
  tokenLineIndex: number | null;
  /** The hunk index this row belongs to. */
  hunkIndex: number;
  /** The line index within the hunk (only for non-header rows). */
  lineIndex: number | null;
  /** Index of the first line in the current consecutive change group. */
  groupStartRow: number | null;
  /** Index of the last line in the current consecutive change group. */
  groupEndRow: number | null;
  /** Global line index for search matching (excluding hunk headers) */
  searchLineIndex: number | null;
}

/**
 * Build a flat array of display rows from the diff hunks.
 * Each row maps to a line in the unified view with old/new line numbers.
 */
export function buildUnifiedRows(diff: FileDiff): UnifiedRow[] {
  const rows: UnifiedRow[] = [];
  let searchLineCount = 0; // Track line index for search

  for (let hunkIdx = 0; hunkIdx < diff.hunks.length; hunkIdx++) {
    const hunk = diff.hunks[hunkIdx];
    rows.push({
      kind: "hunk-header",
      content: hunk.header,
      oldLineno: null,
      newLineno: null,
      tokenLineIndex: null,
      hunkIndex: hunkIdx,
      lineIndex: null,
      groupStartRow: null,
      groupEndRow: null,
      searchLineIndex: null,
    });

    for (let lineIdx = 0; lineIdx < hunk.lines.length; lineIdx++) {
      const line = hunk.lines[lineIdx];
      const tokenIdx = findTokenLineIndex(line);
      rows.push({
        kind: line.kind === "Addition"
          ? "addition"
          : line.kind === "Deletion"
            ? "deletion"
            : "context",
        content: line.content,
        oldLineno: line.old_lineno,
        newLineno: line.new_lineno,
        tokenLineIndex: tokenIdx,
        hunkIndex: hunkIdx,
        lineIndex: lineIdx,
        groupStartRow: null,
        groupEndRow: null,
        searchLineIndex: searchLineCount,
      });
      searchLineCount++;
    }
  }

  // Compute consecutive change groups.
  let i = 0;
  while (i < rows.length) {
    if (rows[i].kind === "addition" || rows[i].kind === "deletion") {
      const start = i;
      while (
        i < rows.length &&
        (rows[i].kind === "addition" || rows[i].kind === "deletion")
      ) {
        i++;
      }
      const end = i - 1;
      for (let j = start; j <= end; j++) {
        rows[j].groupStartRow = start;
        rows[j].groupEndRow = end;
      }
    } else {
      i++;
    }
  }

  return rows;
}

/** Split view side type. */
export interface SplitSide {
  kind: "context" | "deletion" | "addition" | "empty" | "hunk-header";
  content: string;
  lineno: number | null;
  tokenLineIndex: number | null;
  hunkIndex: number;
  lineIndex: number | null;
  groupStartRow: number | null;
  groupEndRow: number | null;
  searchLineIndex: number | null;
}

/** Split view row type (left and right side). */
export interface SplitRow {
  left: SplitSide;
  right: SplitSide;
}

/**
 * Build a paired array of rows for split-pane view.
 * Left side shows deletions, right side shows additions.
 */
export function buildSplitRows(diff: FileDiff): SplitRow[] {
  const rows: SplitRow[] = [];
  let searchLineCount = 0; // Track line index for search

  for (let hunkIdx = 0; hunkIdx < diff.hunks.length; hunkIdx++) {
    const hunk = diff.hunks[hunkIdx];
    rows.push({
      left: {
        kind: "hunk-header",
        content: hunk.header,
        lineno: null,
        tokenLineIndex: null,
        hunkIndex: hunkIdx,
        lineIndex: null,
        groupStartRow: null,
        groupEndRow: null,
        searchLineIndex: null,
      },
      right: {
        kind: "hunk-header",
        content: hunk.header,
        lineno: null,
        tokenLineIndex: null,
        hunkIndex: hunkIdx,
        lineIndex: null,
        groupStartRow: null,
        groupEndRow: null,
        searchLineIndex: null,
      },
    });

    // Collect consecutive deletion/addition blocks to pair them side-by-side.
    const lines = hunk.lines;
    let li = 0;

    while (li < lines.length) {
      const line = lines[li];

      if (line.kind === "Context") {
        const tokenIdx = line.new_lineno !== null ? line.new_lineno - 1 : null;
        rows.push({
          left: {
            kind: "context",
            content: line.content,
            lineno: line.old_lineno,
            tokenLineIndex: tokenIdx,
            hunkIndex: hunkIdx,
            lineIndex: li,
            groupStartRow: null,
            groupEndRow: null,
            searchLineIndex: searchLineCount,
          },
          right: {
            kind: "context",
            content: line.content,
            lineno: line.new_lineno,
            tokenLineIndex: tokenIdx,
            hunkIndex: hunkIdx,
            lineIndex: li,
            groupStartRow: null,
            groupEndRow: null,
            searchLineIndex: searchLineCount,
          },
        });
        searchLineCount++;
        li++;
        continue;
      }

      // Gather consecutive deletions, then additions.
      const deletionIndices: number[] = [];
      const additionIndices: number[] = [];

      while (li < lines.length && lines[li].kind === "Deletion") {
        deletionIndices.push(li);
        li++;
      }
      while (li < lines.length && lines[li].kind === "Addition") {
        additionIndices.push(li);
        li++;
      }

      const maxLen = Math.max(deletionIndices.length, additionIndices.length);
      const groupStartRow = rows.length;
      const groupEndRow = rows.length + maxLen - 1;

      for (let j = 0; j < maxLen; j++) {
        const delIdx = j < deletionIndices.length ? deletionIndices[j] : null;
        const addIdx = j < additionIndices.length ? additionIndices[j] : null;
        const del = delIdx !== null ? lines[delIdx] : null;
        const add = addIdx !== null ? lines[addIdx] : null;

        // Left side group bounds only if there are deletions in this group
        const leftGroupStart = deletionIndices.length > 0 ? groupStartRow : null;
        const leftGroupEnd = deletionIndices.length > 0 ? groupEndRow : null;
        // Right side group bounds only if there are additions in this group
        const rightGroupStart = additionIndices.length > 0 ? groupStartRow : null;
        const rightGroupEnd = additionIndices.length > 0 ? groupEndRow : null;

        // Keep split rows aligned with flattened search indexing (deletions then additions).
        const leftSearchLineIndex = del ? searchLineCount++ : null;
        const rightSearchLineIndex = add ? searchLineCount++ : null;

        rows.push({
          left: del
            ? {
                kind: "deletion",
                content: del.content,
                lineno: del.old_lineno,
                tokenLineIndex: null,
                hunkIndex: hunkIdx,
                lineIndex: delIdx,
                groupStartRow: leftGroupStart,
                groupEndRow: leftGroupEnd,
                searchLineIndex: leftSearchLineIndex,
              }
            : {
                kind: "empty",
                content: "",
                lineno: null,
                tokenLineIndex: null,
                hunkIndex: hunkIdx,
                lineIndex: null,
                groupStartRow: null,
                groupEndRow: null,
                searchLineIndex: null,
              },
          right: add
            ? {
                kind: "addition",
                content: add.content,
                lineno: add.new_lineno,
                tokenLineIndex: add.new_lineno !== null ? add.new_lineno - 1 : null,
                hunkIndex: hunkIdx,
                lineIndex: addIdx,
                groupStartRow: rightGroupStart,
                groupEndRow: rightGroupEnd,
                searchLineIndex: rightSearchLineIndex,
              }
            : {
                kind: "empty",
                content: "",
                lineno: null,
                tokenLineIndex: null,
                hunkIndex: hunkIdx,
                lineIndex: null,
                groupStartRow: null,
                groupEndRow: null,
                searchLineIndex: null,
              },
        });
      }
    }
  }

  return rows;
}

/**
 * Map a diff line to its position in the tokenized content array.
 * The tokenized content always represents the "new" (current) file,
 * so only additions and context lines can be mapped. Deletion lines
 * must fall back to rendering their raw diff content.
 */
export function findTokenLineIndex(line: DiffLine): number | null {
  // Additions and context lines have new_lineno → maps to current file.
  if (line.kind !== "Deletion" && line.new_lineno !== null) return line.new_lineno - 1;
  return null;
}
