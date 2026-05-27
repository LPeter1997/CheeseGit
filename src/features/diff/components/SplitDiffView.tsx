import { useCallback, useEffect, useLayoutEffect, useMemo, useRef, useState } from "react";
import type { FileDiff, LineSelection } from "../../../ipc/bindings";
import type { DiffSearchMatch, TokenizedLine } from "../hooks/useHighlightedLines";
import { useShiftKey } from "../../../shared/hooks/useShiftKey";
import { HighlightedText } from "./HighlightedText";
import {
  ROW_HEIGHT,
  OVERSCAN,
  TokenLine,
  SplitSide,
  SplitRow,
  buildSplitRows,
} from "./DiffViewShared";

export interface SplitDiffViewProps {
  diff: FileDiff;
  filePath: string;
  tokenizedLines: TokenizedLine[];
  bg: string | undefined;
  onStageLines?: (selections: LineSelection[]) => void;
  onUnstageLines?: (selections: LineSelection[]) => void;
  onDiscardLines?: (selections: LineSelection[]) => void;
  searchMatches?: DiffSearchMatch[];
  currentMatch?: DiffSearchMatch | null;
}

export function SplitDiffView({
  diff,
  filePath,
  tokenizedLines,
  bg,
  onStageLines,
  onUnstageLines,
  onDiscardLines,
  searchMatches,
  currentMatch,
}: SplitDiffViewProps) {
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

  // Get matches for a specific line
  const getMatchesForLine = useCallback(
    (searchLineIndex: number | null) => {
      if (!searchMatches || searchLineIndex === null) return [];
      return searchMatches.filter((m) => m.lineIndex === searchLineIndex);
    },
    [searchMatches],
  );

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

  // Auto-scroll to current search match
  useEffect(() => {
    if (currentMatch && leftRef.current && rightRef.current) {
      const rowIndex = rows.findIndex(
        (row) => row.left.searchLineIndex === currentMatch.lineIndex || row.right.searchLineIndex === currentMatch.lineIndex,
      );
      if (rowIndex < 0) return;
      const targetScroll = rowIndex * ROW_HEIGHT - (viewportHeight / 2);
      leftRef.current.scrollTop = Math.max(0, targetScroll);
      rightRef.current.scrollTop = Math.max(0, targetScroll);
    }
  }, [currentMatch, viewportHeight, rows]);

  // Measure viewport height via ResizeObserver.
  useEffect(() => {
    const el = leftRef.current;
    if (!el) return;
    const observer = new ResizeObserver((entries) => {
      for (const entry of entries) {
        setViewportHeight(entry.contentRect.height);
      }
    });
    setViewportHeight(el.clientHeight);
    observer.observe(el);
    return () => observer.disconnect();
  }, []);
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
                  {(() => {
                    const lineMatches = getMatchesForLine(cell.searchLineIndex);
                    if (lineMatches.length > 0) {
                      return (
                        <HighlightedText
                          content={cell.content || "\u00a0"}
                          matches={lineMatches}
                          currentMatchLineIndex={currentMatch?.lineIndex}
                          currentMatchCharOffset={currentMatch?.charOffset}
                        />
                      );
                    }

                    if (tokenLine) return <TokenLine tokens={tokenLine.tokens} />;

                    return (
                      <HighlightedText
                        content={cell.content || "\u00a0"}
                        matches={lineMatches}
                        currentMatchLineIndex={currentMatch?.lineIndex}
                        currentMatchCharOffset={currentMatch?.charOffset}
                      />
                    );
                  })()}
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
