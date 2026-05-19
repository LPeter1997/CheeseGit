import { useCallback, useEffect, useRef, useState } from "react";
import { formatRelativeDate } from "../../../shared/utils/format";
import { useHistoryStore } from "../store";
import { GraphOverlay, graphWidth } from "./BranchGraph";
import { ROW_HEIGHT } from "../graph/constants";

/** Number of extra rows rendered above/below the visible viewport. */
const OVERSCAN = 5;

/** Trigger load-more when within this many pixels from the bottom. */
const LOAD_MORE_THRESHOLD = 200;

/** Copy icon SVG (clipboard). */
function CopyIcon() {
  return (
    <svg width="12" height="12" viewBox="0 0 16 16" fill="currentColor" className="shrink-0">
      <path d="M0 6.75C0 5.784.784 5 1.75 5h1.5a.75.75 0 0 1 0 1.5h-1.5a.25.25 0 0 0-.25.25v7.5c0 .138.112.25.25.25h7.5a.25.25 0 0 0 .25-.25v-1.5a.75.75 0 0 1 1.5 0v1.5A1.75 1.75 0 0 1 9.25 16h-7.5A1.75 1.75 0 0 1 0 14.25Z" />
      <path d="M5 1.75C5 .784 5.784 0 6.75 0h7.5C15.216 0 16 .784 16 1.75v7.5A1.75 1.75 0 0 1 14.25 11h-7.5A1.75 1.75 0 0 1 5 9.25Zm1.75-.25a.25.25 0 0 0-.25.25v7.5c0 .138.112.25.25.25h7.5a.25.25 0 0 0 .25-.25v-7.5a.25.25 0 0 0-.25-.25Z" />
    </svg>
  );
}

interface HistoryListProps {
  repoPath: string;
}

export function HistoryList({ repoPath }: HistoryListProps) {
  const commits = useHistoryStore((s) => s.commits);
  const selectedHash = useHistoryStore((s) => s.selectedHash);
  const selectCommit = useHistoryStore((s) => s.selectCommit);
  const loading = useHistoryStore((s) => s.loading);
  const graphLayout = useHistoryStore((s) => s.graphLayout);
  const graphData = useHistoryStore((s) => s.graphData);
  const hoveredBranch = useHistoryStore((s) => s.hoveredBranch);
  const setHoveredBranch = useHistoryStore((s) => s.setHoveredBranch);
  const hasMoreCommits = useHistoryStore((s) => s.hasMoreCommits);
  const loadingMore = useHistoryStore((s) => s.loadingMore);
  const loadMoreGraph = useHistoryStore((s) => s.loadMoreGraph);

  // Track which hash was just copied for transient feedback.
  const [copiedHash, setCopiedHash] = useState<string | null>(null);
  const copiedTimer = useRef<ReturnType<typeof setTimeout> | null>(null);

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

  // Trigger load-more when scrolled near the bottom.
  useEffect(() => {
    const el = scrollRef.current;
    if (!el || !hasMoreCommits || loadingMore) return;
    const distanceFromBottom = el.scrollHeight - el.scrollTop - el.clientHeight;
    if (distanceFromBottom < LOAD_MORE_THRESHOLD) {
      loadMoreGraph(repoPath);
    }
  }, [scrollTop, hasMoreCommits, loadingMore, loadMoreGraph, repoPath]);

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

  const handleSelect = (hash: string) => {
    selectCommit(hash, repoPath);
  };

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
        {/* Graph: edges + dots in a single SVG (virtualized to viewport) */}
        {hasGraph && (
          <GraphOverlay
            layout={graphLayout}
            height={totalHeight}
            scrollTop={scrollTop}
            viewportHeight={viewportHeight}
            hoveredBranch={hoveredBranch}
            onHoverBranch={setHoveredBranch}
            headHash={commits[0]?.hash}
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
              className="absolute left-0 right-0 flex group/row"
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
                <div className="flex items-center gap-2 text-xs text-fg-muted min-w-0">
                  <span className="truncate">{commit.author}</span>
                  <span>·</span>
                  <span>{formatRelativeDate(new Date(commit.timestamp))}</span>
                  <span
                    className="ml-auto flex items-center gap-1 font-mono text-[10px]"
                    onClick={(e) => e.stopPropagation()}
                  >
                    {copiedHash === commit.hash ? (
                      <span
                        className="text-fg text-[10px]"
                        style={{ animation: "alert-copied-fade 1.5s ease-out forwards" }}
                      >
                        Copied!
                      </span>
                    ) : (
                      <>
                        {commit.short_hash}
                        <button
                          type="button"
                          title="Copy full hash"
                          className="cursor-pointer opacity-0 group-hover/row:opacity-60 hover:!opacity-100 active:scale-90 transition-[opacity,transform] p-0.5 rounded hover:bg-bg-hover"
                          onClick={(e) => {
                            e.stopPropagation();
                            navigator.clipboard.writeText(commit.hash);
                            setCopiedHash(commit.hash);
                            clearTimeout(copiedTimer.current);
                            copiedTimer.current = setTimeout(() => setCopiedHash(null), 1500);
                          }}
                        >
                          <CopyIcon />
                        </button>
                      </>
                    )}
                  </span>
                </div>
              </button>
            </div>
          );
        })}
      </div>
      {/* Load-more indicator at the bottom */}
      {loadingMore && (
        <div className="flex items-center justify-center py-3 text-xs text-fg-muted">
          Loading more commits…
        </div>
      )}
    </div>
  );
}
