import { useState, useEffect, useCallback } from "react";
import { open } from "@tauri-apps/plugin-dialog";
import { commands } from "../../../ipc/bindings";
import { useReposStore } from "../store";

interface CloneRepoDialogProps {
  open: boolean;
  onClose: () => void;
}

function repoNameFromUrl(rawUrl: string): string {
  const trimmed = rawUrl.trim().replace(/\/+$/, "");
  const lastSegment = trimmed.split(/[/:]/g).pop() ?? "";
  return lastSegment.replace(/\.git$/, "");
}

export function CloneRepoDialog({ open: isOpen, onClose }: CloneRepoDialogProps) {
  const [parentFolder, setParentFolder] = useState("");
  const [url, setUrl] = useState("");
  const [error, setError] = useState<string | null>(null);
  const [pathStatus, setPathStatus] = useState<{ ok: boolean; message: string } | null>(null);
  const [loading, setLoading] = useState(false);
  const openRepo = useReposStore((s) => s.openRepo);
  const lastParentFolder = useReposStore((s) => s.lastParentFolder);
  const setLastParentFolder = useReposStore((s) => s.setLastParentFolder);

  // Load the last-used parent folder on open.
  useEffect(() => {
    if (!isOpen) return;
    setUrl("");
    setError(null);
    setPathStatus(null);
    setParentFolder(lastParentFolder ?? "");
  }, [isOpen, lastParentFolder]);

  // Live validation whenever inputs change.
  useEffect(() => {
    const name = repoNameFromUrl(url);
    if (!parentFolder || !name) {
      setPathStatus(null);
      return;
    }
    let cancelled = false;
    commands.validateRepoPath(parentFolder, name).then((result) => {
      if (cancelled) return;
      if (result.status === "ok") {
        setPathStatus({ ok: true, message: `${parentFolder}/${name}` });
      } else {
        const err = result.error;
        setPathStatus({ ok: false, message: err.Io ?? err.Git ?? err.Other ?? "Invalid path" });
      }
    });
    return () => { cancelled = true; };
  }, [parentFolder, url]);

  const handleBrowse = useCallback(async () => {
    const selected = await open({ directory: true, multiple: false });
    if (selected) {
      setParentFolder(selected);
      setError(null);
    }
  }, []);

  const handleClone = useCallback(async () => {
    setError(null);
    if (!parentFolder) {
      setError("Please select a parent folder.");
      return;
    }
    if (!url.trim()) {
      setError("Please enter a repository URL.");
      return;
    }

    setLoading(true);
    const result = await commands.cloneRepository(url.trim(), parentFolder);
    if (result.status === "error") {
      const err = result.error;
      setError(err.Io ?? err.Git ?? err.Other ?? "Failed to clone repository");
      setLoading(false);
      return;
    }

    setLastParentFolder(parentFolder);
    await openRepo(result.data.path);
    setLoading(false);
    onClose();
  }, [parentFolder, url, openRepo, onClose, setLastParentFolder]);

  if (!isOpen) return null;

  const canClone = pathStatus?.ok && !error && !loading;

  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/50" onMouseDown={onClose}>
      <div
        className="w-[480px] rounded-lg border border-border bg-bg-surface p-6 shadow-xl"
        onMouseDown={(e) => e.stopPropagation()}
      >
        <h2 className="mb-4 text-lg font-semibold text-fg">Clone Repository</h2>

        <label className="mb-1 block text-sm text-fg-muted">Repository URL</label>
        <input
          type="text"
          value={url}
          onChange={(e) => {
            setUrl(e.target.value);
            setError(null);
          }}
          placeholder="https://github.com/user/repo.git"
          className="mb-4 w-full rounded border border-border bg-bg px-3 py-1.5 text-sm text-fg placeholder:text-fg-muted/50 focus:border-accent focus:outline-none"
        />

        <label className="mb-1 block text-sm text-fg-muted">Parent Folder</label>
        <div className="mb-1 flex gap-2">
          <input
            type="text"
            value={parentFolder}
            onChange={(e) => {
              setParentFolder(e.target.value);
              setError(null);
            }}
            placeholder="/home/user/projects"
            className="flex-1 rounded border border-border bg-bg px-3 py-1.5 text-sm text-fg placeholder:text-fg-muted/50 focus:border-accent focus:outline-none"
          />
          <button
            onClick={handleBrowse}
            className="shrink-0 rounded border border-border bg-bg-hover px-3 py-1.5 text-sm text-fg transition-colors hover:bg-bg-hover/80 cursor-pointer"
          >
            Browse…
          </button>
        </div>

        {error && <p className="mb-3 text-xs text-danger">{error}</p>}
        {!error && pathStatus && !pathStatus.ok && (
          <p className="mb-3 text-xs text-danger">{pathStatus.message}</p>
        )}
        {!error && pathStatus?.ok && (
          <p className="mb-3 text-xs text-fg-muted">
            Repository will be cloned to <span className="font-mono text-fg">{pathStatus.message}</span>
          </p>
        )}
        {!error && !pathStatus && <div className="mb-3" />}

        {loading && (
          <div className="mb-3 flex items-center gap-2 text-sm text-fg-muted">
            <svg className="h-4 w-4 animate-spin" viewBox="0 0 24 24" fill="none">
              <circle className="opacity-25" cx="12" cy="12" r="10" stroke="currentColor" strokeWidth="4" />
              <path className="opacity-75" fill="currentColor" d="M4 12a8 8 0 018-8V0C5.373 0 0 5.373 0 12h4z" />
            </svg>
            Cloning repository…
          </div>
        )}

        <div className="flex justify-end gap-2">
          <button
            onClick={onClose}
            disabled={loading}
            className="rounded border border-border px-4 py-1.5 text-sm text-fg transition-colors hover:bg-bg-hover disabled:opacity-50 cursor-pointer"
          >
            Cancel
          </button>
          <button
            onClick={handleClone}
            disabled={!canClone}
            className="rounded bg-accent px-4 py-1.5 text-sm font-medium text-accent-fg transition-colors hover:opacity-90 disabled:opacity-50 cursor-pointer"
          >
            {loading ? "Cloning…" : "Clone"}
          </button>
        </div>
      </div>
    </div>
  );
}
