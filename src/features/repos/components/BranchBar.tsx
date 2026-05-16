import { useState, useRef, useEffect } from "react";
import { commands, type BranchInfo, type BranchTrackingStatus } from "../../../ipc/bindings";
import { formatRelativeDate } from "../../../shared/utils/format";
import { useHistoryStore } from "../../history";
import { RemoteButton } from "./RemoteButton";
import { OptionsMenu } from "./OptionsMenu";

interface BranchBarProps {
  repoPath: string;
  currentBranch: string | null;
  tracking: BranchTrackingStatus | null;
  switching: boolean;
  panelWidth: number;
  onSwitch: (branchName: string) => void;
  onCreate: (branchName: string) => void;
  onRemoteComplete: () => void;
}

export function BranchBar({ repoPath, currentBranch, tracking, switching, panelWidth, onSwitch, onCreate, onRemoteComplete }: BranchBarProps) {
  const [open, setOpen] = useState(false);
  const toggleRef = useRef<HTMLButtonElement>(null);

  return (
    <div className="flex h-10 items-center border-b border-border bg-bg-surface">
      <div style={{ width: panelWidth }} className="flex flex-shrink-0 items-center gap-1 border-r border-border px-2">
        <div className="relative flex-1 min-w-0">
          <button
            ref={toggleRef}
            onClick={() => setOpen(!open)}
            title={currentBranch ?? undefined}
            className="flex w-full items-center gap-2 rounded px-2 py-1.5 text-sm transition-colors hover:bg-bg-hover cursor-pointer"
          >
            <BranchIcon />
            <span className="min-w-0 flex-1 truncate font-medium text-fg">
              {currentBranch ?? "…"}
            </span>
            <ChevronIcon open={open} />
          </button>

          {open && (
            <BranchDropdown
              repoPath={repoPath}
              currentBranch={currentBranch}
              toggleRef={toggleRef}
              onSelect={(name) => {
                setOpen(false);
                if (name !== currentBranch) {
                  onSwitch(name);
                }
              }}
              onCreate={(name) => {
                setOpen(false);
                onCreate(name);
              }}
              onClose={() => setOpen(false)}
            />
          )}
        </div>

        {switching && (
          <span className="text-xs text-fg-muted animate-pulse">…</span>
        )}

        <div className="flex-1 min-w-0">
          <RemoteButton repoPath={repoPath} tracking={tracking} onComplete={onRemoteComplete} />
        </div>
      </div>

      <div className="flex-1" />

      <OptionsMenu />
    </div>
  );
}

