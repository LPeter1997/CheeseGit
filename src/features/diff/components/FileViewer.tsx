import { useEffect, useState, useMemo, useRef, useCallback, useLayoutEffect } from "react";
import type { ThemedToken } from "shiki";
import type { FileDiff, DiffLine } from "../../../ipc/bindings";
import type { DiffViewMode } from "../store";
import { useHighlightedLines, type TokenizedLine } from "../hooks/useHighlightedLines";
import { SmartPath } from "../../../shared/components/SmartPath";
import { useShiftKey } from "../../../shared/hooks/useShiftKey";

/** Fixed row height for virtualized diff lines. */
const ROW_HEIGHT = 24;
/** Number of extra rows rendered above/below the visible viewport. */
const OVERSCAN = 20;

/** Render a single line of tokens. */
function TokenLine({ tokens }: { tokens: ThemedToken[] }) {
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

// ── Unified View ────────────────────────────────────────────────

/**
 * Build a flat array of display rows from the diff hunks.
 * Each row maps to a line in the unified view with old/new line numbers.
 */
interface UnifiedRow {
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
}

function buildUnifiedRows(
  diff: FileDiff,
): UnifiedRow[] {
  const rows: UnifiedRow[] = [];

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
      });
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

/** Map a diff line to its position in the tokenized content array.
 *  The tokenized content always represents the "new" (current) file,
 *  so only additions and context lines can be mapped. Deletion lines
 *  must fall back to rendering their raw diff content.
 */
function findTokenLineIndex(line: DiffLine): number | null {
  // Additions and context lines have new_lineno → maps to current file.
  if (line.kind !== "Deletion" && line.new_lineno !== null) return line.new_lineno - 1;
  return null;
}

