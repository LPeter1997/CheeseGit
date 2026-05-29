import { useCallback, useEffect, useMemo, useRef, useState, type KeyboardEvent as ReactKeyboardEvent } from "react";
import { formatRelativeDate } from "../../../shared/utils/format";
import { DiffStats } from "../../../shared/components/DiffStats";
import { useHistoryStore } from "../store";
import { GraphOverlay, graphWidth } from "./BranchGraph";
import { ROW_HEIGHT } from "../graph/constants";
import type { CommitInfo, GraphCommit } from "../../../ipc/bindings";

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

/** Revert icon SVG (undo arrow). */
function RevertIcon() {
  return (
    <svg width="12" height="12" viewBox="0 0 16 16" fill="currentColor" className="shrink-0">
      <path fillRule="evenodd" d="M1.22 6.28a.75.75 0 0 1 0-1.06l3-3a.75.75 0 0 1 1.06 1.06L3.56 5h7.69a3.75 3.75 0 0 1 0 7.5H7.75a.75.75 0 0 1 0-1.5h3.5a2.25 2.25 0 0 0 0-4.5H3.56l1.72 1.72a.75.75 0 0 1-1.06 1.06l-3-3Z" clipRule="evenodd" />
    </svg>
  );
}

/** Uncommit icon SVG (move latest commit back to staging). */
function UncommitIcon() {
  return (
    <svg width="12" height="12" viewBox="0 0 16 16" fill="currentColor" className="shrink-0">
      <path fillRule="evenodd" d="M8 1.5a.75.75 0 0 1 .75.75V8.2l2-2a.75.75 0 1 1 1.06 1.06l-3.28 3.27a.75.75 0 0 1-1.06 0L4.2 7.26a.75.75 0 0 1 1.06-1.06l1.99 2V2.25A.75.75 0 0 1 8 1.5Zm-4.5 10a.75.75 0 0 1 .75.75v1A1.25 1.25 0 0 0 5.5 14.5h5A1.25 1.25 0 0 0 11.75 13v-1a.75.75 0 0 1 1.5 0v1A2.75 2.75 0 0 1 10.5 15.75h-5A2.75 2.75 0 0 1 2.75 13v-1a.75.75 0 0 1 .75-.75Z" clipRule="evenodd"/>
    </svg>
  );
}

/** Cherry-pick icon SVG. */
function CherryPickIcon() {
  return (
    <svg width="12" height="12" viewBox="0 0 16 16" fill="currentColor" className="shrink-0">
      <path d="M4.25 2a.75.75 0 0 1 .75.75V8h6.19L9.47 6.28a.75.75 0 0 1 1.06-1.06l3 3a.75.75 0 0 1 0 1.06l-3 3a.75.75 0 0 1-1.06-1.06L11.19 9.5H4.25a.75.75 0 0 1-.75-.75V2.75A.75.75 0 0 1 4.25 2Z" />
      <path d="M2.75 10a.75.75 0 0 1 .75.75v1.5c0 .14.11.25.25.25h1.5a.75.75 0 0 1 0 1.5h-1.5A1.75 1.75 0 0 1 2 12.25v-1.5A.75.75 0 0 1 2.75 10Z" />
    </svg>
  );
}

interface HistoryListProps {
  repoPath: string;
  browsingHistory?: boolean;
  onCheckoutCommit?: (hash: string) => void;
  onRevertCommit?: (hash: string) => void;
  onUndoLastCommit?: (hash: string) => void;
  onJumpToPresent?: () => void;
  onCherryPickCommits?: (hashes: string[]) => void;
}

interface SelectionModifiers {
  ctrlKey: boolean;
  metaKey: boolean;
  shiftKey: boolean;
}

function buildRange(hashes: string[], from: string, to: string): string[] {
  const fromIndex = hashes.indexOf(from);
  const toIndex = hashes.indexOf(to);
  if (fromIndex === -1 || toIndex === -1) return [to];
  const [lo, hi] = fromIndex <= toIndex ? [fromIndex, toIndex] : [toIndex, fromIndex];
  return hashes.slice(lo, hi + 1);
}

