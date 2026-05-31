import { useState, useEffect, useRef, useCallback } from "react";
import { commands, type RemoteInfo } from "../../../ipc/bindings";
import { useAlertStore } from "../../../shared/stores/alerts";
import { extractErrorMessage } from "../../../shared/utils/errors";
import { isSshAuthError } from "../hooks/useSshAuthRetry";
import { SshPassphraseDialog } from "./SshPassphraseDialog";

/**
 * Extract a suggested remote name from a URL.
 * For URLs like ssh://git@codeberg.org/... → "codeberg"
 * For URLs like git@github.com:... → "github"
 * For https://gitlab.com/... → "gitlab"
 */
function suggestRemoteName(url: string): string {
  try {
    // SSH-style: git@hostname:...
    const sshMatch = url.match(/^[^@]+@([^:]+):/);
    if (sshMatch) {
      return extractHostLabel(sshMatch[1]);
    }
    // ssh://git@hostname/... or https://hostname/...
    const urlMatch = url.match(/:\/\/(?:[^@]+@)?([^/]+)/);
    if (urlMatch) {
      return extractHostLabel(urlMatch[1]);
    }
  } catch {
    // fall through
  }
  return "";
}

function extractHostLabel(hostname: string): string {
  // Remove port if present
  const host = hostname.split(":")[0];
  // Split by dots, take the main domain part
  const parts = host.split(".");
  if (parts.length >= 2) {
    // e.g. "github.com" → "github", "git.sr.ht" → "sr"
    return parts[parts.length - 2];
  }
  return parts[0];
}

interface AddRemoteDialogProps {
  repoPath: string;
  existingRemotes: RemoteInfo[];
  onClose: () => void;
  onAdded: () => void;
}