function UnifiedDiffView({
  diff,
  filePath,
  tokenizedLines,
  bg,
  onStageLines,
  onUnstageLines,
  onDiscardLines,
}: {
  diff: FileDiff;
  filePath: string;
  tokenizedLines: TokenizedLine[];
  bg: string | undefined;
  onStageLines?: (selections: LineSelection[]) => void;
  onUnstageLines?: (selections: LineSelection[]) => void;
  onDiscardLines?: (selections: LineSelection[]) => void;
}) {
  const rows = useMemo(
    () => buildUnifiedRows(diff),
    [diff],
  );
  const [hoveredGroup, setHoveredGroup] = useState<{ start: number; end: number } | null>(null);
  const [hoveredLine, setHoveredLine] = useState<number | null>(null);
  const [hoveredHunk, setHoveredHunk] = useState<number | null>(null);

  const shiftHeld = useShiftKey();
  const discardMode = shiftHeld && !!onDiscardLines;
  const stageAction = onStageLines ?? onUnstageLines;
  const isInteractive = !!stageAction;

  // ── Virtualization ──────────────────────────────────────────────
  const scrollRef = useRef<HTMLDivElement>(null);
  const [scrollTop, setScrollTop] = useState(0);
  const [viewportHeight, setViewportHeight] = useState(0);

  const onScroll = useCallback(() => {
    if (scrollRef.current) setScrollTop(scrollRef.current.scrollTop);
  }, []);

  const refCallback = useCallback((el: HTMLDivElement | null) => {
    (scrollRef as React.MutableRefObject<HTMLDivElement | null>).current = el;
    if (el) setViewportHeight(el.clientHeight);
  }, []);

  // Reset scroll when the user switches to a different file.
  // We intentionally do NOT depend on `diff` here — a refresh of
  // the same file (e.g., from the file watcher) should preserve
  // the current scroll position.
  useLayoutEffect(() => {
    if (scrollRef.current) scrollRef.current.scrollTop = 0;
    setScrollTop(0);
  }, [filePath]);

  const startRow = Math.max(0, Math.floor(scrollTop / ROW_HEIGHT) - OVERSCAN);
  const endRow = Math.min(rows.length, Math.ceil((scrollTop + viewportHeight) / ROW_HEIGHT) + OVERSCAN);
  // ────────────────────────────────────────────────────────────────

  const maxOld = rows.reduce(
    (m, r) => Math.max(m, r.oldLineno ?? 0),
    0,
  );
  const maxNew = rows.reduce(
    (m, r) => Math.max(m, r.newLineno ?? 0),
    0,
  );
  const gutterW = Math.max(String(Math.max(maxOld, maxNew)).length, 4);

  function getHunkSelections(hunkIndex: number): LineSelection[] {
    const hunk = diff.hunks[hunkIndex];
    const sels: LineSelection[] = [];
    for (let li = 0; li < hunk.lines.length; li++) {
      if (hunk.lines[li].kind !== "Context") {
        sels.push({ hunk_index: hunkIndex, line_index: li });
      }
    }
    return sels;
  }

  function getGroupSelections(startRow: number, endRow: number): LineSelection[] {
    const sels: LineSelection[] = [];
    for (let r = startRow; r <= endRow; r++) {
      const row = rows[r];
      if (row.lineIndex !== null && row.kind !== "context") {
        sels.push({ hunk_index: row.hunkIndex, line_index: row.lineIndex });
      }
    }
    return sels;
  }

  function getLineSelection(row: UnifiedRow): LineSelection[] {
    if (row.lineIndex === null) return [];
    return [{ hunk_index: row.hunkIndex, line_index: row.lineIndex }];
  }

  function isHighlighted(rowIdx: number, row: UnifiedRow): boolean {
    if (!isInteractive) return false;
    if (row.kind === "context" || row.kind === "hunk-header") return false;

    if (hoveredHunk !== null && row.hunkIndex === hoveredHunk) return true;
    if (hoveredGroup && rowIdx >= hoveredGroup.start && rowIdx <= hoveredGroup.end) return true;
    if (hoveredLine === rowIdx) return true;
    return false;
  }

  return (
    <div
      ref={refCallback}
      onScroll={onScroll}
      className="flex-1 overflow-auto text-sm leading-relaxed"
      style={{ backgroundColor: bg, willChange: "transform" }}
    >
      <table className="w-full border-collapse font-mono">
        <tbody>
          {startRow > 0 && (
            <tr><td style={{ height: startRow * ROW_HEIGHT, padding: 0 }} /></tr>
          )}
          {rows.slice(startRow, endRow).map((row, offset) => {
            const i = startRow + offset;
            if (row.kind === "hunk-header") {
              return (
                <tr
                  key={i}
                  style={{ height: ROW_HEIGHT }}
                  className={`leading-relaxed ${
                    hoveredHunk === row.hunkIndex ? "bg-accent/20" : "bg-accent/10"
                  }`}
                >
                  {isInteractive && (
                    <td
                      className={`select-none w-6 text-center align-middle cursor-pointer text-fg-muted ${
                        discardMode ? "hover:text-danger" : "hover:text-fg"
                      }`}
                      onMouseEnter={() => setHoveredHunk(row.hunkIndex)}
                      onMouseLeave={() => setHoveredHunk(null)}
                      onClick={() => {
                        const sels = getHunkSelections(row.hunkIndex);
                        if (discardMode) onDiscardLines!(sels);
                        else stageAction?.(sels);
                      }}
                      title={discardMode ? "Discard hunk" : (onStageLines ? "Stage hunk" : "Unstage hunk")}
                    >
                      {discardMode ? "✕" : (onStageLines ? "↓" : "↑")}
                    </td>
                  )}
                  <td
                    colSpan={isInteractive ? 3 : 3}
                    className="select-none px-3 py-0.5 text-xs text-fg-muted"
                  >
                    {row.content}
                  </td>
                </tr>
              );
            }

            const highlighted = isHighlighted(i, row);
            const bgClass = highlighted
              ? "bg-accent/20"
              : row.kind === "addition"
                ? "bg-success/15"
                : row.kind === "deletion"
                  ? "bg-danger/15"
                  : "";

            const tokens =
              row.tokenLineIndex !== null &&
              row.tokenLineIndex < tokenizedLines.length
                ? tokenizedLines[row.tokenLineIndex]
                : null;

            const isChange = row.kind === "addition" || row.kind === "deletion";

            return (
              <tr key={i} style={{ height: ROW_HEIGHT }} className={`${bgClass} leading-relaxed`}>
                {isInteractive && (
                  <td className="select-none w-6 align-middle">
                    {isChange && (
                      <div className="flex">
                        {/* Outer gutter: group action (hidden for single-line groups) */}
                        {row.groupStartRow !== null && row.groupEndRow !== null && row.groupStartRow !== row.groupEndRow ? (
                          <span
                            className={`flex-1 cursor-pointer text-center text-fg-muted ${
                              discardMode ? "hover:text-danger" : "hover:text-fg"
                            } ${
                              hoveredGroup && row.groupStartRow === hoveredGroup.start
                                ? "opacity-100"
                                : "opacity-0 hover:opacity-100"
                            }`}
                            onMouseEnter={() =>
                              setHoveredGroup({ start: row.groupStartRow!, end: row.groupEndRow! })
                            }
                            onMouseLeave={() => setHoveredGroup(null)}
                            onClick={() => {
                              const sels = getGroupSelections(row.groupStartRow!, row.groupEndRow!);
                              if (discardMode) onDiscardLines!(sels);
                              else stageAction?.(sels);
                            }}
                            title={discardMode ? "Discard group" : (onStageLines ? "Stage group" : "Unstage group")}
                          >
                            {i === Math.floor((row.groupStartRow! + row.groupEndRow!) / 2)
                              ? (discardMode ? "✕" : (onStageLines ? "›" : "‹"))
                              : "\u00a0"}
                          </span>
                        ) : (
                          <span className="flex-1" />
                        )}
                        {/* Inner gutter: single line action */}
                        <span
                          className={`flex-1 cursor-pointer text-center text-fg-muted opacity-0 hover:opacity-100 ${
                            discardMode ? "hover:text-danger" : "hover:text-fg"
                          }`}
                          onMouseEnter={() => setHoveredLine(i)}
                          onMouseLeave={() => setHoveredLine(null)}
                          onClick={() => {
                            const sels = getLineSelection(row);
                            if (discardMode) onDiscardLines!(sels);
                            else stageAction?.(sels);
                          }}
                          title={discardMode ? "Discard line" : (onStageLines ? "Stage line" : "Unstage line")}
                        >
                          {discardMode ? "✕" : (onStageLines ? "+" : "−")}
                        </span>
                      </div>
                    )}
                  </td>
                )}
                <td
                  className="select-none px-1.5 text-right align-top text-fg-muted opacity-50"
                  style={{ width: `${gutterW + 1}ch` }}
                >
                  {row.oldLineno ?? ""}
                </td>
                <td
                  className="select-none px-1.5 text-right align-top text-fg-muted opacity-50"
                  style={{ width: `${gutterW + 1}ch` }}
                >
                  {row.newLineno ?? ""}
                </td>
                <td className="whitespace-pre pr-3 overflow-hidden">
                  {tokens ? (
                    <TokenLine tokens={tokens.tokens} />
                  ) : (
                    row.content
                  )}
                </td>
              </tr>
            );
          })}
          {endRow < rows.length && (
            <tr><td style={{ height: (rows.length - endRow) * ROW_HEIGHT, padding: 0 }} /></tr>
          )}
        </tbody>
      </table>
    </div>
  );
}

