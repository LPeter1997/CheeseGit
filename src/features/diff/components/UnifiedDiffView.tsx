import { useCallback, useEffect, useLayoutEffect, useMemo, useRef, useState } from "react";
import type { FileDiff, LineSelection } from "../../../ipc/bindings";
import type { DiffSearchMatch, TokenizedLine } from "../hooks/useHighlightedLines";
import { useShiftKey } from "../../../shared/hooks/useShiftKey";
import { HighlightedText } from "./HighlightedText";
import {
  ROW_HEIGHT,
  OVERSCAN,
  TokenLine,
  UnifiedRow,
  buildUnifiedRows,
} from "./DiffViewShared";

export interface UnifiedDiffViewProps {
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

export function UnifiedDiffView({
  diff,
  filePath,
  tokenizedLines,
  bg,
  onStageLines,
  onUnstageLines,
  onDiscardLines,
  searchMatches,
  currentMatch,
}: UnifiedDiffViewProps) {
  const rows = useMemo(
    () => buildUnifiedRows(diff),
    [diff],
  );
  const [hoveredGroup, setHoveredGroup] = useState<{ start: number; end: number } | null>(null);
  const [hoveredLine, setHoveredLine] = useState<number | null>(null);
  const [hoveredHunk, setHoveredHunk] = useState<number | null>(null);

  // Get matches for a specific line
  const getMatchesForLine = useCallback(
    (searchLineIndex: number | null) => {
      if (!searchMatches || searchLineIndex === null) return [];
      return searchMatches.filter((m) => m.lineIndex === searchLineIndex);
    },
    [searchMatches],
  );

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

  // Track viewport height continuously via ResizeObserver so the
  // virtualization window stays accurate after layout changes.
  useEffect(() => {
    const el = scrollRef.current;
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

  // Reset scroll when the user switches to a different file.
  // We intentionally do NOT depend on `diff` here — a refresh of
  // the same file (e.g., from the file watcher) should preserve
  // the current scroll position.
  useLayoutEffect(() => {
    if (scrollRef.current) scrollRef.current.scrollTop = 0;
    setScrollTop(0);
  }, [filePath]);

  // Auto-scroll to current search match
  useEffect(() => {
    if (currentMatch && scrollRef.current) {
      const rowIndex = rows.findIndex((row) => row.searchLineIndex === currentMatch.lineIndex);
      if (rowIndex < 0) return;
      const targetScroll = rowIndex * ROW_HEIGHT - (viewportHeight / 2);
      scrollRef.current.scrollTop = Math.max(0, targetScroll);
    }
  }, [currentMatch, viewportHeight, rows]);

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
      ref={scrollRef}
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
                  {(() => {
                    const lineMatches = getMatchesForLine(row.searchLineIndex);
                    if (lineMatches.length > 0) {
                      return (
                        <HighlightedText
                          content={row.content}
                          matches={lineMatches}
                          currentMatchLineIndex={currentMatch?.lineIndex}
                          currentMatchCharOffset={currentMatch?.charOffset}
                        />
                      );
                    }

                    if (tokens) return <TokenLine tokens={tokens.tokens} />;

                    return (
                      <HighlightedText
                        content={row.content}
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
    </div>
  );
}
