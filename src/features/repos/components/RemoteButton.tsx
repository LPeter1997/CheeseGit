import { useState, useEffect, useRef, useCallback } from "react";
import { commands, type RemoteInfo, type BranchTrackingStatus } from "../../../ipc/bindings";
import { useAlertStore } from "../../../shared/stores/alerts";
import { useClickOutside } from "../../../shared/hooks/useClickOutside";
import { extractErrorMessage } from "../../../shared/utils/errors";
import { SshPassphraseDialog } from "./SshPassphraseDialog";
import { AddRemoteDialog } from "./AddRemoteDialog";
import { isSshAuthError } from "../hooks/useSshAuthRetry";

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
  const [addRemoteOpen, setAddRemoteOpen] = useState(false);
  const [deleteConfirm, setDeleteConfirm] = useState<string | null>(null);
  const addAlert = useAlertStore((s) => s.addAlert);
  const actionButtonRef = useRef<HTMLButtonElement>(null);
  const toggleButtonRef = useRef<HTMLButtonElement>(null);
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
      if (result.data.length === 0) {
        setActiveRemote(null);
        onRemoteChange?.(null);
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

  const closeDropdown = useCallback(() => {
    setDropdownOpen(false);
    setDeleteConfirm(null);
  }, []);
  useClickOutside([dropdownRef, actionButtonRef, toggleButtonRef], closeDropdown, dropdownOpen);

  const hasRemotes = remotes.length > 0;

  // Use remote-specific tracking when available, otherwise fall back to upstream prop.
  const effectiveTracking = hasRemotes
    ? (remoteTracking !== undefined ? remoteTracking : tracking)
    : null;
  const action = hasRemotes ? getAction(effectiveTracking) : "add";
  const label = hasRemotes
    ? getLabel(action as RemoteAction, effectiveTracking, activeRemote)
    : "Add remote";

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
    return undefined;
  }

  async function handleAction() {
    if (!hasRemotes) {
      setAddRemoteOpen(true);
      return;
    }
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
    setDeleteConfirm(null);
    onRemoteChange?.(name);
  }

  async function handleRemoveRemote(name: string) {
    const result = await commands.removeRemote(repoPath, name);
    if (result.status === "error") {
      addAlert(extractErrorMessage(result.error, "Failed to remove remote"));
    } else {
      setDeleteConfirm(null);
      // If we removed the active remote, pick the first remaining one
      if (name === activeRemote) {
        setActiveRemote(null);
      }
      await fetchRemotes();
      onComplete();
    }
  }

  function handleAddRemoteComplete() {
    setAddRemoteOpen(false);
    setDropdownOpen(false);
    // Reset activeRemote so fetchRemotes picks the first one
    setActiveRemote(null);
    fetchRemotes().then(() => onComplete());
  }

  return (
    <div className="relative flex items-center rounded border border-transparent focus-within:border-border">
      <button
        ref={actionButtonRef}
        onClick={handleAction}
        disabled={loading || (hasRemotes && !activeRemote) || (hasRemotes && disabled)}
        data-testid="remote-button"
        className="flex w-full items-center gap-1.5 px-2 py-1.5 text-sm font-medium transition-colors hover:bg-bg-hover disabled:opacity-50 cursor-pointer"
        title={
          !hasRemotes
            ? "Add a remote"
            : disabled
              ? "Sync disabled while viewing history"
              : `${action} ${activeRemote ?? ""}`
        }
      >
        {hasRemotes ? (
          <ActionIcon action={action as RemoteAction} loading={loading} />
        ) : (
          <PlusIcon />
        )}
        <span className="min-w-0 truncate text-fg">{label}</span>
      </button>

      <button
        ref={toggleButtonRef}
        onClick={() => {
          setDropdownOpen((open) => !open);
          setDeleteConfirm(null);
        }}
        data-testid="remote-dropdown-toggle"
        title={hasRemotes ? "Select remote" : "Remote options"}
        className="group/chevron flex cursor-pointer items-center px-1.5 py-1.5 text-sm"
      >
        <span className="rounded p-0.5 transition-colors group-hover/chevron:bg-bg-hover">
          <ChevronIcon open={dropdownOpen} />
        </span>
      </button>

      {dropdownOpen && (
        <div
          ref={dropdownRef}
          data-testid="remote-dropdown"
          className="absolute right-0 top-full z-50 mt-1 w-64 rounded-md border border-border bg-bg-surface shadow-lg"
        >
          <div className="py-1">
            {remotes.map((r) => (
              <div
                key={r.name}
                className={`flex w-full items-center gap-1 px-3 py-1.5 text-sm transition-colors hover:bg-bg-hover ${
                  r.name === activeRemote ? "text-accent" : "text-fg"
                }`}
                data-testid={`remote-row-${r.name}`}
              >
                <button
                  onClick={() => handleRemoteSelect(r.name)}
                  className="flex min-w-0 flex-1 items-center gap-2 text-left"
                >
                  {r.name === activeRemote && <span className="text-accent">✓</span>}
                  <span className={r.name === activeRemote ? "" : "ml-5"}>
                    {r.name}
                  </span>
                  <span className="ml-auto truncate text-xs text-fg-muted max-w-28" title={r.url}>
                    {r.url}
                  </span>
                </button>
                {deleteConfirm === r.name ? (
                  <div className="flex items-center gap-1 ml-1">
                    <button
                      onClick={(e) => { e.stopPropagation(); handleRemoveRemote(r.name); }}
                      data-testid={`remove-remote-confirm-${r.name}`}
                      className="rounded px-1.5 py-0.5 text-xs font-medium text-white bg-danger hover:opacity-90 cursor-pointer"
                      title="Confirm removal"
                    >
                      ✓
                    </button>
                    <button
                      onClick={(e) => { e.stopPropagation(); setDeleteConfirm(null); }}
                      data-testid={`remove-remote-cancel-${r.name}`}
                      className="rounded px-1.5 py-0.5 text-xs text-fg-muted hover:bg-bg-hover cursor-pointer"
                      title="Cancel"
                    >
                      ✕
                    </button>
                  </div>
                ) : (
                  <button
                    onClick={(e) => { e.stopPropagation(); setDeleteConfirm(r.name); }}
                    data-testid={`remove-remote-${r.name}`}
                    className="ml-1 flex-shrink-0 rounded p-1 text-fg-muted/40 transition-colors hover:text-danger cursor-pointer"
                    title={`Remove remote ${r.name}`}
                  >
                    <TrashIcon />
                  </button>
                )}
              </div>
            ))}
          </div>

          {/* Add remote button at the bottom */}
          <div className="border-t border-border px-3 py-2">
            <button
              onClick={() => {
                setDropdownOpen(false);
                setDeleteConfirm(null);
                setAddRemoteOpen(true);
              }}
              data-testid="add-remote-button"
              className="w-full rounded bg-accent px-3 py-1.5 text-xs font-medium text-accent-fg transition-colors hover:opacity-90 cursor-pointer"
            >
              Add remote
            </button>
          </div>
        </div>
      )}

      {addRemoteOpen && (
        <AddRemoteDialog
          repoPath={repoPath}
          existingRemotes={remotes}
          onClose={() => setAddRemoteOpen(false)}
          onAdded={handleAddRemoteComplete}
        />
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

function PlusIcon() {
  return (
    <svg className="h-4 w-4 text-fg-muted" viewBox="0 0 16 16" fill="currentColor">
      <path d="M8 2.5v11M2.5 8h11" stroke="currentColor" strokeWidth="1.5" fill="none" strokeLinecap="round" />
    </svg>
  );
}

function TrashIcon() {
  return (
    <svg className="h-3 w-3" viewBox="0 0 16 16" fill="currentColor">
      <path d="M5.5 5.5A.5.5 0 0 1 6 6v6a.5.5 0 0 1-1 0V6a.5.5 0 0 1 .5-.5zm5 0a.5.5 0 0 1 .5.5v6a.5.5 0 0 1-1 0V6a.5.5 0 0 1 .5-.5zm-7-2A1.5 1.5 0 0 1 5 2h6a1.5 1.5 0 0 1 1.5 1.5H14a.5.5 0 0 1 0 1h-.538l-.853 10.66A2 2 0 0 1 10.616 17H5.384a2 2 0 0 1-1.993-1.84L2.538 4.5H2a.5.5 0 0 1 0-1h1.5z" />
    </svg>
  );
}