// ── Split View ──────────────────────────────────────────────────

interface SplitSide {
  kind: "context" | "deletion" | "addition" | "empty" | "hunk-header";
  content: string;
  lineno: number | null;
  tokenLineIndex: number | null;
  hunkIndex: number;
  lineIndex: number | null;
  groupStartRow: number | null;
  groupEndRow: number | null;
}

interface SplitRow {
  left: SplitSide;
  right: SplitSide;
}

function buildSplitRows(
  diff: FileDiff,
): SplitRow[] {
  const rows: SplitRow[] = [];

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
          },
        });
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
              },
          right: add
            ? {
                kind: "addition",
                content: add.content,
                lineno: add.new_lineno,
                tokenLineIndex:
                  add.new_lineno !== null ? add.new_lineno - 1 : null,
                hunkIndex: hunkIdx,
                lineIndex: addIdx,
                groupStartRow: rightGroupStart,
                groupEndRow: rightGroupEnd,
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
              },
        });
      }
    }
  }

  return rows;
}

function SplitDiffView({
  diff,
  filePath,
  tokenizedLines,
  bg,
  onStageLines,
  onUnstageLines,
  onDiscardLines,
}: {
  diff: FileDiff;
  filePath: string;
  tokenizedLines: TokenizedLine[];
  bg: string | undefined;
  onStageLines?: (selections: LineSelection[]) => void;
  onUnstageLines?: (selections: LineSelection[]) => void;
  onDiscardLines?: (selections: LineSelection[]) => void;
}) {
  const rows = useMemo(
    () => buildSplitRows(diff),
    [diff],
  );

  const shiftHeld = useShiftKey();
  const discardMode = shiftHeld && !!onDiscardLines;
  const stageAction = onStageLines ?? onUnstageLines;
  const isInteractive = !!stageAction;

  // Per-side hover state so highlighting doesn't leak across sides
  const [hoveredHunkLeft, setHoveredHunkLeft] = useState<number | null>(null);
  const [hoveredHunkRight, setHoveredHunkRight] = useState<number | null>(null);
  const [hoveredGroupLeft, setHoveredGroupLeft] = useState<{ start: number; end: number } | null>(null);
  const [hoveredGroupRight, setHoveredGroupRight] = useState<{ start: number; end: number } | null>(null);
  const [hoveredLineLeft, setHoveredLineLeft] = useState<number | null>(null);
  const [hoveredLineRight, setHoveredLineRight] = useState<number | null>(null);

  // ── Virtualization ──────────────────────────────────────────────
  const [scrollTop, setScrollTop] = useState(0);
  const [viewportHeight, setViewportHeight] = useState(0);

  const startRow = Math.max(0, Math.floor(scrollTop / ROW_HEIGHT) - OVERSCAN);
  const endRow = Math.min(rows.length, Math.ceil((scrollTop + viewportHeight) / ROW_HEIGHT) + OVERSCAN);

  // Reset scroll when the user switches to a different file.
  useLayoutEffect(() => {
    if (leftRef.current) leftRef.current.scrollTop = 0;
    if (rightRef.current) rightRef.current.scrollTop = 0;
    setScrollTop(0);
  }, [filePath]);

  // Measure viewport height.
  useEffect(() => {
    if (leftRef.current) setViewportHeight(leftRef.current.clientHeight);
  }, [filePath]);
  // ────────────────────────────────────────────────────────────────

  const maxLineno = rows.reduce(
    (m, r) =>
      Math.max(m, r.left.lineno ?? 0, r.right.lineno ?? 0),
    0,
  );
  const gutterW = `${Math.max(String(maxLineno).length, 4) + 1}ch`;

  const leftRef = useRef<HTMLDivElement | null>(null);
  const rightRef = useRef<HTMLDivElement | null>(null);
  const syncing = useRef(false);
  const [equalizedWidth, setEqualizedWidth] = useState<number | null>(null);

  useEffect(() => {
    const leftTable = leftRef.current?.querySelector("table");
    const rightTable = rightRef.current?.querySelector("table");
    if (!leftTable || !rightTable) return;

    leftTable.style.minWidth = "100%";
    rightTable.style.minWidth = "100%";

    const maxW = Math.max(leftTable.scrollWidth, rightTable.scrollWidth);
    setEqualizedWidth(maxW);
  }, [diff, rows]);

  const handleScroll = useCallback(
    (source: HTMLDivElement | null, target: HTMLDivElement | null) => {
      if (syncing.current || !source || !target) return;
      syncing.current = true;
      target.scrollTop = source.scrollTop;
      target.scrollLeft = source.scrollLeft;
      setScrollTop(source.scrollTop);
      requestAnimationFrame(() => {
        syncing.current = false;
      });
    },
    [],
  );

  const handleWheel = useCallback(
    (e: React.WheelEvent) => {
      const left = leftRef.current;
      const right = rightRef.current;
      if (!left || !right) return;

      // Prevent native scroll on individual panels; we drive both manually.
      syncing.current = true;
      left.scrollTop += e.deltaY;
      left.scrollLeft += e.deltaX;
      right.scrollTop = left.scrollTop;
      right.scrollLeft = left.scrollLeft;
      setScrollTop(left.scrollTop);
      requestAnimationFrame(() => {
        syncing.current = false;
      });
    },
    [],
  );

  function getHunkSelectionsForSide(hunkIndex: number, side: "left" | "right"): LineSelection[] {
    const hunk = diff.hunks[hunkIndex];
    const sels: LineSelection[] = [];
    for (let li = 0; li < hunk.lines.length; li++) {
      const line = hunk.lines[li];
      if (side === "left" && line.kind === "Deletion") {
        sels.push({ hunk_index: hunkIndex, line_index: li });
      } else if (side === "right" && line.kind === "Addition") {
        sels.push({ hunk_index: hunkIndex, line_index: li });
      }
    }
    return sels;
  }

  function getGroupSelectionsForSide(startRow: number, endRow: number, side: "left" | "right"): LineSelection[] {
    const sels: LineSelection[] = [];
    for (let r = startRow; r <= endRow; r++) {
      const cell = side === "left" ? rows[r].left : rows[r].right;
      if (cell.lineIndex !== null) {
        const isRelevant = side === "left" ? cell.kind === "deletion" : cell.kind === "addition";
        if (isRelevant) {
          sels.push({ hunk_index: cell.hunkIndex, line_index: cell.lineIndex });
        }
      }
    }
    return sels;
  }

  function isCellHighlighted(cell: SplitSide, side: "left" | "right", rowIdx: number): boolean {
    if (!isInteractive) return false;
    if (cell.kind === "context" || cell.kind === "empty" || cell.kind === "hunk-header") return false;

    const hoveredHunk = side === "left" ? hoveredHunkLeft : hoveredHunkRight;
    if (hoveredHunk !== null && cell.hunkIndex === hoveredHunk) return true;

    const hoveredGroup = side === "left" ? hoveredGroupLeft : hoveredGroupRight;
    if (hoveredGroup && rowIdx >= hoveredGroup.start && rowIdx <= hoveredGroup.end) return true;

    const hoveredLine = side === "left" ? hoveredLineLeft : hoveredLineRight;
    if (hoveredLine === rowIdx) return true;

    return false;
  }

  function renderSide(
    side: "left" | "right",
    tokens: TokenizedLine[],
  ) {
    const tableStyle = equalizedWidth
      ? { minWidth: `${equalizedWidth}px` }
      : undefined;

    const setHoveredHunk = side === "left" ? setHoveredHunkLeft : setHoveredHunkRight;
    const setHoveredGroup = side === "left" ? setHoveredGroupLeft : setHoveredGroupRight;
    const setHoveredLine = side === "left" ? setHoveredLineLeft : setHoveredLineRight;
    const hoveredHunk = side === "left" ? hoveredHunkLeft : hoveredHunkRight;

    return (
      <table className="min-w-full border-collapse font-mono" style={tableStyle}>
        <colgroup>
          {isInteractive && <col style={{ width: "2rem" }} />}
          <col style={{ width: gutterW }} />
          <col />
        </colgroup>
        <tbody>
          {startRow > 0 && (
            <tr><td style={{ height: startRow * ROW_HEIGHT, padding: 0 }} /></tr>
          )}
          {rows.slice(startRow, endRow).map((row, offset) => {
            const i = startRow + offset;
            const cell = side === "left" ? row.left : row.right;
            const highlighted = isCellHighlighted(cell, side, i);
            const isChange = cell.kind === "addition" || cell.kind === "deletion";

            if (cell.kind === "hunk-header") {
              const hunkSels = getHunkSelectionsForSide(cell.hunkIndex, side);
              const hasRelevantChanges = hunkSels.length > 0;
              return (
                <tr key={i} style={{ height: ROW_HEIGHT }} className={`leading-relaxed ${hoveredHunk === cell.hunkIndex ? "bg-accent/20" : "bg-accent/10"}`}>
                  {isInteractive && (
                    <td
                      className={`select-none text-center align-middle text-fg-muted ${
                        hasRelevantChanges ? `cursor-pointer ${discardMode ? "hover:text-danger" : "hover:text-fg"}` : ""
                      }`}
                      style={{ width: "2rem" }}
                      onMouseEnter={() => hasRelevantChanges && setHoveredHunk(cell.hunkIndex)}
                      onMouseLeave={() => setHoveredHunk(null)}
                      onClick={() => {
                        if (hasRelevantChanges) {
                          if (discardMode) onDiscardLines!(hunkSels);
                          else stageAction?.(hunkSels);
                        }
                      }}
                      title={hasRelevantChanges ? (discardMode ? "Discard hunk" : (onStageLines ? "Stage hunk" : "Unstage hunk")) : undefined}
                    >
                      {hasRelevantChanges ? (discardMode ? "✕" : (onStageLines ? "↓" : "↑")) : "\u00a0"}
                    </td>
                  )}
                  <td
                    className="select-none px-1.5 text-right align-top text-fg-muted opacity-50"
                    style={{ width: gutterW }}
                  />
                  <td className="px-3 py-0.5 text-xs text-fg-muted">
                    <div className="whitespace-pre">{cell.content}</div>
                  </td>
                </tr>
              );
            }

            const bgClass = highlighted
              ? "bg-accent/20"
              : cell.kind === "addition"
                ? "bg-success/15"
                : cell.kind === "deletion"
                  ? "bg-danger/15"
                  : cell.kind === "empty"
                    ? "bg-bg-surface/50"
                    : "";

            const tokenLine =
              cell.tokenLineIndex !== null &&
              cell.tokenLineIndex < tokens.length
                ? tokens[cell.tokenLineIndex]
                : null;

            return (
              <tr key={i} style={{ height: ROW_HEIGHT }} className={`${bgClass} leading-relaxed`}>
                {isInteractive && (
                  <td
                    className="select-none align-middle"
                    style={{ width: "2rem" }}
                  >
                    {isChange && (
                      <div className="flex">
                        {/* Outer gutter: group action (hidden for single-line groups) */}
                        {cell.groupStartRow !== null && cell.groupEndRow !== null && cell.groupStartRow !== cell.groupEndRow ? (
                          <span
                            className={`flex-1 cursor-pointer text-center text-fg-muted ${
                              discardMode ? "hover:text-danger" : "hover:text-fg"
                            } ${
                              (side === "left" ? hoveredGroupLeft : hoveredGroupRight)?.start === cell.groupStartRow
                                ? "opacity-100"
                                : "opacity-0 hover:opacity-100"
                            }`}
                            onMouseEnter={() =>
                              setHoveredGroup({ start: cell.groupStartRow!, end: cell.groupEndRow! })
                            }
                            onMouseLeave={() => setHoveredGroup(null)}
                            onClick={() => {
                              const sels = getGroupSelectionsForSide(cell.groupStartRow!, cell.groupEndRow!, side);
                              if (sels.length > 0) {
                                if (discardMode) onDiscardLines!(sels);
                                else stageAction?.(sels);
                              }
                            }}
                            title={discardMode ? "Discard group" : (onStageLines ? "Stage group" : "Unstage group")}
                          >
                            {i === Math.floor((cell.groupStartRow! + cell.groupEndRow!) / 2)
                              ? (discardMode ? "✕" : (onStageLines ? "›" : "‹"))
                              : "\u00a0"}
                          </span>
                        ) : (
                          <span className="flex-1" />
                        )}
                        {/* Inner gutter: single line action */}
                        <span
                          className={`flex-1 cursor-pointer text-center text-fg-muted opacity-0 hover:opacity-100 ${
                            discardMode ? "hover:text-danger" : "hover:text-fg"
                          }`}
                          onMouseEnter={() => setHoveredLine(i)}
                          onMouseLeave={() => setHoveredLine(null)}
                          onClick={() => {
                            if (cell.lineIndex !== null) {
                              const sels = [{ hunk_index: cell.hunkIndex, line_index: cell.lineIndex }];
                              if (discardMode) onDiscardLines!(sels);
                              else stageAction?.(sels);
                            }
                          }}
                          title={discardMode ? "Discard line" : (onStageLines ? "Stage line" : "Unstage line")}
                        >
                          {discardMode ? "✕" : (onStageLines ? "+" : "−")}
                        </span>
                      </div>
                    )}
                  </td>
                )}
                <td
                  className="select-none px-1.5 text-right align-top text-fg-muted opacity-50"
                  style={{ width: gutterW }}
                >
                  {cell.lineno ?? ""}
                </td>
                <td className="whitespace-pre pr-3 overflow-hidden">
                  {tokenLine ? (
                    <TokenLine tokens={tokenLine.tokens} />
                  ) : (
                    cell.content || "\u00a0"
                  )}
                </td>
              </tr>
            );
          })}
          {endRow < rows.length && (
            <tr><td style={{ height: (rows.length - endRow) * ROW_HEIGHT, padding: 0 }} /></tr>
          )}
        </tbody>
      </table>
    );
  }

  return (
    <div
      className="flex flex-1 overflow-hidden text-sm leading-relaxed"
      style={{ backgroundColor: bg, willChange: "transform" }}
      onWheel={handleWheel}
    >
      <div
        ref={leftRef}
        className="flex-1 min-w-0 overflow-auto"
        onScroll={() => handleScroll(leftRef.current, rightRef.current)}
      >
        {renderSide("left", tokenizedLines)}
      </div>
      <div className="w-px flex-shrink-0 bg-border" />
      <div
        ref={rightRef}
        className="flex-1 min-w-0 overflow-auto"
        onScroll={() => handleScroll(rightRef.current, leftRef.current)}
      >
        {renderSide("right", tokenizedLines)}
      </div>
    </div>
  );
}

