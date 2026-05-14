import { useState, useRef, useEffect } from "react";
import { commands, type BranchInfo, type BranchTrackingStatus } from "../../../ipc/bindings";
import { formatRelativeDate } from "../../../shared/utils/format";
import { RemoteButton } from "./RemoteButton";

interface BranchBarProps {
  repoPath: string;
  currentBranch: string | null;
  tracking: BranchTrackingStatus | null;
  switching: boolean;
  onSwitch: (branchName: string) => void;
  onCreate: (branchName: string) => void;
  onRemoteComplete: () => void;
}

export function BranchBar({ repoPath, currentBranch, tracking, switching, onSwitch, onCreate, onRemoteComplete }: BranchBarProps) {
  const [open, setOpen] = useState(false);
  const toggleRef = useRef<HTMLButtonElement>(null);

  return (
    <div className="flex h-10 items-center border-b border-border bg-bg-surface px-3">
      <div className="relative">
        <button
          ref={toggleRef}
          onClick={() => setOpen(!open)}
          className="flex items-center gap-2 rounded px-2 py-1 text-sm transition-colors hover:bg-bg-hover"
        >
          <BranchIcon />
          <span className="font-medium text-fg">
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
        <span className="ml-3 text-xs text-fg-muted animate-pulse">
          Switching branch…
        </span>
      )}

      <div className="ml-auto">
        <RemoteButton repoPath={repoPath} tracking={tracking} onComplete={onRemoteComplete} />
      </div>
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
            {filtered.map((b) => (
              <button
                key={b.name}
                onClick={() => onSelect(b.name)}
                className={`flex w-full items-center gap-2 px-3 py-1.5 text-left text-sm transition-colors hover:bg-bg-hover ${
                  b.name === currentBranch ? "text-accent" : "text-fg"
                }`}
              >
                {b.name === currentBranch && (
                  <span className="text-accent">✓</span>
                )}
                <span className={b.name === currentBranch ? "" : "ml-5"}>
                  {b.name}
                </span>
                <span className="ml-auto text-xs text-fg-muted">
                  {formatRelativeDate(new Date(b.last_commit_date))}
                </span>
              </button>
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