function commitMatchesSearch(commit: CommitInfo | GraphCommit, query: string): boolean {
  const graphCommit = commit as GraphCommit;
  const parts = [
    commit.hash,
    commit.short_hash,
    commit.summary,
    commit.author,
    commit.timestamp,
    ...(Array.isArray(graphCommit.parents) ? graphCommit.parents : []),
    ...(Array.isArray(graphCommit.refs) ? graphCommit.refs : []),
  ];

  if (typeof graphCommit.insertions === "number") {
    parts.push(String(graphCommit.insertions));
  }
  if (typeof graphCommit.deletions === "number") {
    parts.push(String(graphCommit.deletions));
  }

  return parts.join(" ").toLowerCase().includes(query);
}

export function HistoryList({ repoPath, browsingHistory, onCheckoutCommit, onRevertCommit, onUndoLastCommit, onJumpToPresent, onCherryPickCommits }: HistoryListProps) {
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
  const copiedTimer = useRef<ReturnType<typeof setTimeout> | undefined>(undefined);

  // Multi-selection state for cherry-picking from history.
  const [selectedHashes, setSelectedHashes] = useState<Set<string>>(new Set());
  const [selectionAnchor, setSelectionAnchor] = useState<string | null>(null);
  const [searchQuery, setSearchQuery] = useState("");

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

  const baseCommits = graphLayout?.commits ?? graphData?.commits ?? commits;
  const normalizedSearch = searchQuery.trim().toLowerCase();
  const isSearching = normalizedSearch.length > 0;
  const displayCommits = useMemo(
    () => (isSearching ? baseCommits.filter((commit) => commitMatchesSearch(commit, normalizedSearch)) : baseCommits),
    [baseCommits, isSearching, normalizedSearch],
  );

  useEffect(() => {
    const available = new Set(displayCommits.map((commit) => commit.hash));
    setSelectedHashes((prev) => {
      const next = new Set(Array.from(prev).filter((hash) => available.has(hash)));
      if (next.size === prev.size && Array.from(next).every((hash) => prev.has(hash))) {
        return prev;
      }
      return next;
    });
    setSelectionAnchor((prev) => (prev && available.has(prev) ? prev : null));
  }, [displayCommits]);

  useEffect(() => {
    if (!selectedHash) return;
    const selectedIndex = displayCommits.findIndex((commit) => commit.hash === selectedHash);
    if (selectedIndex === -1) return;

    const el = scrollRef.current;
    if (!el) return;

    const rowTop = selectedIndex * ROW_HEIGHT;
    const rowBottom = rowTop + ROW_HEIGHT;
    const visibleTop = el.scrollTop;
    const visibleBottom = visibleTop + el.clientHeight;
    const inView = rowTop >= visibleTop && rowBottom <= visibleBottom;

    if (inView) return;

    const targetTop = Math.max(0, rowTop - Math.max(0, (el.clientHeight - ROW_HEIGHT) / 2));
    if (typeof el.scrollTo === "function") {
      el.scrollTo({ top: targetTop });
      return;
    }
    el.scrollTop = targetTop;
  }, [selectedHash, displayCommits]);

  if (loading) {
    return (
      <div className="flex h-full items-center justify-center text-xs text-fg-muted">
        Loading…
      </div>
    );
  }

  if (baseCommits.length === 0) {
    return (
      <div className="flex h-full items-center justify-center text-xs text-fg-muted">
        No commits yet.
      </div>
    );
  }

  const handleSelect = (hash: string, modifiers: SelectionModifiers) => {
    const orderedHashes = displayCommits.map((commit) => commit.hash);
    const ctrlLike = modifiers.ctrlKey || modifiers.metaKey;
    const shiftLike = modifiers.shiftKey;

    if (shiftLike) {
      const anchor = selectionAnchor ?? hash;
      const range = buildRange(orderedHashes, anchor, hash);
      setSelectedHashes((prev) => {
        if (ctrlLike) {
          const next = new Set(prev);
          for (const rangeHash of range) next.add(rangeHash);
          return next;
        }
        return new Set(range);
      });
      return;
    }

    if (ctrlLike) {
      setSelectedHashes((prev) => {
        const next = new Set(prev);
        if (next.has(hash)) next.delete(hash);
        else next.add(hash);
        return next;
      });
      setSelectionAnchor(hash);
      return;
    }

    setSelectedHashes(new Set([hash]));
    setSelectionAnchor(hash);
    selectCommit(hash, repoPath);
  };

  const handleRowKeyDown = (hash: string, event: ReactKeyboardEvent<HTMLDivElement>) => {
    if (event.key !== "Enter" && event.key !== " ") return;
    event.preventDefault();
    handleSelect(hash, { ctrlKey: event.ctrlKey, metaKey: event.metaKey, shiftKey: event.shiftKey });
  };

  const selectedHashesInDisplayOrder = displayCommits
    .filter((commit) => selectedHashes.has(commit.hash))
    .map((commit) => commit.hash);
  const canCherryPick = !!onCherryPickCommits && selectedHashesInDisplayOrder.length > 0;

  const hashToBranch = new Map<string, string>();
  if (graphLayout) {
    for (const node of graphLayout.nodes) {
      hashToBranch.set(node.hash, node.branch);
    }
  }

  const hasGraph = graphLayout && graphLayout.nodes.length > 0;
  const showGraph = hasGraph && !isSearching;
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
        {showGraph && (
          <GraphOverlay
            layout={graphLayout}
            height={totalHeight}
            scrollTop={scrollTop}
            viewportHeight={viewportHeight}
            hoveredBranch={hoveredBranch}
            onHoverBranch={setHoveredBranch}
            headHash={commits[0]?.hash}
            onClickCommit={onCheckoutCommit}
          />
        )}

        {/* Virtualized commit rows — only visible rows are in the DOM. */}
        {displayCommits.slice(startRow, endRow).map((commit, i) => {
          const rowIndex = startRow + i;
          const isSelected = selectedHashes.has(commit.hash);
          const commitBranch = hashToBranch.get(commit.hash);
          const isFaded = hoveredBranch !== null && commitBranch !== hoveredBranch;
          const isHeadCommit = commit.hash === commits[0]?.hash;
          const canUndoLastCommit = !!onUndoLastCommit && isHeadCommit && !browsingHistory;

          return (
            <div
              key={commit.hash}
              data-selected={isSelected ? "true" : "false"}
              className="absolute left-0 right-0 flex group/row"
              style={{ height: ROW_HEIGHT, top: rowIndex * ROW_HEIGHT }}
            >
              <div
                role="button"
                tabIndex={0}
                data-testid="history-row"
                onClick={(event) => handleSelect(commit.hash, { ctrlKey: event.ctrlKey, metaKey: event.metaKey, shiftKey: event.shiftKey })}
                onKeyDown={(event) => handleRowKeyDown(commit.hash, event)}
                style={{ paddingLeft: hasGraph ? gw : undefined }}
                className={`flex w-full cursor-pointer flex-col justify-center gap-0.5 border-b border-border px-3 text-left transition-[color,opacity] duration-150 focus-visible:outline-none focus-visible:ring-1 focus-visible:ring-accent ${
                  isSelected
                    ? "bg-accent/10 text-fg"
                    : "text-fg hover:bg-bg-hover"
                } ${isFaded ? "opacity-30" : ""}`}
              >
                <div className="flex items-center gap-2 min-w-0">
                  <span data-testid="commit-message" className="truncate text-sm font-medium">{commit.summary}</span>
                  {canUndoLastCommit && (
                    <span className="ml-auto mr-1 flex-shrink-0" onClick={(e) => e.stopPropagation()}>
                      <button
                        type="button"
                        title="Undo latest commit (keep changes staged)"
                        data-testid="undo-last-commit-button"
                        className="cursor-pointer opacity-0 group-hover/row:opacity-60 hover:!opacity-100 active:scale-90 transition-[opacity,transform] p-0.5 rounded hover:bg-bg-hover"
                        onClick={(e) => {
                          e.stopPropagation();
                          onUndoLastCommit(commit.hash);
                        }}
                      >
                        <UncommitIcon />
                      </button>
                    </span>
                  )}
                  {onRevertCommit && (
                    <span className={`${canUndoLastCommit ? "mr-1" : "ml-auto mr-1"} flex-shrink-0`} onClick={(e) => e.stopPropagation()}>
                      <button
                        type="button"
                        title="Revert this commit"
                        data-testid="revert-commit-button"
                        className="cursor-pointer opacity-0 group-hover/row:opacity-60 hover:!opacity-100 active:scale-90 transition-[opacity,transform] p-0.5 rounded hover:bg-bg-hover"
                        onClick={(e) => {
                          e.stopPropagation();
                          onRevertCommit(commit.hash);
                        }}
                      >
                        <RevertIcon />
                      </button>
                    </span>
                  )}
                </div>
                <div className="flex items-center gap-2 text-xs text-fg-muted min-w-0">
                  <span className="truncate">{commit.author}</span>
                  <span>·</span>
                  <span>{formatRelativeDate(new Date(commit.timestamp))}</span>
                  {(() => {
                    const gc = commit as GraphCommit;
                    const ins = gc.insertions ?? 0;
                    const del = gc.deletions ?? 0;
                    if (ins === 0 && del === 0) return null;
                    return (
                      <>
                        <span>·</span>
                        <DiffStats additions={ins} deletions={del} className="text-[10px]" />
                      </>
                    );
                  })()}
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
                        {commit.short_hash}
                      </>
                    )}
                  </span>
                </div>
              </div>
            </div>
          );
        })}

      </div>
      {displayCommits.length === 0 && (
        <div className="flex items-start justify-center px-3 pt-3 pb-2 text-center text-xs text-fg-muted">
          No commits match your search.
        </div>
      )}
      {/* Load-more indicator at the bottom */}
      {loadingMore && (
        <div className="flex items-center justify-center py-3 text-xs text-fg-muted">
          Loading more commits…
        </div>
      )}
      <div className="sticky bottom-0 left-0 right-0 z-10 flex justify-center py-2 pointer-events-none">
        <div className="pointer-events-none inline-flex min-w-[18rem] max-w-[22rem] flex-col gap-2 px-2 isolate">
            {canCherryPick && (
              <div className="pointer-events-auto">
                <button
                  type="button"
                  data-testid="history-cherry-pick-button"
                  onClick={() => onCherryPickCommits([...selectedHashesInDisplayOrder].reverse())}
                  className="flex h-9 w-full cursor-pointer items-center justify-center gap-2 rounded-full border border-border bg-bg-surface/90 px-4 text-xs font-medium text-accent shadow-md transition-colors hover:bg-bg-hover"
                >
                  <CherryPickIcon />
                  Cherry-pick {selectedHashesInDisplayOrder.length} commit{selectedHashesInDisplayOrder.length === 1 ? "" : "s"}
                </button>
              </div>
            )}

            {/* "Jump back to present" floating action when browsing history */}
            {browsingHistory && onJumpToPresent && (
              <button
                onClick={onJumpToPresent}
                className="pointer-events-auto flex h-9 w-full cursor-pointer items-center justify-center rounded-full border border-border bg-bg-surface/90 px-4 text-xs font-medium text-accent shadow-md transition-colors hover:bg-bg-hover"
              >
                ← Jump back to present
              </button>
            )}
            <div
              className="pointer-events-auto flex items-center rounded-full border border-border bg-bg-surface/90 shadow-md"
            >
              <label htmlFor="history-search" className="sr-only">
                Search commits
              </label>
              <input
                id="history-search"
                data-testid="history-search-input"
                type="text"
                value={searchQuery}
                onChange={(event) => setSearchQuery(event.target.value)}
                onKeyDown={(event) => {
                  if (event.key !== "Escape" || searchQuery.length === 0) return;
                  event.preventDefault();
                  setSearchQuery("");
                }}
                placeholder="Search commits"
                className="h-9 w-full rounded-full bg-transparent px-4 text-xs text-fg placeholder:text-fg-muted outline-none"
              />
              {searchQuery.length > 0 && (
                <button
                  type="button"
                  data-testid="history-search-clear"
                  title="Clear search"
                  onClick={() => setSearchQuery("")}
                  className="mr-2 flex h-6 w-6 cursor-pointer items-center justify-center rounded-full text-fg-muted transition-colors hover:bg-bg-hover hover:text-fg"
                >
                  <span aria-hidden="true" className="text-sm leading-none">×</span>
                  <span className="sr-only">Clear search</span>
                </button>
              )}
            </div>
        </div>
      </div>
    </div>
  );
}
