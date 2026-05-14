import { useState, useEffect, useRef, useCallback } from "react";
import { commands, type RemoteInfo, type BranchTrackingStatus } from "../../../ipc/bindings";
import { useToastStore } from "../../../shared/stores/toast";

interface RemoteButtonProps {
  repoPath: string;
  tracking: BranchTrackingStatus | null;
  onComplete: () => void;
}

export function RemoteButton({ repoPath, tracking, onComplete }: RemoteButtonProps) {
  const [remotes, setRemotes] = useState<RemoteInfo[]>([]);
  const [activeRemote, setActiveRemote] = useState<string | null>(null);
  const [loading, setLoading] = useState(false);
  const [dropdownOpen, setDropdownOpen] = useState(false);
  const addToast = useToastStore((s) => s.addToast);
  const buttonRef = useRef<HTMLButtonElement>(null);
  const dropdownRef = useRef<HTMLDivElement>(null);

  const fetchRemotes = useCallback(async () => {
    const result = await commands.listRemotes(repoPath);
    if (result.status === "ok") {
      setRemotes(result.data);
      if (result.data.length > 0 && !activeRemote) {
        setActiveRemote(result.data[0].name);
      }
    }
  }, [repoPath, activeRemote]);

  useEffect(() => {
    fetchRemotes();
  }, [fetchRemotes]);

  useEffect(() => {
    const handler = (e: MouseEvent) => {
      const target = e.target as Node;
      if (
        dropdownRef.current && !dropdownRef.current.contains(target) &&
        buttonRef.current && !buttonRef.current.contains(target)
      ) {
        setDropdownOpen(false);
      }
    };
    document.addEventListener("mousedown", handler);
    return () => document.removeEventListener("mousedown", handler);
  }, []);

  if (remotes.length === 0) {
    return null;
  }

  const action = getAction(tracking);
  const label = getLabel(action, tracking, activeRemote);

  async function handleAction() {
    if (!activeRemote || loading) return;
    setLoading(true);

    let result;
    switch (action) {
      case "publish":
        result = await commands.publishBranch(repoPath, activeRemote);
        break;
      case "push":
        result = await commands.push(repoPath, activeRemote);
        break;
      case "pull":
        result = await commands.pull(repoPath, activeRemote);
        break;
      case "fetch":
        result = await commands.fetch(repoPath, activeRemote);
        break;
    }

    setLoading(false);

    if (result.status === "error") {
      const err = result.error;
      addToast(err.Git ?? err.Io ?? err.Other ?? `Failed to ${action}`);
    } else {
      onComplete();
    }
  }

  const hasMultipleRemotes = remotes.length > 1;

  return (
    <div className="relative flex items-center">
      <button
        ref={buttonRef}
        onClick={handleAction}
        disabled={loading || !activeRemote}
        className="flex items-center gap-1.5 rounded-l px-3 py-1.5 text-sm font-medium transition-colors hover:bg-bg-hover disabled:opacity-50 cursor-pointer"
        title={`${action} ${activeRemote ?? ""}`}
      >
        <ActionIcon action={action} loading={loading} />
        <span className="text-fg">{label}</span>
      </button>

      {hasMultipleRemotes && (
        <button
          onClick={() => setDropdownOpen(!dropdownOpen)}
          className="flex items-center rounded-r border-l border-border px-1.5 py-1 text-sm transition-colors hover:bg-bg-hover"
        >
          <ChevronIcon open={dropdownOpen} />
        </button>
      )}

      {dropdownOpen && hasMultipleRemotes && (
        <div
          ref={dropdownRef}
          className="absolute right-0 top-full z-50 mt-1 w-56 rounded-md border border-border bg-bg-surface shadow-lg"
        >
          <div className="py-1">
            {remotes.map((r) => (
              <button
                key={r.name}
                onClick={() => {
                  setActiveRemote(r.name);
                  setDropdownOpen(false);
                }}
                className={`flex w-full items-center gap-2 px-3 py-1.5 text-left text-sm transition-colors hover:bg-bg-hover ${
                  r.name === activeRemote ? "text-accent" : "text-fg"
                }`}
              >
                {r.name === activeRemote && <span className="text-accent">✓</span>}
                <span className={r.name === activeRemote ? "" : "ml-5"}>
                  {r.name}
                </span>
                <span className="ml-auto truncate text-xs text-fg-muted max-w-32">
                  {r.url}
                </span>
              </button>
            ))}
          </div>
        </div>
      )}
    </div>
  );
}

type RemoteAction = "publish" | "push" | "pull" | "fetch";

function getAction(tracking: BranchTrackingStatus | null): RemoteAction {
  if (!tracking) return "publish";
  if (tracking.behind > 0) return "pull";
  if (tracking.ahead > 0) return "push";
  return "fetch";
}

function getLabel(
  action: RemoteAction,
  tracking: BranchTrackingStatus | null,
  remote: string | null,
): string {
  const remoteName = remote ?? "remote";
  switch (action) {
    case "publish":
      return `Publish to ${remoteName}`;
    case "push":
      return `Push ${tracking?.ahead ?? 0} ↑ ${remoteName}`;
    case "pull":
      return `Pull ${tracking?.behind ?? 0} ↓ ${remoteName}`;
    case "fetch":
      return `Fetch ${remoteName}`;
  }
}

function ActionIcon({ action, loading }: { action: RemoteAction; loading: boolean }) {
  if (loading) {
    return (
      <svg className="h-4 w-4 animate-spin text-fg-muted" viewBox="0 0 16 16" fill="none">
        <circle cx="8" cy="8" r="6" stroke="currentColor" strokeWidth="2" opacity="0.3" />
        <path d="M14 8a6 6 0 0 0-6-6" stroke="currentColor" strokeWidth="2" strokeLinecap="round" />
      </svg>
    );
  }

  switch (action) {
    case "publish":
      return (
        <svg className="h-4 w-4 text-fg-muted" viewBox="0 0 16 16" fill="currentColor">
          <path d="M8 2.5l-3.5 3.5h2.5v4h2v-4h2.5L8 2.5zM3 12h10v1.5H3V12z" />
        </svg>
      );
    case "push":
      return (
        <svg className="h-4 w-4 text-fg-muted" viewBox="0 0 16 16" fill="currentColor">
          <path d="M8 2.5l-3.5 3.5h2.5v6h2v-6h2.5L8 2.5z" />
        </svg>
      );
    case "pull":
      return (
        <svg className="h-4 w-4 text-fg-muted" viewBox="0 0 16 16" fill="currentColor">
          <path d="M8 13.5l3.5-3.5H9v-6H7v6H4.5L8 13.5z" />
        </svg>
      );
    case "fetch":
      return (
        <svg className="h-4 w-4 text-fg-muted" viewBox="0 0 16 16" fill="currentColor">
          <path d="M12.5 8a4.5 4.5 0 1 0-1.4 3.27l.71.71A5.5 5.5 0 1 1 13.5 8h-1zM13 8l2 2-2 2" stroke="currentColor" strokeWidth="1" fill="none" />
        </svg>
      );
  }
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