export function AddRemoteDialog({
  repoPath,
  existingRemotes,
  onClose,
  onAdded,
}: AddRemoteDialogProps) {
  const isFirstRemote = existingRemotes.length === 0;
  const [url, setUrl] = useState("");
  const [name, setName] = useState(isFirstRemote ? "origin" : "");
  const [nameManuallyEdited, setNameManuallyEdited] = useState(isFirstRemote);
  const [busy, setBusy] = useState(false);
  const [sshDialogOpen, setSshDialogOpen] = useState(false);
  const addAlert = useAlertStore((s) => s.addAlert);
  const ref = useRef<HTMLDivElement>(null);
  const urlInputRef = useRef<HTMLInputElement>(null);

  useEffect(() => {
    setTimeout(() => urlInputRef.current?.focus(), 50);
  }, []);

  useEffect(() => {
    const handler = (e: KeyboardEvent) => {
      if (e.key === "Escape") onClose();
    };
    document.addEventListener("keydown", handler);
    return () => document.removeEventListener("keydown", handler);
  }, [onClose]);

  // Auto-suggest remote name when URL changes (unless user manually edited)
  useEffect(() => {
    if (!nameManuallyEdited && !isFirstRemote && url.trim()) {
      const suggested = suggestRemoteName(url.trim());
      if (suggested) {
        // Check if suggested name is taken, append a number if so
        const existingNames = new Set(existingRemotes.map((r) => r.name));
        let candidate = suggested;
        let counter = 2;
        while (existingNames.has(candidate)) {
          candidate = `${suggested}${counter}`;
          counter++;
        }
        setName(candidate);
      }
    }
  }, [url, nameManuallyEdited, isFirstRemote, existingRemotes]);

  const existingNames = new Set(existingRemotes.map((r) => r.name));
  const nameTaken = name.trim() !== "" && existingNames.has(name.trim());
  const nameEmpty = name.trim() === "";
  const urlEmpty = url.trim() === "";
  const nameValid = /^[a-zA-Z0-9._-]+$/.test(name.trim());
  const canSubmit = !urlEmpty && !nameEmpty && !nameTaken && nameValid && !busy;

  const handleAdd = useCallback(async () => {
    if (!canSubmit) return;
    setBusy(true);
    const result = await commands.addRemote(repoPath, name.trim(), url.trim());
    setBusy(false);

    if (result.status === "error") {
      if (isSshAuthError(result.error)) {
        setSshDialogOpen(true);
      } else {
        addAlert(extractErrorMessage(result.error, "Failed to add remote"));
      }
      return;
    }

    onAdded();
  }, [canSubmit, repoPath, name, url, addAlert, onAdded]);

  async function handleSshSuccess() {
    setSshDialogOpen(false);
    // Retry adding now that the key is loaded
    setBusy(true);
    const result = await commands.addRemote(repoPath, name.trim(), url.trim());
    setBusy(false);
    if (result.status === "error") {
      addAlert(extractErrorMessage(result.error, "Failed to add remote"));
    } else {
      onAdded();
    }
  }

  return (
    <div data-testid="add-remote-dialog-overlay" className="fixed inset-0 z-[100] flex items-center justify-center bg-black/40">
      <div ref={ref} data-testid="add-remote-dialog" className="w-96 rounded-lg border border-border bg-bg-surface p-4 shadow-xl">
        <h3 className="text-sm font-semibold text-fg">Add Remote</h3>

        <div className="mt-3 flex flex-col gap-3">
          <label className="flex flex-col gap-1">
            <span className="text-xs font-medium text-fg-muted">Remote URL</span>
            <input
              ref={urlInputRef}
              type="text"
              value={url}
              onChange={(e) => setUrl(e.target.value)}
              placeholder="https://github.com/user/repo.git"
              data-testid="add-remote-url"
              className="rounded border border-border bg-bg px-2 py-1.5 text-sm text-fg placeholder:text-fg-muted focus:border-accent focus:outline-none"
              onKeyDown={(e) => {
                if (e.key === "Enter") handleAdd();
              }}
            />
          </label>

          <label className="flex flex-col gap-1">
            <span className="text-xs font-medium text-fg-muted">Remote Name</span>
            <input
              type="text"
              value={name}
              onChange={(e) => {
                setName(e.target.value);
                setNameManuallyEdited(true);
              }}
              placeholder="origin"
              data-testid="add-remote-name"
              className={`rounded border px-2 py-1.5 text-sm text-fg placeholder:text-fg-muted focus:outline-none ${
                nameTaken || (name.trim() && !nameValid)
                  ? "border-danger focus:border-danger"
                  : "border-border focus:border-accent"
              } bg-bg`}
              onKeyDown={(e) => {
                if (e.key === "Enter") handleAdd();
              }}
            />
            {nameTaken && (
              <span className="text-xs text-danger" data-testid="add-remote-name-error">
                Remote &ldquo;{name.trim()}&rdquo; already exists
              </span>
            )}
            {name.trim() && !nameValid && !nameTaken && (
              <span className="text-xs text-danger" data-testid="add-remote-name-error">
                Name can only contain letters, numbers, dots, hyphens and underscores
              </span>
            )}
          </label>
        </div>

        <div className="mt-4 flex justify-end gap-2">
          <button
            onClick={onClose}
            disabled={busy}
            data-testid="add-remote-cancel"
            className="rounded px-3 py-1.5 text-xs text-fg-muted transition-colors hover:bg-bg-hover cursor-pointer"
          >
            Cancel
          </button>
          <button
            onClick={handleAdd}
            disabled={!canSubmit}
            data-testid="add-remote-confirm"
            className="rounded bg-accent px-3 py-1.5 text-xs font-medium text-accent-fg transition-colors hover:opacity-90 disabled:opacity-40 cursor-pointer"
          >
            {busy ? "Adding…" : "Add Remote"}
          </button>
        </div>
      </div>

      <SshPassphraseDialog
        open={sshDialogOpen}
        onSuccess={handleSshSuccess}
        onCancel={() => setSshDialogOpen(false)}
      />
    </div>
  );
}
