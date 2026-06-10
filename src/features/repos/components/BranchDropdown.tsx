import { useState, useEffect, useRef, useMemo, useCallback } from "react";
import { commands, type BranchInfo, type RemoteBranchInfo } from "../../../ipc/bindings";
import { useClickOutside } from "../../../shared/hooks/useClickOutside";
import { getCachedBranchList, setCachedBranchList } from "../branchListCache";
import { BranchRow } from "./BranchRow";
import { RemoteBranchRow } from "./RemoteBranchRow";
import { GraphVisibilityBar } from "./GraphVisibilityBar";

/** Fixed row height (px) used for list virtualization. */
const ROW_HEIGHT = 32;
/** Extra rows rendered above/below the viewport to avoid blank edges while scrolling. */
const OVERSCAN = 5;

type DropdownItem =
  | { kind: "local"; branch: BranchInfo }
  | { kind: "remote-header" }
  | { kind: "remote"; branch: RemoteBranchInfo };

/**
 * Dropdown menu for selecting, creating, and managing branches.
 * Includes search, branch visibility toggles, and batch creation.
 */
export function BranchDropdown({
  repoPath,
  currentBranch,
  toggleRef,
  onSelect,
  onCreate,
  onMerge,
  onDelete,
  onClose,
}: {
  repoPath: string;
  currentBranch: string | null;
  toggleRef: React.RefObject<HTMLButtonElement | null>;
  onSelect: (name: string) => void;
  onCreate: (name: string) => void;
  onMerge: (name: string) => void;
  onDelete: () => void;
  onClose: () => void;
}) {
  const [branches, setBranches] = useState<BranchInfo[]>(
    () => getCachedBranchList(repoPath)?.branches ?? [],
  );
  const [remoteBranches, setRemoteBranches] = useState<RemoteBranchInfo[]>(
    () => getCachedBranchList(repoPath)?.remoteBranches ?? [],
  );
  const [search, setSearch] = useState("");
  // Only show the empty "Loading…" state when we have nothing cached to render.
  const [loading, setLoading] = useState(() => getCachedBranchList(repoPath) === undefined);
  const [fetchKey, setFetchKey] = useState(0);
  const ref = useRef<HTMLDivElement>(null);
  const inputRef = useRef<HTMLInputElement>(null);

  useEffect(() => {
    let cancelled = false;
    async function fetch() {
      // Show the cached snapshot (if any) immediately and refresh in the
      // background; only block with a spinner when there's nothing to show.
      const cached = getCachedBranchList(repoPath);
      if (cached) {
        setBranches(cached.branches);
        setRemoteBranches(cached.remoteBranches);
        setLoading(false);
      } else {
        setLoading(true);
      }
      const [localResult, remoteResult] = await Promise.all([
        commands.listBranches(repoPath),
        commands.listRemoteBranches(repoPath),
      ]);
      if (!cancelled) {
        if (localResult.status === "ok") setBranches(localResult.data);
        if (remoteResult.status === "ok") setRemoteBranches(remoteResult.data);
        if (localResult.status === "ok" && remoteResult.status === "ok") {
          setCachedBranchList(repoPath, {
            branches: localResult.data,
            remoteBranches: remoteResult.data,
          });
        }
      }
      if (!cancelled) setLoading(false);
    }
    fetch();
    return () => { cancelled = true; };
  }, [repoPath, fetchKey]);

  useEffect(() => {
    inputRef.current?.focus();
  }, []);

  const outsideRefs = useMemo(() => [ref, toggleRef], [toggleRef]);
  useClickOutside(outsideRefs, onClose);

  const filtered = branches.filter((b) =>
    b.name.toLowerCase().includes(search.toLowerCase()),
  );
  const filteredRemote = remoteBranches.filter((b) =>
    b.name.toLowerCase().includes(search.toLowerCase()),
  );
  const exactMatch = branches.some(
    (b) => b.name.toLowerCase() === search.toLowerCase(),
  ) || remoteBranches.some(
    (b) => b.name.toLowerCase() === search.toLowerCase(),
  );
  const hasResults = filtered.length > 0 || filteredRemote.length > 0;

  // Flatten local branches, an optional "Remote" divider, and remote branches
  // into a single list so the whole thing can be virtualized as one scroller.
  const items = useMemo<DropdownItem[]>(() => {
    const list: DropdownItem[] = filtered.map((branch) => ({ kind: "local", branch }));
    if (filteredRemote.length > 0) {
      list.push({ kind: "remote-header" });
      for (const branch of filteredRemote) list.push({ kind: "remote", branch });
    }
    return list;
  }, [filtered, filteredRemote]);

  // Scroll position drives which rows are rendered (viewport virtualization).
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

  const refCallback = useCallback((el: HTMLDivElement | null) => {
    scrollRef.current = el;
    if (el) setViewportHeight(el.clientHeight);
  }, []);

  const totalHeight = items.length * ROW_HEIGHT;
  const startRow = Math.max(0, Math.floor(scrollTop / ROW_HEIGHT) - OVERSCAN);
  const endRow = Math.min(
    items.length,
    Math.ceil((scrollTop + (viewportHeight || ROW_HEIGHT * 8)) / ROW_HEIGHT) + OVERSCAN,
  );

  const showCreate = hasResults && search.trim().length > 0 && !exactMatch;

  return (
    <div
      ref={ref}
      data-testid="branch-dropdown"
      className="absolute left-0 top-full z-50 mt-1 w-96 rounded-md border border-border bg-bg-surface shadow-lg"
    >
      <div className="border-b border-border p-2">
        <input
          ref={inputRef}
          type="text"
          value={search}
          onChange={(e) => setSearch(e.target.value)}
          placeholder="Find or create a branch…"
          data-testid="branch-search"
          className="w-full rounded border border-border bg-bg px-2 py-1 text-sm text-fg placeholder:text-fg-muted focus:border-accent focus:outline-none"
          onKeyDown={(e) => {
            if (e.key === "Escape") {
              onClose();
            }
          }}
        />
      </div>

      <div className="max-h-64 overflow-auto py-1" ref={refCallback} onScroll={onScroll}>
        {loading ? (
          <div className="px-3 py-2 text-xs text-fg-muted">Loading…</div>
        ) : hasResults ? (
          <>
            {/* Show / Hide all toggle */}
            <GraphVisibilityBar />
            {/* Virtualized list — only rows near the viewport are in the DOM. */}
            <div className="relative" style={{ height: totalHeight }}>
              {items.slice(startRow, endRow).map((item, i) => {
                const rowIndex = startRow + i;
                const top = rowIndex * ROW_HEIGHT;
                if (item.kind === "remote-header") {
                  return (
                    <div
                      key="remote-header"
                      className="absolute left-0 right-0 flex items-center border-t border-border px-3 text-xs font-medium text-fg-muted"
                      style={{ top, height: ROW_HEIGHT }}
                      data-testid="remote-branches-header"
                    >
                      Remote
                    </div>
                  );
                }
                if (item.kind === "remote") {
                  const b = item.branch;
                  return (
                    <div
                      key={`${b.remote}/${b.name}`}
                      className="absolute left-0 right-0 flex items-stretch"
                      style={{ top, height: ROW_HEIGHT }}
                    >
                      <RemoteBranchRow
                        branch={b}
                        onSelect={() => {
                          onSelect(b.name);
                        }}
                        onMerge={() => {
                          onMerge(`${b.remote}/${b.name}`);
                        }}
                      />
                    </div>
                  );
                }
                const b = item.branch;
                return (
                  <div
                    key={b.name}
                    className="absolute left-0 right-0 flex items-stretch"
                    style={{ top, height: ROW_HEIGHT }}
                  >
                    <BranchRow
                      branch={b}
                      isCurrent={b.name === currentBranch}
                      repoPath={repoPath}
                      onSelect={onSelect}
                      onMerge={onMerge}
                      onDelete={() => {
                        setFetchKey((k) => k + 1);
                        onDelete();
                      }}
                    />
                  </div>
                );
              })}
            </div>
            {showCreate && (
              <div className="border-t border-border px-3 py-2">
                <button
                  onClick={() => onCreate(search.trim())}
                  data-testid="create-branch-button"
                  className="w-full rounded bg-accent px-3 py-1.5 text-xs font-medium text-accent-fg transition-colors hover:opacity-90"
                >
                  Create branch &ldquo;{search.trim()}&rdquo;
                </button>
              </div>
            )}
          </>
        ) : (
          <div className="px-3 py-2">
            <p className="text-xs text-fg-muted">
              No branch matching &ldquo;{search}&rdquo;
            </p>
            {search.trim() && (
              <button
                onClick={() => onCreate(search.trim())}
                data-testid="create-branch-button"
                className="mt-2 w-full rounded bg-accent px-3 py-1.5 text-xs font-medium text-accent-fg transition-colors hover:opacity-90"
              >
                Create branch &ldquo;{search.trim()}&rdquo;
              </button>
            )}
          </div>
        )}
      </div>
    </div>
  );
}