// ── Plain file view (no diff) ───────────────────────────────────

function PlainFileView({
  lines,
  bg,
}: {
  lines: TokenizedLine[];
  bg: string | undefined;
}) {
  const lineCount = lines.length;
  const gutterWidth = Math.max(String(lineCount).length, 2);

  // ── Virtualization ──────────────────────────────────────────────
  const scrollRef = useRef<HTMLDivElement>(null);
  const [scrollTop, setScrollTop] = useState(0);
  const [viewportHeight, setViewportHeight] = useState(0);

  const onScroll = useCallback(() => {
    if (scrollRef.current) setScrollTop(scrollRef.current.scrollTop);
  }, []);

  const refCallback = useCallback((el: HTMLDivElement | null) => {
    (scrollRef as React.MutableRefObject<HTMLDivElement | null>).current = el;
    if (el) setViewportHeight(el.clientHeight);
  }, []);

  const startRow = Math.max(0, Math.floor(scrollTop / ROW_HEIGHT) - OVERSCAN);
  const endRow = Math.min(lineCount, Math.ceil((scrollTop + viewportHeight) / ROW_HEIGHT) + OVERSCAN);
  // ────────────────────────────────────────────────────────────────

  return (
    <div
      ref={refCallback}
      onScroll={onScroll}
      className="flex-1 overflow-auto text-sm leading-relaxed"
      style={{ backgroundColor: bg, willChange: "transform" }}
    >
      <table className="w-full border-collapse font-mono">
        <tbody>
          {startRow > 0 && (
            <tr><td style={{ height: startRow * ROW_HEIGHT, padding: 0 }} /></tr>
          )}
          {lines.slice(startRow, endRow).map((line, offset) => {
            const i = startRow + offset;
            return (
              <tr key={i} style={{ height: ROW_HEIGHT }} className="leading-relaxed">
                <td
                  className="select-none px-3 text-right align-top text-fg-muted opacity-50"
                  style={{ width: `${gutterWidth + 2}ch` }}
                >
                  {i + 1}
                </td>
                <td className="whitespace-pre pr-3 overflow-hidden">
                  <TokenLine tokens={line.tokens} />
                </td>
              </tr>
            );
          })}
          {endRow < lineCount && (
            <tr><td style={{ height: (lineCount - endRow) * ROW_HEIGHT, padding: 0 }} /></tr>
          )}
        </tbody>
      </table>
    </div>
  );
}

