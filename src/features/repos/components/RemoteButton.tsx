import { useState, useEffect, useRef, useCallback } from "react";
import { commands, type RemoteInfo, type BranchTrackingStatus, type AppError } from "../../../ipc/bindings";
import { useAlertStore } from "../../../shared/stores/alerts";
import { useClickOutside } from "../../../shared/hooks/useClickOutside";
import { extractErrorMessage } from "../../../shared/utils/errors";
import { SshPassphraseDialog } from "./SshPassphraseDialog";

function isSshAuthError(error: AppError): boolean {
  return "SshAuthRequired" in error;
}

interface RemoteButtonProps {
  repoPath: string;
  tracking: BranchTrackingStatus | null;
  disabled?: boolean;
  onComplete: () => void;
  onRemoteChange?: (remote: string | null) => void;
}

export function RemoteButton({ repoPath, tracking, disabled, onComplete, onRemoteChange }: RemoteButtonProps) {
  const [remotes, setRemotes] = useState<RemoteInfo[]>([]);
  const [activeRemote, setActiveRemote] = useState<string | null>(null);
  const [remoteTracking, setRemoteTracking] = useState<BranchTrackingStatus | null | undefined>(undefined);
  const [loading, setLoading] = useState(false);
  const [dropdownOpen, setDropdownOpen] = useState(false);
  const [sshDialogOpen, setSshDialogOpen] = useState(false);
  const [pendingRetry, setPendingRetry] = useState(false);
  const addAlert = useAlertStore((s) => s.addAlert);
  const buttonRef = useRef<HTMLButtonElement>(null);
  const dropdownRef = useRef<HTMLDivElement>(null);

  const fetchRemotes = useCallback(async () => {
    const result = await commands.listRemotes(repoPath);
    if (result.status === "ok") {
      setRemotes(result.data);
      if (result.data.length > 0 && !activeRemote) {
        const defaultRemote = result.data[0].name;
        setActiveRemote(defaultRemote);
        onRemoteChange?.(defaultRemote);
      }
    }
  }, [repoPath, activeRemote, onRemoteChange]);

  useEffect(() => {
    fetchRemotes();
  }, [fetchRemotes]);

  // When the active remote changes, fetch tracking status relative to that remote.
  useEffect(() => {
    if (!activeRemote || !repoPath) {
      setRemoteTracking(undefined);
      return;
    }
    // Check if the selected remote is the upstream remote — if so, use the prop directly.
    if (tracking && tracking.upstream.startsWith(activeRemote + "/")) {
      setRemoteTracking(undefined); // signal to use prop
      return;
    }
    // Query status relative to the selected remote.
    let cancelled = false;
    commands.getRemoteBranchStatus(repoPath, activeRemote).then((result) => {
      if (cancelled) return;
      if (result.status === "ok") {
        setRemoteTracking(result.data);
      } else {
        setRemoteTracking(null);
      }
    });
    return () => { cancelled = true; };
  }, [activeRemote, repoPath, tracking]);

  const closeDropdown = useCallback(() => setDropdownOpen(false), []);
  useClickOutside([dropdownRef, buttonRef], closeDropdown, dropdownOpen);

  if (remotes.length === 0) {
    return null;
  }

  // Use remote-specific tracking when available, otherwise fall back to upstream prop.
  const effectiveTracking = remoteTracking !== undefined ? remoteTracking : tracking;
  const action = getAction(effectiveTracking);
  const label = getLabel(action, effectiveTracking, activeRemote);

  async function runRemoteAction() {
    if (!activeRemote) return undefined;
    switch (action) {
      case "publish":
        return commands.publishBranch(repoPath, activeRemote);
      case "push":
        return commands.push(repoPath, activeRemote);
      case "pull":
        return commands.pull(repoPath, activeRemote);
      case "fetch":
        return commands.fetch(repoPath, activeRemote);
    }
  }

  async function handleAction() {
    if (!activeRemote || loading) return;
    setLoading(true);

    const result = await runRemoteAction();
    if (!result) {
      setLoading(false);
      return;
    }

    setLoading(false);

    if (result.status === "error") {
      if (isSshAuthError(result.error)) {
        setPendingRetry(true);
        setSshDialogOpen(true);
      } else {
        addAlert(extractErrorMessage(result.error, `Failed to ${action}`));
      }
    } else {
      onComplete();
    }
  }

  async function handleSshSuccess() {
    setSshDialogOpen(false);
    if (pendingRetry) {
      setPendingRetry(false);
      // Retry the operation now that the key is loaded
      setLoading(true);
      const result = await runRemoteAction();
      setLoading(false);
      if (!result) return;
      if (result.status === "error") {
        addAlert(extractErrorMessage(result.error, `Failed to ${action}`));
      } else {
        onComplete();
      }
    }
  }

  function handleSshCancel() {
    setSshDialogOpen(false);
    setPendingRetry(false);
  }

  function handleRemoteSelect(name: string) {
    setActiveRemote(name);
    setDropdownOpen(false);
    onRemoteChange?.(name);
  }

  const hasMultipleRemotes = remotes.length > 1;

  return (
    <div className="relative flex items-center">
      <button
        ref={buttonRef}
        onClick={handleAction}
        disabled={loading || !activeRemote || disabled}
        data-testid="remote-button"
        className="flex w-full items-center gap-1.5 rounded-l px-2 py-1.5 text-sm font-medium transition-colors hover:bg-bg-hover disabled:opacity-50 cursor-pointer"
        title={disabled ? "Sync disabled while viewing history" : `${action} ${activeRemote ?? ""}`}
      >
        <ActionIcon action={action} loading={loading} />
        <span className="min-w-0 truncate text-fg">{label}</span>
      </button>

      {hasMultipleRemotes && (
        <button
          onClick={() => setDropdownOpen(!dropdownOpen)}
          className="flex items-center rounded-r px-1.5 py-1 text-sm transition-colors hover:bg-bg-hover"
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
                onClick={() => handleRemoteSelect(r.name)}
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

      <SshPassphraseDialog
        open={sshDialogOpen}
        onSuccess={handleSshSuccess}
        onCancel={handleSshCancel}
      />
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
