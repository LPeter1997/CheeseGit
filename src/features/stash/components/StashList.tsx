import { useRef, useState } from "react";
import { useStashStore } from "../store";
import { useScrollClamp } from "../../../shared/hooks/useScrollClamp";
import type { StashEntry } from "../../../ipc/bindings";
import { DiffStats } from "../../../shared/components/DiffStats";
import { formatStashMessage } from "../utils/format-stash-message";

interface StashListProps {
  repoPath: string;
  onApply?: () => void;
}

export function StashList({ repoPath, onApply }: StashListProps) {
  const stashes = useStashStore((s) => s.stashes);
  const stashEntryStats = useStashStore((s) => s.stashEntryStats);
  const loading = useStashStore((s) => s.loading);
  const selectedIndex = useStashStore((s) => s.selectedIndex);
  const selectStash = useStashStore((s) => s.selectStash);
  const applyStash = useStashStore((s) => s.applyStash);
  const popStash = useStashStore((s) => s.popStash);
  const dropStash = useStashStore((s) => s.dropStash);
  const [confirm, setConfirm] = useState<{ message: string; onConfirm: () => void } | null>(null);
  const listRef = useRef<HTMLDivElement>(null);
  useScrollClamp(listRef, stashes.length);

  if (loading) {
    return (
      <div className="flex h-full items-center justify-center text-xs text-fg-muted">
        Loading…
      </div>
    );
  }

  if (stashes.length === 0) {
    return (
      <div className="flex h-full items-center justify-center text-xs text-fg-muted">
        No stashes
      </div>
    );
  }

  function handleApply(index: number) {
    setConfirm({
      message: "Apply this stash without removing it?",
      onConfirm: async () => {
        setConfirm(null);
        const ok = await applyStash(repoPath, index);
        if (ok) onApply?.();
      },
    });
  }

  function handlePop(index: number) {
    setConfirm({
      message: "Apply and remove this stash?",
      onConfirm: async () => {
        setConfirm(null);
        const ok = await popStash(repoPath, index);
        if (ok) onApply?.();
      },
    });
  }

  function handleDrop(index: number) {
    setConfirm({
      message: "Delete this stash? This cannot be undone.",
      onConfirm: async () => {
        setConfirm(null);
        await dropStash(repoPath, index);
      },
    });
  }

  return (
    <>
      <div ref={listRef} className="flex h-full flex-col overflow-auto">
        {stashes.map((stash) => (
          <StashRow
            key={stash.hash}
            stash={stash}
            selected={stash.index === selectedIndex}
            onClick={() => selectStash(stash.index, repoPath)}
            onApply={() => handleApply(stash.index)}
            onPop={() => handlePop(stash.index)}
            onDrop={() => handleDrop(stash.index)}
            stats={stashEntryStats.get(stash.index)}
          />
        ))}
      </div>

      {confirm && (
        <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/40">
          <div className="w-80 rounded-lg border border-border bg-bg-surface p-4 shadow-xl">
            <p className="mb-4 text-sm text-fg">{confirm.message}</p>
            <div className="flex justify-end gap-2">
              <button
                onClick={() => setConfirm(null)}
                data-testid="cancel-button"
                className="rounded px-3 py-1.5 text-xs text-fg-muted transition-colors hover:bg-bg-hover cursor-pointer"
              >
                Cancel
              </button>
              <button
                onClick={confirm.onConfirm}
                data-testid="confirm-button"
                className="rounded bg-accent px-3 py-1.5 text-xs text-accent-fg transition-colors hover:opacity-90 cursor-pointer"
              >
                Confirm
              </button>
            </div>
          </div>
        </div>
      )}
    </>
  );
}

function StashRow({
  stash,
  selected,
  onClick,
  onApply,
  onPop,
  onDrop,
  stats,
}: {
  stash: StashEntry;
  selected: boolean;
  onClick: () => void;
  onApply: () => void;
  onPop: () => void;
  onDrop: () => void;
  stats?: { additions: number; deletions: number };
}) {
  const date = new Date(stash.timestamp);
  const relative = formatRelativeTime(date);
  const parsed = formatStashMessage(stash.message);

  return (
    <button
      onClick={onClick}
      data-testid="stash-row"
      className={`group flex w-full flex-col gap-0.5 border-b border-border px-3 py-2 text-left transition-colors cursor-pointer ${
        selected ? "bg-accent/10" : "hover:bg-bg-hover"
      }`}
    >
      <div className="flex items-baseline gap-2">
        <span data-testid="stash-message" className="flex-1 truncate text-xs text-fg">
          {parsed.title}
        </span>
        <span className="flex-shrink-0 font-mono text-[10px] text-fg-muted">
          {stash.short_hash}
        </span>
      </div>
      <div className="flex items-center gap-2">
        <span className="text-[10px] text-fg-muted">
          {relative}
        </span>
        {parsed.context && (
          <span className="truncate text-[10px] text-fg-muted" title={parsed.context}>
            {parsed.context}
          </span>
        )}
        <span className="text-[10px] text-fg-muted">
          {stash.author}
        </span>
        {stats && (stats.additions > 0 || stats.deletions > 0) && (
          <DiffStats additions={stats.additions} deletions={stats.deletions} className="text-[10px]" />
        )}
        <div className="ml-auto flex gap-1 opacity-0 group-hover:opacity-100 transition-opacity">
          <ActionButton label="Apply" title="Apply without removing" testId="stash-apply" onClick={(e) => { e.stopPropagation(); onApply(); }} />
          <ActionButton label="Pop" title="Apply and remove" testId="stash-pop" onClick={(e) => { e.stopPropagation(); onPop(); }} />
          <ActionButton label="Drop" title="Delete this stash" testId="stash-drop" danger onClick={(e) => { e.stopPropagation(); onDrop(); }} />
        </div>
      </div>
    </button>
  );
}

function ActionButton({
  label,
  title,
  danger,
  testId,
  onClick,
}: {
  label: string;
  title: string;
  danger?: boolean;
  testId?: string;
  onClick: (e: React.MouseEvent) => void;
}) {
  return (
    <button
      onClick={onClick}
      title={title}
      data-testid={testId}
      className={`rounded px-1.5 py-0.5 text-[10px] transition-colors cursor-pointer ${
        danger
          ? "text-danger/70 hover:bg-danger/10 hover:text-danger"
          : "text-fg-muted hover:bg-bg-hover hover:text-fg"
      }`}
    >
      {label}
    </button>
  );
}

function formatRelativeTime(date: Date): string {
  const now = Date.now();
  const diffMs = now - date.getTime();
  const seconds = Math.floor(diffMs / 1000);
  const minutes = Math.floor(seconds / 60);
  const hours = Math.floor(minutes / 60);
  const days = Math.floor(hours / 24);

  if (days > 0) return `${days}d ago`;
  if (hours > 0) return `${hours}h ago`;
  if (minutes > 0) return `${minutes}m ago`;
  return "just now";
}
