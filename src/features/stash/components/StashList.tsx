import { useRef, useState, type KeyboardEvent as ReactKeyboardEvent, type ReactNode } from "react";
import { useStashStore } from "../store";
import { useScrollClamp } from "../../../shared/hooks/useScrollClamp";
import type { StashEntry } from "../../../ipc/bindings";
import { DiffStats } from "../../../shared/components/DiffStats";
import { formatStashMessage } from "../utils/format-stash-message";

function ApplyIcon() {
  return (
    <svg width="12" height="12" viewBox="0 0 16 16" fill="currentColor" className="shrink-0">
      <path d="M3.25 2a.75.75 0 0 1 .75.75V8h6.19L8.47 6.28a.75.75 0 0 1 1.06-1.06l3 3a.75.75 0 0 1 0 1.06l-3 3a.75.75 0 0 1-1.06-1.06L10.19 9.5H3.25a.75.75 0 0 1-.75-.75V2.75A.75.75 0 0 1 3.25 2Z" />
      <path d="M2.75 10a.75.75 0 0 1 .75.75v1.5c0 .14.11.25.25.25h1.5a.75.75 0 0 1 0 1.5h-1.5A1.75 1.75 0 0 1 2 12.25v-1.5A.75.75 0 0 1 2.75 10Z" />
    </svg>
  );
}

function PopIcon() {
  return (
    <svg width="12" height="12" viewBox="0 0 16 16" fill="currentColor" className="shrink-0">
      <path fillRule="evenodd" d="M8 1.5a.75.75 0 0 1 .75.75V8.2l2-2a.75.75 0 1 1 1.06 1.06l-3.28 3.27a.75.75 0 0 1-1.06 0L4.2 7.26a.75.75 0 1 1 1.06-1.06l1.99 2V2.25A.75.75 0 0 1 8 1.5Z" clipRule="evenodd" />
      <path d="M3.5 11a.75.75 0 0 1 .75.75v.5c0 .14.11.25.25.25h7a.25.25 0 0 0 .25-.25v-.5a.75.75 0 0 1 1.5 0v.5A1.75 1.75 0 0 1 11.5 14h-7A1.75 1.75 0 0 1 2.75 12.25v-.5A.75.75 0 0 1 3.5 11Z" />
    </svg>
  );
}

function DropIcon() {
  return (
    <svg width="12" height="12" viewBox="0 0 16 16" fill="currentColor" className="shrink-0">
      <path fillRule="evenodd" d="M6 1.75A1.75 1.75 0 0 1 7.75 0h.5A1.75 1.75 0 0 1 10 1.75V2h3.25a.75.75 0 0 1 0 1.5h-.51l-.6 9.06A2 2 0 0 1 10.15 14.5h-4.3a2 2 0 0 1-1.99-1.94l-.6-9.06h-.51a.75.75 0 0 1 0-1.5H6v-.25Zm2.25-.25h-.5a.25.25 0 0 0-.25.25V2h1V1.75a.25.25 0 0 0-.25-.25ZM5.15 12.46a.5.5 0 0 0 .5.49h4.7a.5.5 0 0 0 .5-.49l.59-8.96H4.56l.59 8.96Z" clipRule="evenodd" />
      <path d="M6.75 5.75a.75.75 0 0 1 .75.75v3a.75.75 0 0 1-1.5 0v-3a.75.75 0 0 1 .75-.75Zm2.5 0a.75.75 0 0 1 .75.75v3a.75.75 0 0 1-1.5 0v-3a.75.75 0 0 1 .75-.75Z" />
    </svg>
  );
}

