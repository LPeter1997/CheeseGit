import { useCallback, useEffect, useRef, useState } from "react";
import type { DiffSearchMatch, TokenizedLine } from "../hooks/useHighlightedLines";
import { HighlightedText } from "./HighlightedText";
import { ROW_HEIGHT, OVERSCAN, TokenLine } from "./DiffViewShared";

export interface PlainFileViewProps {
  filePath: string;
  lines: TokenizedLine[];
  plainLines: string[];
  bg: string | undefined;
  searchMatches?: DiffSearchMatch[];
  currentMatch?: DiffSearchMatch | null;
}

export function PlainFileView({
  filePath: _filePath,
  lines,
  plainLines,
  bg,
  searchMatches,
  currentMatch,
}: PlainFileViewProps) {
  const lineCount = lines.length;
  const gutterWidth = Math.max(String(lineCount).length, 2);

  const getMatchesForLine = useCallback(
    (lineIndex: number) => {
      if (!searchMatches) return [];
      return searchMatches.filter((m) => m.lineIndex === lineIndex);
    },
    [searchMatches],
  );

  // ── Virtualization ──────────────────────────────────────────────
  const scrollRef = useRef<HTMLDivElement>(null);
  const [scrollTop, setScrollTop] = useState(0);
  const [viewportHeight, setViewportHeight] = useState(0);

  const onScroll = useCallback(() => {
    if (scrollRef.current) setScrollTop(scrollRef.current.scrollTop);
  }, []);

  // Track viewport height continuously via ResizeObserver.
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

  useEffect(() => {
    if (currentMatch && scrollRef.current) {
      const targetScroll = currentMatch.lineIndex * ROW_HEIGHT - (viewportHeight / 2);
      scrollRef.current.scrollTop = Math.max(0, targetScroll);
    }
  }, [currentMatch, viewportHeight]);

  const startRow = Math.max(0, Math.floor(scrollTop / ROW_HEIGHT) - OVERSCAN);
  const endRow = Math.min(lineCount, Math.ceil((scrollTop + viewportHeight) / ROW_HEIGHT) + OVERSCAN);
  // ────────────────────────────────────────────────────────────────

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
          {lines.slice(startRow, endRow).map((line, offset) => {
            const i = startRow + offset;
            const lineMatches = getMatchesForLine(i);
            return (
              <tr key={i} style={{ height: ROW_HEIGHT }} className="leading-relaxed">
                <td
                  className="select-none px-3 text-right align-top text-fg-muted opacity-50"
                  style={{ width: `${gutterWidth + 2}ch` }}
                >
                  {i + 1}
                </td>
                <td className="whitespace-pre pr-3 overflow-hidden">
                  {lineMatches.length > 0 ? (
                    <HighlightedText
                      content={plainLines[i] ?? ""}
                      matches={lineMatches}
                      currentMatchLineIndex={currentMatch?.lineIndex}
                      currentMatchCharOffset={currentMatch?.charOffset}
                    />
                  ) : (
                    <TokenLine tokens={line.tokens} />
                  )}
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
