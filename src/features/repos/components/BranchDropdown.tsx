import { useState, useEffect, useRef, useMemo } from "react";
import { commands, type BranchInfo } from "../../../ipc/bindings";
import { useClickOutside } from "../../../shared/hooks/useClickOutside";
import { BranchRow } from "./BranchRow";
import { GraphVisibilityBar } from "./GraphVisibilityBar";

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
  const [branches, setBranches] = useState<BranchInfo[]>([]);
  const [search, setSearch] = useState("");
  const [loading, setLoading] = useState(true);
  const [fetchKey, setFetchKey] = useState(0);
  const ref = useRef<HTMLDivElement>(null);
  const inputRef = useRef<HTMLInputElement>(null);

  useEffect(() => {
    let cancelled = false;
    async function fetch() {
      setLoading(true);
      const result = await commands.listBranches(repoPath);
      if (!cancelled && result.status === "ok") {
        setBranches(result.data);
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
  const exactMatch = branches.some(
    (b) => b.name.toLowerCase() === search.toLowerCase(),
  );

  return (
    <div
      ref={ref}
      data-testid="branch-dropdown"
      className="absolute left-0 top-full z-50 mt-1 w-72 rounded-md border border-border bg-bg-surface shadow-lg"
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

      <div className="max-h-64 overflow-auto py-1">
        {loading ? (
          <div className="px-3 py-2 text-xs text-fg-muted">Loading…</div>
        ) : filtered.length > 0 ? (
          <>
            {/* Show / Hide all toggle */}
            <GraphVisibilityBar />
            {filtered.map((b) => (
              <BranchRow
                key={b.name}
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
            ))}
            {search.trim() && !exactMatch && (
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
