import type { HighlightToken } from "../hooks/useHighlightedLines";
import { syntaxColor } from "../hooks/useHighlightedLines";
import type { FileDiff, DiffLine, InlineHighlight } from "../../../ipc/bindings";

/**
 * Reconstruct the old file content from the new file content and diff hunks.
 * This allows tokenizing the old file for syntax-highlighted deletion lines.
 */
export function reconstructOldContent(newContent: string, diff: FileDiff): string {
  const newLines = newContent.split("\n");
  const oldLines: string[] = [];
  let newPos = 0;

  for (const hunk of diff.hunks) {
    const hunkNewStart = hunk.new_start - 1;

    while (newPos < hunkNewStart) {
      oldLines.push(newLines[newPos]);
      newPos++;
    }

    for (const line of hunk.lines) {
      if (line.kind === "Context") {
        oldLines.push(line.content);
        newPos++;
      } else if (line.kind === "Deletion") {
        oldLines.push(line.content);
      } else {
        newPos++;
      }
    }
  }

  while (newPos < newLines.length) {
    oldLines.push(newLines[newPos]);
    newPos++;
  }

  return oldLines.join("\n");
}

/** Fixed row height for virtualized diff lines. */
export const ROW_HEIGHT = 24;
/** Number of extra rows rendered above/below the visible viewport. */
export const OVERSCAN = 20;

/** Render a single line of tokens. */
export function TokenLine({ tokens }: { tokens: HighlightToken[] }) {
  if (tokens.length === 0) return <>{"\n"}</>;
  return (
    <>
      {tokens.map((token, j) => (
        <span key={j} style={{ color: syntaxColor(token.category) }}>
          {token.content}
        </span>
      ))}
    </>
  );
}

/**
 * Render a syntax-highlighted line with inline change highlights overlaid.
 * Splits syntax tokens at highlight boundaries so both syntax colors and
 * change-highlight backgrounds are preserved.
 */
export function SyntaxHighlightedLine({
  tokens,
  highlights,
  kind,
}: {
  tokens: HighlightToken[];
  highlights: InlineHighlight[];
  kind: "addition" | "deletion";
}) {
  if (highlights.length === 0) return <TokenLine tokens={tokens} />;
  if (tokens.length === 0) {
    return <InlineHighlightedLine content="" highlights={highlights} kind={kind} />;
  }

  const hlClass = kind === "deletion" ? "bg-danger/35" : "bg-success/35";

  // Build a set of character ranges that should be highlighted.
  const hlRanges: { start: number; end: number }[] = highlights.map((h) => ({
    start: h.start,
    end: h.start + h.length,
  }));

  const parts: React.ReactNode[] = [];
  let charPos = 0;
  let hlIdx = 0;
  let keyIdx = 0;

  for (const token of tokens) {
    const tokenStart = charPos;
    const tokenEnd = charPos + token.content.length;
    let pos = tokenStart;

    while (pos < tokenEnd) {
      // Advance past highlights that end before current position.
      while (hlIdx < hlRanges.length && hlRanges[hlIdx].end <= pos) {
        hlIdx++;
      }

      const hl = hlIdx < hlRanges.length ? hlRanges[hlIdx] : null;

      if (hl && hl.start <= pos) {
        // Inside a highlight range.
        const sliceEnd = Math.min(tokenEnd, hl.end);
        parts.push(
          <span key={keyIdx++} className={hlClass} style={{ color: syntaxColor(token.category) }}>
            {token.content.substring(pos - tokenStart, sliceEnd - tokenStart)}
          </span>,
        );
        pos = sliceEnd;
      } else {
        // Outside any highlight range — plain syntax span.
        const sliceEnd = hl ? Math.min(tokenEnd, hl.start) : tokenEnd;
        parts.push(
          <span key={keyIdx++} style={{ color: syntaxColor(token.category) }}>
            {token.content.substring(pos - tokenStart, sliceEnd - tokenStart)}
          </span>,
        );
        pos = sliceEnd;
      }
    }

    charPos = tokenEnd;
  }

  return <>{parts}</>;
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
  /** Inline change highlights within the line content. */
  highlights: InlineHighlight[];
}

/**
 * Build a flat array of display rows from the diff hunks.
 * Each row maps to a line in the unified view with old/new line numbers.
 */
export function buildUnifiedRows(diff: FileDiff, newLineCount?: number): UnifiedRow[] {
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
      highlights: [],
    });

    for (let lineIdx = 0; lineIdx < hunk.lines.length; lineIdx++) {
      const line = hunk.lines[lineIdx];
      const tokenIdx = findTokenLineIndex(line, newLineCount);
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
        highlights: line.highlights,
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
  /** Inline change highlights within the line content. */
  highlights: InlineHighlight[];
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
export function buildSplitRows(diff: FileDiff, newLineCount?: number): SplitRow[] {
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
        highlights: [],
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
        highlights: [],
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
            highlights: [],
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
            highlights: [],
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
                tokenLineIndex: newLineCount !== undefined && del.old_lineno !== null
                  ? newLineCount + del.old_lineno - 1
                  : null,
                hunkIndex: hunkIdx,
                lineIndex: delIdx,
                groupStartRow: leftGroupStart,
                groupEndRow: leftGroupEnd,
                searchLineIndex: leftSearchLineIndex,
                highlights: del.highlights,
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
                highlights: [],
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
                highlights: add.highlights,
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
                highlights: [],
              },
        });
      }
    }
  }

  return rows;
}

/**
 * Map a diff line to its position in the tokenized content array.
 * When newLineCount is provided, deletion lines are mapped to old-file
 * tokens located at offset newLineCount in the merged token array.
 */
export function findTokenLineIndex(line: DiffLine, newLineCount?: number): number | null {
  if (line.kind !== "Deletion" && line.new_lineno !== null) return line.new_lineno - 1;
  if (newLineCount !== undefined && line.kind === "Deletion" && line.old_lineno !== null) {
    return newLineCount + line.old_lineno - 1;
  }
  return null;
}

/**
 * Render line content with inline change highlights.
 * Splits the content into highlighted (changed) and non-highlighted spans.
 */
export function InlineHighlightedLine({
  content,
  highlights,
  kind,
}: {
  content: string;
  highlights: InlineHighlight[];
  kind: "addition" | "deletion";
}) {
  if (highlights.length === 0) return <>{content}</>;

  const hlClass = kind === "deletion"
    ? "bg-danger/35"
    : "bg-success/35";

  const parts: React.ReactNode[] = [];
  let lastEnd = 0;

  for (let i = 0; i < highlights.length; i++) {
    const h = highlights[i];
    if (h.start > lastEnd) {
      parts.push(content.substring(lastEnd, h.start));
    }
    parts.push(
      <span key={i} className={hlClass}>
        {content.substring(h.start, h.start + h.length)}
      </span>,
    );
    lastEnd = h.start + h.length;
  }

  if (lastEnd < content.length) {
    parts.push(content.substring(lastEnd));
  }

  return <>{parts}</>;
}
