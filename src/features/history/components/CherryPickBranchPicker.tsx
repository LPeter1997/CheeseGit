import { useEffect, useMemo, useRef, useState } from "react";
import { commands, type BranchInfo } from "../../../ipc/bindings";
import { useClickOutside } from "../../../shared/hooks/useClickOutside";

interface CherryPickBranchPickerProps {
  repoPath: string;
  selectedCount: number;
  onSelect: (branchName: string, createBranch: boolean) => void;
  onClose: () => void;
  busy?: boolean;
}

export function CherryPickBranchPicker({
  repoPath,
  selectedCount,
  onSelect,
  onClose,
  busy = false,
}: CherryPickBranchPickerProps) {
  const [branches, setBranches] = useState<BranchInfo[]>([]);
  const [search, setSearch] = useState("");
  const [loading, setLoading] = useState(true);
  const panelRef = useRef<HTMLDivElement>(null);
  const inputRef = useRef<HTMLInputElement>(null);

  useClickOutside(useMemo(() => [panelRef], []), onClose);

  useEffect(() => {
    let cancelled = false;
    async function fetchBranches() {
      setLoading(true);
      const result = await commands.listBranches(repoPath);
      if (!cancelled) {
        if (result.status === "ok") {
          setBranches(result.data);
        } else {
          setBranches([]);
        }
        setLoading(false);
      }
    }
    fetchBranches();
    return () => {
      cancelled = true;
    };
  }, [repoPath]);

  useEffect(() => {
    inputRef.current?.focus();
  }, []);

  const filtered = branches.filter((branch) =>
    branch.name.toLowerCase().includes(search.toLowerCase()),
  );
  const trimmedSearch = search.trim();
  const exactMatch = branches.some(
    (branch) => branch.name.toLowerCase() === trimmedSearch.toLowerCase(),
  );

  return (
    <div className="absolute inset-0 z-50 flex items-start justify-center bg-bg/50 pt-12" data-testid="cherry-pick-target-overlay">
      <div
        ref={panelRef}
        className="w-80 rounded-md border border-border bg-bg-surface shadow-lg"
        data-testid="cherry-pick-target-picker"
      >
        <div className="border-b border-border px-3 py-2">
          <p className="text-xs font-medium text-fg">Cherry-pick {selectedCount} commit{selectedCount === 1 ? "" : "s"} to…</p>
        </div>

        <div className="border-b border-border p-2">
          <input
            ref={inputRef}
            type="text"
            value={search}
            onChange={(event) => setSearch(event.target.value)}
            placeholder="Find or create a branch…"
            data-testid="cherry-pick-branch-search"
            className="w-full rounded border border-border bg-bg px-2 py-1 text-sm text-fg placeholder:text-fg-muted focus:border-accent focus:outline-none"
            onKeyDown={(event) => {
              if (event.key === "Escape") onClose();
            }}
          />
        </div>

        <div className="max-h-64 overflow-auto py-1">
          {loading ? (
            <div className="px-3 py-2 text-xs text-fg-muted">Loading…</div>
          ) : filtered.length > 0 ? (
            filtered.map((branch) => (
              <button
                key={branch.name}
                type="button"
                disabled={busy}
                data-testid={`cherry-pick-branch-row-${branch.name}`}
                onClick={() => onSelect(branch.name, false)}
                className="flex w-full items-center gap-2 px-3 py-1.5 text-left text-sm text-fg transition-colors hover:bg-bg-hover disabled:cursor-default disabled:opacity-50"
              >
                {branch.is_current && <span className="text-accent">✓</span>}
                <span className={`${branch.is_current ? "" : "ml-[18px]"} truncate`}>{branch.name}</span>
              </button>
            ))
          ) : (
            <div className="px-3 py-2 text-xs text-fg-muted">No branch matching "{search}"</div>
          )}
        </div>

        {trimmedSearch && !exactMatch && (
          <div className="border-t border-border px-3 py-2">
            <button
              type="button"
              disabled={busy}
              data-testid="cherry-pick-create-branch-button"
              onClick={() => onSelect(trimmedSearch, true)}
              className="w-full cursor-pointer rounded bg-accent px-3 py-1.5 text-xs font-medium text-accent-fg transition-opacity hover:opacity-90 disabled:cursor-default disabled:opacity-60"
            >
              Create and cherry-pick to "{trimmedSearch}"
            </button>
          </div>
        )}
      </div>
    </div>
  );
}