// ── FileViewer (main export) ────────────────────────────────────

import type { LineSelection } from "../../../ipc/bindings";

interface FileViewerProps {
  filePath: string;
  content: string;
  diff?: FileDiff | null;
  viewMode?: DiffViewMode;
  onViewModeChange?: (mode: DiffViewMode) => void;
  onStageLines?: (selections: LineSelection[]) => void;
  onUnstageLines?: (selections: LineSelection[]) => void;
  onDiscardLines?: (selections: LineSelection[]) => void;
}

/** Known binary/non-text file extensions that cannot be meaningfully diffed. */
const BINARY_EXTENSIONS = new Set([
  "png", "jpg", "jpeg", "gif", "bmp", "ico", "webp", "avif", "tiff", "tif",
  "svg", "pdf",
  "woff", "woff2", "ttf", "otf", "eot",
  "zip", "gz", "tar", "bz2", "xz", "7z", "rar",
  "exe", "dll", "so", "dylib", "bin",
  "wasm",
  "mp3", "mp4", "ogg", "wav", "flac", "avi", "mkv", "mov", "webm",
  "class", "jar", "pyc", "pyo",
  "ds_store",
]);

function isBinaryFile(filePath: string): boolean {
  const ext = filePath.split(".").pop()?.toLowerCase() ?? "";
  return BINARY_EXTENSIONS.has(ext);
}

