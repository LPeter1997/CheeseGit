import { useCallback, useRef, useState } from "react";
import { formatRelativeDate } from "../../../shared/utils/format";
import { useHistoryStore } from "../store";
import { GraphOverlay, graphWidth } from "./BranchGraph";
import { ROW_HEIGHT } from "../graph/constants";

/** Number of extra rows rendered above/below the visible viewport. */
const OVERSCAN = 5;

interface HistoryListProps {
  repoPath: string;
}

export function HistoryList({ repoPath }: HistoryListProps) {
  const commits = useHistoryStore((s) => s.commits);
  const selectedIndex = useHistoryStore((s) => s.selectedIndex);
  const selectCommit = useHistoryStore((s) => s.selectCommit);
  const loading = useHistoryStore((s) => s.loading);
  const graphLayout = useHistoryStore((s) => s.graphLayout);
  const graphData = useHistoryStore((s) => s.graphData);
  const hoveredBranch = useHistoryStore((s) => s.hoveredBranch);
  const setHoveredBranch = useHistoryStore((s) => s.setHoveredBranch);

  // Scroll position drives which rows are rendered.
  const scrollRef = useRef<HTMLDivElement>(null);
  const [scrollTop, setScrollTop] = useState(0);
  const [viewportHeight, setViewportHeight] = useState(0);

  const onScroll = useCallback(() => {
    const el = scrollRef.current;
    if (el) {
      setScrollTop(el.scrollTop);
      setViewportHeight(el.clientHeight);
    }
  }, []);

  // Measure viewport on first render via ref callback.
  const refCallback = useCallback((el: HTMLDivElement | null) => {
    (scrollRef as React.MutableRefObject<HTMLDivElement | null>).current = el;
    if (el) {
      setViewportHeight(el.clientHeight);
    }
  }, []);

  if (loading) {
    return (
      <div className="flex h-full items-center justify-center text-xs text-fg-muted">
        Loading…
      </div>
    );
  }

  const displayCommits = graphLayout?.commits ?? graphData?.commits ?? commits;

  if (displayCommits.length === 0) {
    return (
      <div className="flex h-full items-center justify-center text-xs text-fg-muted">
        No commits yet.
      </div>
    );
  }

  // Build a lookup from graph commit hash → original commit index.
  const hashToOriginalIndex = new Map<string, number>();
  for (let i = 0; i < commits.length; i++) {
    hashToOriginalIndex.set(commits[i].hash, i);
  }

  const handleSelect = (hash: string) => {
    const originalIdx = hashToOriginalIndex.get(hash);
    if (originalIdx !== undefined) {
      selectCommit(originalIdx, repoPath);
    }
  };

  const selectedHash = selectedIndex >= 0 ? commits[selectedIndex]?.hash : null;

  const hashToBranch = new Map<string, string>();
  if (graphLayout) {
    for (const node of graphLayout.nodes) {
      hashToBranch.set(node.hash, node.branch);
    }
  }

  const hasGraph = graphLayout && graphLayout.nodes.length > 0;
  const gw = hasGraph ? graphWidth(graphLayout.columnCount) : 0;
  const totalHeight = displayCommits.length * ROW_HEIGHT;

  // Compute visible row range with overscan.
  const startRow = Math.max(0, Math.floor(scrollTop / ROW_HEIGHT) - OVERSCAN);
  const endRow = Math.min(
    displayCommits.length,
    Math.ceil((scrollTop + viewportHeight) / ROW_HEIGHT) + OVERSCAN,
  );

  return (
    <div ref={refCallback} className="overflow-auto h-full" onScroll={onScroll}>
      <div className="relative" style={{ height: totalHeight }}>
        {/* Graph: edges + dots in a single SVG (grouped per branch) */}
        {hasGraph && (
          <GraphOverlay
            layout={graphLayout}
            height={totalHeight}
            hoveredBranch={hoveredBranch}
            onHoverBranch={setHoveredBranch}
          />
        )}

        {/* Virtualized commit rows — only visible rows are in the DOM. */}
        {displayCommits.slice(startRow, endRow).map((commit, i) => {
          const rowIndex = startRow + i;
          const isSelected = commit.hash === selectedHash;
          const commitBranch = hashToBranch.get(commit.hash);
          const isFaded = hoveredBranch !== null && commitBranch !== hoveredBranch;

          return (
            <div
              key={commit.hash}
              className="absolute left-0 right-0 flex"
              style={{ height: ROW_HEIGHT, top: rowIndex * ROW_HEIGHT }}
            >
              <button
                onClick={() => handleSelect(commit.hash)}
                style={{ paddingLeft: hasGraph ? gw : undefined }}
                className={`flex w-full flex-col justify-center gap-0.5 border-b border-border px-3 text-left transition-[color,opacity] duration-150 ${
                  isSelected
                    ? "bg-accent/10 text-fg"
                    : "text-fg hover:bg-bg-hover"
                } ${isFaded ? "opacity-30" : ""}`}
              >
                <span className="truncate text-sm font-medium">{commit.summary}</span>
                <div className="flex items-center gap-2 text-xs text-fg-muted">
                  <span>{commit.author}</span>
                  <span>·</span>
                  <span>{formatRelativeDate(new Date(commit.timestamp))}</span>
                  <span className="ml-auto font-mono text-[10px]">{commit.short_hash}</span>
                </div>
              </button>
            </div>
          );
        })}
      </div>
    </div>
  );
}
