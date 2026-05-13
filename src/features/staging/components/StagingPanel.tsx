import { useStagingStore } from "../store";
import { useHistoryStore } from "../../history";
import { useDiffStore } from "../../diff/store";
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

  async function handleCommit() {
    const ok = await commitChanges(repoPath);
    if (ok) {
      fetchLog(repoPath);
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
        <div className="sticky top-0 bg-bg-surface px-3 py-1.5 text-xs font-medium text-fg-muted">
          Unstaged Changes
          {unstaged.length > 0 && (
            <span className="ml-1.5 rounded bg-bg-hover px-1 py-0.5 text-[10px]">
              {unstaged.length}
            </span>
          )}
        </div>
        {unstaged.length === 0 ? (
          <div className="px-3 py-3 text-center text-xs text-fg-muted">
            No unstaged changes.
          </div>
        ) : (
          <FileList entries={unstaged} actionIcon="stage" onAction={(path) => stageFile(repoPath, path)} onSelect={(path) => selectFile(repoPath, path)} selectedPath={selectedFile} />
        )}
      </div>

      {/* Stage all / Unstage all bar */}
      {(unstaged.length > 0 || staged.length > 0) && (
        <div className="flex items-center justify-center gap-2 border-b border-border px-3 py-1">
          <button
            onClick={() => stageAll(repoPath)}
            disabled={unstaged.length === 0}
            className="rounded px-2 py-0.5 text-xs text-fg-muted transition-colors hover:bg-bg-hover hover:text-fg disabled:opacity-40"
            title="Stage all changes"
          >
            Stage All ↓
          </button>
          <button
            onClick={() => unstageAll(repoPath)}
            disabled={staged.length === 0}
            className="rounded px-2 py-0.5 text-xs text-fg-muted transition-colors hover:bg-bg-hover hover:text-fg disabled:opacity-40"
            title="Unstage all changes"
          >
            ↑ Unstage All
          </button>
        </div>
      )}

      {/* Staged changes */}
      <div className="flex-1 overflow-auto border-b border-border">
        <div className="sticky top-0 bg-bg-surface px-3 py-1.5 text-xs font-medium text-fg-muted">
          Staged Changes
          {staged.length > 0 && (
            <span className="ml-1.5 rounded bg-bg-hover px-1 py-0.5 text-[10px]">
              {staged.length}
            </span>
          )}
        </div>
        {staged.length === 0 ? (
          <div className="px-3 py-3 text-center text-xs text-fg-muted">
            No staged changes.
          </div>
        ) : (
          <FileList entries={staged} actionIcon="unstage" onAction={(path) => unstageFile(repoPath, path)} onSelect={(path) => selectFile(repoPath, path)} selectedPath={selectedFile} />
        )}
      </div>

      {/* Commit form */}
      <div className="flex flex-col gap-1.5 p-2">
        <input
          type="text"
          value={summary}
          onChange={(e) => setSummary(e.target.value)}
          placeholder={defaultSummary || "Summary (required)"}
          className="w-full rounded border border-border bg-bg px-2 py-1 text-sm text-fg placeholder:text-fg-muted focus:border-accent focus:outline-none"
        />
        <textarea
          value={description}
          onChange={(e) => setDescription(e.target.value)}
          placeholder="Description"
          rows={2}
          className="w-full resize-none rounded border border-border bg-bg px-2 py-1 text-xs text-fg placeholder:text-fg-muted focus:border-accent focus:outline-none"
        />
        <button
          onClick={handleCommit}
          disabled={!canCommit}
          className="w-full rounded bg-accent py-1.5 text-xs font-medium text-accent-fg transition-colors hover:opacity-90 disabled:opacity-40"
        >
          {committing ? "Committing…" : `Commit to ${effectiveSummary ? "branch" : "…"}`}
        </button>
      </div>
    </div>
  );
}
