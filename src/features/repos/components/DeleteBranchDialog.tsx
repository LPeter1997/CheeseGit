import { useState, useEffect, useRef } from "react";
import { commands, type BranchDeleteInfo } from "../../../ipc/bindings";
import { useAlertStore } from "../../../shared/stores/alerts";
import { useClickOutside } from "../../../shared/hooks/useClickOutside";
import { extractErrorMessage } from "../../../shared/utils/errors";

/**
 * Dialog for confirming branch deletion, with optional remote cleanup.
 */
export function DeleteBranchDialog({
  branchName,
  repoPath,
  onClose,
  onDeleted,
}: {
  branchName: string;
  repoPath: string;
  onClose: () => void;
  onDeleted: () => void;
}) {
  const [info, setInfo] = useState<BranchDeleteInfo | null>(null);
  const [loadingInfo, setLoadingInfo] = useState(true);
  const [deleteRemote, setDeleteRemote] = useState(false);
  const [busy, setBusy] = useState(false);
  const ref = useRef<HTMLDivElement>(null);

  useEffect(() => {
    let cancelled = false;
    async function fetch() {
      const result = await commands.getBranchDeleteInfo(repoPath, branchName);
      if (cancelled) return;
      if (result.status === "ok") {
        setInfo(result.data);
      } else {
        useAlertStore.getState().addAlert(extractErrorMessage(result.error, "Failed to get branch info"));
      }
      setLoadingInfo(false);
    }
    fetch();
    return () => { cancelled = true; };
  }, [repoPath, branchName]);

  useClickOutside([ref], onClose);

  useEffect(() => {
    const handler = (e: KeyboardEvent) => {
      if (e.key === "Escape") onClose();
    };
    document.addEventListener("keydown", handler);
    return () => document.removeEventListener("keydown", handler);
  }, [onClose]);

  async function handleDelete() {
    setBusy(true);
    const result = await commands.deleteBranch(repoPath, branchName, true);
    if (result.status === "error") {
      useAlertStore.getState().addAlert(extractErrorMessage(result.error, "Failed to delete branch"));
      setBusy(false);
      return;
    }
    if (deleteRemote && info?.exists_on_remote && info.remote_name && info.remote_branch_name) {
      const remoteResult = await commands.deleteRemoteBranch(repoPath, info.remote_name, info.remote_branch_name);
      if (remoteResult.status === "error") {
        useAlertStore.getState().addAlert(extractErrorMessage(remoteResult.error, "Branch deleted locally, but failed to delete on remote"));
      }
    }
    setBusy(false);
    onDeleted();
  }

  return (
    <div data-testid="delete-branch-dialog-overlay" className="fixed inset-0 z-[100] flex items-center justify-center bg-black/40">
      <div ref={ref} data-testid="delete-branch-dialog" className="w-80 rounded-lg border border-border bg-bg-surface p-4 shadow-xl">
        <h3 className="text-sm font-semibold text-fg">
          Delete branch &ldquo;{branchName}&rdquo;?
        </h3>

        <p className="mt-2 text-xs text-fg-muted">
          Are you sure you want to delete this branch? This action cannot be undone.
        </p>

        {loadingInfo ? (
          <div className="mt-3 flex items-center gap-2 text-xs text-fg-muted">
            <span className="animate-spin">⟳</span>
            Checking remote…
          </div>
        ) : info?.exists_on_remote && info.remote_name ? (
          <label className="mt-3 flex items-center gap-2 text-xs text-fg-muted cursor-pointer">
            <input
              type="checkbox"
              checked={deleteRemote}
              onChange={(e) => setDeleteRemote(e.target.checked)}
              className="rounded"
            />
            Also delete on remote ({info.remote_name})
          </label>
        ) : null}

        <div className="mt-4 flex justify-end gap-2">
          <button
            onClick={onClose}
            disabled={busy}
            data-testid="delete-branch-cancel"
            className="rounded px-3 py-1.5 text-xs text-fg-muted transition-colors hover:bg-bg-hover cursor-pointer"
          >
            Cancel
          </button>
          <button
            onClick={handleDelete}
            disabled={busy}
            data-testid="delete-branch-confirm"
            className="rounded bg-danger px-3 py-1.5 text-xs font-medium text-white transition-colors hover:opacity-90 disabled:opacity-40 cursor-pointer"
          >
            {busy ? "Deleting…" : "Delete"}
          </button>
        </div>
      </div>
    </div>
  );
}
