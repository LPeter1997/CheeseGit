import { useStagingStore } from "../store";
import { useHistoryStore } from "../../history";
import { useDiffStore } from "../../diff/store";
import { useDiffPrefetch } from "../../diff/hooks/useDiffPrefetch";
import { FileList } from "./FileList";

interface StagingPanelProps {
  repoPath: string;
}

export function StagingPanel({ repoPath }: StagingPanelProps) {
  const staged = useStagingStore((s) => s.staged);
  const unstaged = useStagingStore((s) => s.unstaged);
  const summary = useStagingStore((s) => s.summary);
  const description = useStagingStore((s) => s.description);
  const defaultSummary = useStagingStore((s) => s.defaultSummary);
  const setSummary = useStagingStore((s) => s.setSummary);
  const setDescription = useStagingStore((s) => s.setDescription);
  const commitChanges = useStagingStore((s) => s.commit);
  const committing = useStagingStore((s) => s.committing);
  const stageFile = useStagingStore((s) => s.stageFile);
  const unstageFile = useStagingStore((s) => s.unstageFile);
  const stageAll = useStagingStore((s) => s.stageAll);
  const unstageAll = useStagingStore((s) => s.unstageAll);
  const loading = useStagingStore((s) => s.loading);
  const fetchLog = useHistoryStore((s) => s.fetchLog);
  const selectFile = useDiffStore((s) => s.selectFile);
  const selectedFile = useDiffStore((s) => s.selectedFile);

  const effectiveSummary = summary || defaultSummary;
  const canCommit = staged.length > 0 && effectiveSummary.length > 0 && !committing;

  // Prefetch diffs for all files so selecting them is instant
  useDiffPrefetch(repoPath, unstaged, staged);

  async function handleCommit() {
    const ok = await commitChanges(repoPath);
    if (ok) {
      fetchLog(repoPath);
      useDiffStore.getState().clearSelection();
    }
  }

  function handleStageFile(path: string) {
    // Find next unstaged file before performing the action
    const nextFile = unstaged.find((e) => e.path !== path);
    stageFile(repoPath, path);
    // Navigate to next unstaged file, or clear if none remain
    if (selectedFile === path) {
      if (nextFile) {
        selectFile(repoPath, nextFile.path, "Unstaged");
      } else {
        useDiffStore.getState().clearSelection();
      }
    }
  }

  function handleUnstageFile(path: string) {
    // Find next staged file before performing the action
    const nextFile = staged.find((e) => e.path !== path);
    unstageFile(repoPath, path);
    // Navigate to next staged file, or clear if none remain
    if (selectedFile === path) {
      if (nextFile) {
        selectFile(repoPath, nextFile.path, "Staged");
      } else {
        useDiffStore.getState().clearSelection();
      }
    }
  }

  if (loading) {
    return (
      <div className="flex h-full items-center justify-center text-xs text-fg-muted">
        Loading…
      </div>
    );
  }

  return (
    <div className="flex h-full flex-col">
      {/* Unstaged changes */}
      <div className="flex-1 overflow-auto border-b border-border">
        <div className="sticky top-0 bg-bg-surface px-3 py-2 text-xs font-medium text-fg-muted">
          Unstaged Changes
          {unstaged.length > 0 && (
            <span className="ml-2 rounded bg-bg-hover px-1.5 py-0.5 text-[10px]">
              {unstaged.length}
            </span>
          )}
        </div>
        {unstaged.length === 0 ? (
          <div className="px-3 py-3 text-center text-xs text-fg-muted">
            No unstaged changes.
          </div>
        ) : (
          <FileList entries={unstaged} actionIcon="stage" onAction={(path) => handleStageFile(path)} onSelect={(path) => selectFile(repoPath, path, "Unstaged")} selectedPath={selectedFile} />
        )}
      </div>

      {/* Stage all / Unstage all bar */}
      {(unstaged.length > 0 || staged.length > 0) && (
        <div className="flex items-center justify-center gap-3 border-b border-border px-3 py-2.5">
          <button
            onClick={() => stageAll(repoPath)}
            disabled={unstaged.length === 0}
            className="rounded px-3 py-1.5 text-xs text-fg-muted transition-colors hover:bg-bg-hover hover:text-fg disabled:opacity-40 cursor-pointer"
            title="Stage all changes"
          >
            Stage All ↓
          </button>
          <button
            onClick={() => unstageAll(repoPath)}
            disabled={staged.length === 0}
            className="rounded px-3 py-1.5 text-xs text-fg-muted transition-colors hover:bg-bg-hover hover:text-fg disabled:opacity-40 cursor-pointer"
            title="Unstage all changes"
          >
            ↑ Unstage All
          </button>
        </div>
      )}

      {/* Staged changes */}
      <div className="flex-1 overflow-auto border-b border-border">
        <div className="sticky top-0 bg-bg-surface px-3 py-2 text-xs font-medium text-fg-muted">
          Staged Changes
          {staged.length > 0 && (
            <span className="ml-2 rounded bg-bg-hover px-1.5 py-0.5 text-[10px]">
              {staged.length}
            </span>
          )}
        </div>
        {staged.length === 0 ? (
          <div className="px-3 py-3 text-center text-xs text-fg-muted">
            No staged changes.
          </div>
        ) : (
          <FileList entries={staged} actionIcon="unstage" onAction={(path) => handleUnstageFile(path)} onSelect={(path) => selectFile(repoPath, path, "Staged")} selectedPath={selectedFile} />
        )}
      </div>

      {/* Commit form */}
      <div className="flex flex-col gap-2 border-t border-border p-3">
        <input
          type="text"
          value={summary}
          onChange={(e) => setSummary(e.target.value)}
          placeholder={defaultSummary || "Summary (required)"}
          className="w-full rounded border border-border bg-bg px-2.5 py-1.5 text-sm text-fg placeholder:text-fg-muted focus:border-accent focus:outline-none"
        />
        <textarea
          value={description}
          onChange={(e) => setDescription(e.target.value)}
          placeholder="Description"
          rows={2}
          className="w-full resize-none rounded border border-border bg-bg px-2.5 py-1.5 text-xs text-fg placeholder:text-fg-muted focus:border-accent focus:outline-none"
        />
        <button
          onClick={handleCommit}
          disabled={!canCommit}
          className="w-full rounded bg-accent py-2 text-sm font-medium text-accent-fg transition-colors hover:opacity-90 disabled:opacity-40 cursor-pointer"
        >
          {committing ? "Committing…" : `Commit to ${effectiveSummary ? "branch" : "…"}`}
        </button>
      </div>
    </div>
  );
}