export function FileViewer({
  filePath,
  content,
  diff,
  viewMode = "unified",
  onViewModeChange,
  onStageLines,
  onUnstageLines,
  onDiscardLines,
}: FileViewerProps) {
  const isBinary = isBinaryFile(filePath);
  const { lines, bg } = useHighlightedLines(filePath, isBinary ? "" : content);

  const hasDiff = diff && diff.hunks.length > 0;

  if (isBinary) {
    return (
      <div className="flex h-full flex-col overflow-hidden">
        <div className="flex flex-shrink-0 items-center border-b border-border bg-bg-surface px-4 py-2 text-xs text-fg-muted">
          <SmartPath path={filePath} className="flex-1 text-xs" />
        </div>
        <div className="flex flex-1 items-center justify-center text-sm text-fg-muted">
          Diff view is not supported for this file type.
        </div>
      </div>
    );
  }

  return (
    <div className="flex h-full flex-col overflow-hidden">
      <div className="flex flex-shrink-0 items-center border-b border-border bg-bg-surface px-4 py-2 text-xs text-fg-muted">
        <SmartPath path={filePath} className="flex-1 text-xs" />
        {hasDiff && onViewModeChange && (
          <div className="ml-3 flex items-center gap-1">
            <button
              onClick={() => onViewModeChange("unified")}
              className={`cursor-pointer rounded px-2 py-0.5 text-xs transition-colors ${
                viewMode === "unified"
                  ? "bg-accent text-accent-fg"
                  : "text-fg-muted hover:bg-bg-hover hover:text-fg"
              }`}
            >
              Unified
            </button>
            <button
              onClick={() => onViewModeChange("split")}
              className={`cursor-pointer rounded px-2 py-0.5 text-xs transition-colors ${
                viewMode === "split"
                  ? "bg-accent text-accent-fg"
                  : "text-fg-muted hover:bg-bg-hover hover:text-fg"
              }`}
            >
              Split
            </button>
          </div>
        )}
      </div>
      {lines === null ? (
        <div className="flex-1 p-3 text-fg-muted">Highlighting…</div>
      ) : hasDiff ? (
        viewMode === "split" ? (
          <SplitDiffView
            diff={diff}
            filePath={filePath}
            tokenizedLines={lines}
            bg={bg}
            onStageLines={onStageLines}
            onUnstageLines={onUnstageLines}
            onDiscardLines={onDiscardLines}
          />
        ) : (
          <UnifiedDiffView
            diff={diff}
            filePath={filePath}
            tokenizedLines={lines}
            bg={bg}
            onStageLines={onStageLines}
            onUnstageLines={onUnstageLines}
            onDiscardLines={onDiscardLines}
          />
        )
      ) : (
        <PlainFileView lines={lines} bg={bg} />
      )}
    </div>
  );
}