function BranchDropdown({
  repoPath,
  currentBranch,
  toggleRef,
  onSelect,
  onCreate,
  onClose,
}: {
  repoPath: string;
  currentBranch: string | null;
  toggleRef: React.RefObject<HTMLButtonElement | null>;
  onSelect: (name: string) => void;
  onCreate: (name: string) => void;
  onClose: () => void;
}) {
  const [branches, setBranches] = useState<BranchInfo[]>([]);
  const [search, setSearch] = useState("");
  const [loading, setLoading] = useState(true);
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
  }, [repoPath]);

  useEffect(() => {
    inputRef.current?.focus();
  }, []);

  useEffect(() => {
    const handler = (e: MouseEvent) => {
      const target = e.target as Node;
      if (
        ref.current && !ref.current.contains(target) &&
        toggleRef.current && !toggleRef.current.contains(target)
      ) {
        onClose();
      }
    };
    document.addEventListener("mousedown", handler);
    return () => document.removeEventListener("mousedown", handler);
  }, [onClose, toggleRef]);

  const filtered = branches.filter((b) =>
    b.name.toLowerCase().includes(search.toLowerCase()),
  );
  const exactMatch = branches.some(
    (b) => b.name.toLowerCase() === search.toLowerCase(),
  );

  return (
    <div
      ref={ref}
      className="absolute left-0 top-full z-50 mt-1 w-72 rounded-md border border-border bg-bg-surface shadow-lg"
    >
      <div className="border-b border-border p-2">
        <input
          ref={inputRef}
          type="text"
          value={search}
          onChange={(e) => setSearch(e.target.value)}
          placeholder="Find or create a branch…"
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
                onSelect={onSelect}
              />
            ))}
            {search.trim() && !exactMatch && (
              <div className="border-t border-border px-3 py-2">
                <button
                  onClick={() => onCreate(search.trim())}
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

// ── Graph visibility bar (show/hide all) ────────────────────────────

function GraphVisibilityBar() {
  const visibleBranches = useHistoryStore((s) => s.visibleBranches);
  const allBranches = useHistoryStore((s) => s.allBranches);
  const showAll = useHistoryStore((s) => s.showAllBranches);
  const hideNonReq = useHistoryStore((s) => s.hideNonRequired);

  if (allBranches.length === 0) return null;

  const allVisible = visibleBranches.length >= allBranches.length;

  return (
    <div className="flex items-center justify-between border-b border-border px-3 py-1">
      <span className="text-[10px] uppercase tracking-wide text-fg-muted">Graph</span>
      <button
        onClick={(e) => {
          e.stopPropagation();
          if (allVisible) hideNonReq(); else showAll();
        }}
        className="text-[10px] text-fg-muted transition-colors hover:text-fg cursor-pointer"
      >
        {allVisible ? "Hide all" : "Show all"}
      </button>
    </div>
  );
}

// ── Single branch row with eye toggle ───────────────────────────────

function BranchRow({
  branch,
  isCurrent,
  onSelect,
}: {
  branch: BranchInfo;
  isCurrent: boolean;
  onSelect: (name: string) => void;
}) {
  const visibleBranches = useHistoryStore((s) => s.visibleBranches);
  const requiredBranches = useHistoryStore((s) => s.requiredBranches);
  const toggle = useHistoryStore((s) => s.toggleBranchVisibility);

  const isVisible = visibleBranches.includes(branch.name);
  const isRequired = requiredBranches.includes(branch.name);

  return (
    <div
      className={`flex w-full items-center gap-1 px-1 py-0.5 text-sm transition-colors hover:bg-bg-hover ${
        isCurrent ? "text-accent" : "text-fg"
      }`}
    >
      {/* Eye toggle */}
      <button
        onClick={(e) => {
          e.stopPropagation();
          if (!isRequired) toggle(branch.name);
        }}
        className={`flex-shrink-0 rounded p-1 transition-colors cursor-pointer ${
          isRequired
            ? "text-fg-muted/40 cursor-default"
            : isVisible
              ? "text-fg-muted hover:text-fg"
              : "text-fg-muted/30 hover:text-fg-muted"
        }`}
        title={isRequired ? "Required branch (always visible)" : isVisible ? "Hide from graph" : "Show in graph"}
      >
        <EyeIcon open={isVisible} />
      </button>

      {/* Branch name (click to switch) */}
      <button
        onClick={() => onSelect(branch.name)}
        className="flex min-w-0 flex-1 items-center gap-2 px-1 py-1 text-left cursor-pointer"
      >
        {isCurrent && <span className="text-accent">✓</span>}
        <span className={`truncate ${isCurrent ? "" : "ml-5"}`}>
          {branch.name}
        </span>
        <span className="ml-auto flex-shrink-0 text-xs text-fg-muted">
          {formatRelativeDate(new Date(branch.last_commit_date))}
        </span>
      </button>
    </div>
  );
}

function EyeIcon({ open }: { open: boolean }) {
  if (open) {
    return (
      <svg className="h-3.5 w-3.5" viewBox="0 0 16 16" fill="currentColor">
        <path d="M8 3.5C4.136 3.5 1.092 6.558.793 6.875a.5.5 0 0 0 0 .625C1.092 7.817 4.136 12.5 8 12.5s6.908-4.683 7.207-5a.5.5 0 0 0 0-.625C14.908 6.558 11.864 3.5 8 3.5zM8 11a3 3 0 1 1 0-6 3 3 0 0 1 0 6z" />
        <circle cx="8" cy="8" r="1.5" />
      </svg>
    );
  }
  return (
    <svg className="h-3.5 w-3.5" viewBox="0 0 16 16" fill="currentColor">
      <path d="M14.707 1.293a1 1 0 0 0-1.414 0L1.293 13.293a1 1 0 1 0 1.414 1.414L14.707 2.707a1 1 0 0 0 0-1.414z" />
      <path d="M8 3.5c-1.302 0-2.52.386-3.592.958l1.14 1.14A4.48 4.48 0 0 1 8 5a3 3 0 0 1 2.83 3.98l1.348 1.348C13.16 9.42 14.438 7.867 14.793 7.5a.5.5 0 0 0 0-.625C14.47 6.558 11.864 3.5 8 3.5zM1.207 6.875a.5.5 0 0 0 0 .625c.322.342 3.366 5 7.207 5 1.302 0 2.52-.386 3.592-.958l-1.14-1.14A4.48 4.48 0 0 1 8 11a3 3 0 0 1-2.83-3.98L3.822 5.672C2.84 6.58 1.562 8.133 1.207 6.875z" />
    </svg>
  );
}

function BranchIcon() {
  return (
    <svg className="h-4 w-4 text-fg-muted" viewBox="0 0 16 16" fill="currentColor">
      <path d="M11.75 2.5a.75.75 0 1 0 0 1.5.75.75 0 0 0 0-1.5zm-2.25.75a2.25 2.25 0 1 1 3 2.122V6A2.5 2.5 0 0 1 10 8.5H6a1 1 0 0 0-1 1v1.128a2.251 2.251 0 1 1-1.5 0V5.372a2.25 2.25 0 1 1 1.5 0v1.836A2.492 2.492 0 0 1 6 7h4a1 1 0 0 0 1-1v-.628A2.25 2.25 0 0 1 9.5 3.25zM4.25 12a.75.75 0 1 0 0 1.5.75.75 0 0 0 0-1.5zM3.5 3.25a.75.75 0 1 1 1.5 0 .75.75 0 0 1-1.5 0z" />
    </svg>
  );
}

function ChevronIcon({ open }: { open: boolean }) {
  return (
    <svg
      className={`h-3 w-3 text-fg-muted transition-transform ${open ? "rotate-180" : ""}`}
      viewBox="0 0 12 12"
      fill="currentColor"
    >
      <path d="M2.5 4.5L6 8l3.5-3.5" stroke="currentColor" strokeWidth="1.5" fill="none" strokeLinecap="round" strokeLinejoin="round" />
    </svg>
  );
}