function CopyIcon() {
  return (
    <svg width="12" height="12" viewBox="0 0 16 16" fill="currentColor" className="shrink-0">
      <path d="M0 6.75C0 5.784.784 5 1.75 5h1.5a.75.75 0 0 1 0 1.5h-1.5a.25.25 0 0 0-.25.25v7.5c0 .138.112.25.25.25h7.5a.25.25 0 0 0 .25-.25v-1.5a.75.75 0 0 1 1.5 0v1.5A1.75 1.75 0 0 1 9.25 16h-7.5A1.75 1.75 0 0 1 0 14.25Z" />
      <path d="M5 1.75C5 .784 5.784 0 6.75 0h7.5C15.216 0 16 .784 16 1.75v7.5A1.75 1.75 0 0 1 14.25 11h-7.5A1.75 1.75 0 0 1 5 9.25Zm1.75-.25a.25.25 0 0 0-.25.25v7.5c0 .138.112.25.25.25h7.5a.25.25 0 0 0 .25-.25v-7.5a.25.25 0 0 0-.25-.25Z" />
    </svg>
  );
}

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
  const [copiedHash, setCopiedHash] = useState<string | null>(null);
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
            copied={copiedHash === stash.hash}
            selected={stash.index === selectedIndex}
            onClick={() => selectStash(stash.index, repoPath)}
            onApply={() => handleApply(stash.index)}
            onPop={() => handlePop(stash.index)}
            onDrop={() => handleDrop(stash.index)}
            onCopyHash={() => {
              navigator.clipboard.writeText(stash.hash);
              setCopiedHash(stash.hash);
              window.setTimeout(() => {
                setCopiedHash((current) => (current === stash.hash ? null : current));
              }, 1500);
            }}
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
  copied,
  selected,
  onClick,
  onApply,
  onPop,
  onDrop,
  onCopyHash,
  stats,
}: {
  stash: StashEntry;
  copied: boolean;
  selected: boolean;
  onClick: () => void;
  onApply: () => void;
  onPop: () => void;
  onDrop: () => void;
  onCopyHash: () => void;
  stats?: { additions: number; deletions: number };
}) {
  const date = new Date(stash.timestamp);
  const relative = formatRelativeTime(date);
  const parsed = formatStashMessage(stash.message);

  const handleKeyDown = (event: ReactKeyboardEvent<HTMLDivElement>) => {
    if (event.key !== "Enter" && event.key !== " ") return;
    event.preventDefault();
    onClick();
  };

  return (
    <div className="group/row border-b border-border">
      <div
        role="button"
        tabIndex={0}
        onClick={onClick}
        onKeyDown={handleKeyDown}
        data-testid="stash-row"
        className={`flex h-[50px] w-full min-w-0 cursor-pointer flex-col justify-center gap-0.5 px-3 text-left transition-colors focus-visible:outline-none focus-visible:ring-1 focus-visible:ring-accent ${
          selected ? "bg-accent/10 text-fg" : "text-fg hover:bg-bg-hover"
        }`}
      >
        <div className="flex items-center gap-2 min-w-0">
          <span className="flex min-w-0 flex-1 items-baseline">
            <span data-testid="stash-message" className="truncate text-sm font-medium" title={parsed.title}>{parsed.title}</span>
            {parsed.context && (
              <span className="ml-1 truncate text-[10px] text-fg-muted/70" title={parsed.context}>
                ({parsed.context})
              </span>
            )}
          </span>
          <span className="ml-auto flex gap-1" onClick={(e) => e.stopPropagation()}>
            <IconActionButton
              title="Apply without removing"
              testId="stash-apply"
              onClick={onApply}
              icon={<ApplyIcon />}
            />
            <IconActionButton
              title="Apply and remove"
              testId="stash-pop"
              onClick={onPop}
              icon={<PopIcon />}
            />
            <IconActionButton
              title="Delete this stash"
              testId="stash-drop"
              danger
              onClick={onDrop}
              icon={<DropIcon />}
            />
          </span>
        </div>
        <div className="flex min-w-0 items-center gap-2 text-xs text-fg-muted">
          <span className="truncate max-w-32 shrink" title={stash.author}>{stash.author}</span>
          <span>·</span>
          <span>{relative}</span>
          {stats && (stats.additions > 0 || stats.deletions > 0) && (
            <>
              <span>·</span>
              <DiffStats additions={stats.additions} deletions={stats.deletions} className="text-[10px]" />
            </>
          )}
          <span className="ml-auto flex h-4 min-w-[4.75rem] shrink-0 items-center justify-end gap-1 font-mono text-[10px] leading-none" onClick={(e) => e.stopPropagation()}>
            {copied ? (
              <span className="inline-flex h-4 items-center text-fg text-[10px] leading-none" style={{ animation: "alert-copied-fade 1.5s ease-out forwards" }}>
                Copied!
              </span>
            ) : (
              <>
                <button
                  type="button"
                  title="Copy full hash"
                  className="flex h-4 w-4 cursor-pointer items-center justify-center opacity-0 group-hover/row:opacity-60 hover:!opacity-100 active:scale-90 transition-[opacity,transform] p-0.5 rounded hover:bg-bg-hover"
                  onClick={onCopyHash}
                >
                  <CopyIcon />
                </button>
                <span className="leading-none">{stash.short_hash}</span>
              </>
            )}
          </span>
        </div>
      </div>
    </div>
  );
}

function IconActionButton({
  title,
  danger,
  testId,
  icon,
  onClick,
}: {
  title: string;
  danger?: boolean;
  testId?: string;
  icon: ReactNode;
  onClick: () => void;
}) {
  return (
    <button
      type="button"
      onClick={(e) => {
        e.stopPropagation();
        onClick();
      }}
      title={title}
      data-testid={testId}
      className={`cursor-pointer opacity-0 group-hover/row:opacity-60 hover:!opacity-100 active:scale-90 transition-[opacity,transform] p-0.5 rounded ${
        danger
          ? "text-danger/70 hover:bg-danger/10 hover:text-danger"
          : "hover:bg-bg-hover"
      }`}
    >
      {icon}
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
