import { useState, useEffect, useCallback } from "react";
import { open } from "@tauri-apps/plugin-dialog";
import { commands } from "../../../ipc/bindings";
import { useReposStore } from "../store";

interface CreateRepoDialogProps {
  open: boolean;
  onClose: () => void;
}

export function CreateRepoDialog({ open: isOpen, onClose }: CreateRepoDialogProps) {
  const [parentFolder, setParentFolder] = useState("");
  const [repoName, setRepoName] = useState("");
  const [error, setError] = useState<string | null>(null);
  const [pathStatus, setPathStatus] = useState<{ ok: boolean; message: string } | null>(null);
  const [loading, setLoading] = useState(false);
  const openRepo = useReposStore((s) => s.openRepo);
  const lastParentFolder = useReposStore((s) => s.lastParentFolder);
  const setLastParentFolder = useReposStore((s) => s.setLastParentFolder);

  // Load the last-used parent folder on open.
  useEffect(() => {
    if (!isOpen) return;
    setRepoName("");
    setError(null);
    setPathStatus(null);
    setParentFolder(lastParentFolder ?? "");
  }, [isOpen, lastParentFolder]);

  // Live validation whenever inputs change.
  useEffect(() => {
    const name = repoName.trim();
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
  }, [parentFolder, repoName]);

  const handleBrowse = useCallback(async () => {
    const selected = await open({ directory: true, multiple: false });
    if (selected) {
      setParentFolder(selected);
      setError(null);
    }
  }, []);

  const handleCreate = useCallback(async () => {
    setError(null);
    if (!parentFolder) {
      setError("Please select a parent folder.");
      return;
    }
    if (!repoName.trim()) {
      setError("Please enter a repository name.");
      return;
    }

    setLoading(true);
    const result = await commands.initRepository(parentFolder, repoName.trim());
    if (result.status === "error") {
      const err = result.error;
      setError(err.Io ?? err.Git ?? err.Other ?? "Failed to create repository");
      setLoading(false);
      return;
    }

    setLastParentFolder(parentFolder);
    await openRepo(result.data.path);
    setLoading(false);
    onClose();
  }, [parentFolder, repoName, openRepo, onClose, setLastParentFolder]);

  if (!isOpen) return null;

  const canCreate = pathStatus?.ok && !error && !loading;

  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/50" onMouseDown={onClose}>
      <div
        className="w-[480px] rounded-lg border border-border bg-bg-surface p-6 shadow-xl"
        onMouseDown={(e) => e.stopPropagation()}
      >
        <h2 className="mb-4 text-lg font-semibold text-fg">Create New Repository</h2>

        <label className="mb-1 block text-sm text-fg-muted">Parent Folder</label>
        <div className="mb-4 flex gap-2">
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

        <label className="mb-1 block text-sm text-fg-muted">Repository Name</label>
        <input
          type="text"
          value={repoName}
          onChange={(e) => {
            setRepoName(e.target.value);
            setError(null);
          }}
          placeholder="my-new-repo"
          className="mb-1 w-full rounded border border-border bg-bg px-3 py-1.5 text-sm text-fg placeholder:text-fg-muted/50 focus:border-accent focus:outline-none"
          onKeyDown={(e) => {
            if (e.key === "Enter" && canCreate) handleCreate();
          }}
        />
        {error && <p className="mb-3 text-xs text-danger">{error}</p>}
        {!error && pathStatus && !pathStatus.ok && (
          <p className="mb-3 text-xs text-danger">{pathStatus.message}</p>
        )}
        {!error && pathStatus?.ok && (
          <p className="mb-3 text-xs text-fg-muted">
            Repository will be created at <span className="font-mono text-fg">{pathStatus.message}</span>
          </p>
        )}
        {!error && !pathStatus && <div className="mb-3" />}

        <div className="flex justify-end gap-2">
          <button
            onClick={onClose}
            className="rounded border border-border px-4 py-1.5 text-sm text-fg transition-colors hover:bg-bg-hover cursor-pointer"
          >
            Cancel
          </button>
          <button
            onClick={handleCreate}
            disabled={!canCreate}
            className="rounded bg-accent px-4 py-1.5 text-sm font-medium text-accent-fg transition-colors hover:opacity-90 disabled:opacity-50 cursor-pointer"
          >
            {loading ? "Creating…" : "Create"}
          </button>
        </div>
      </div>
    </div>
  );
}
