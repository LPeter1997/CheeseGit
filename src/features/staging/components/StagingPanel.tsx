import { useEffect, useState } from "react";
import { useStagingStore } from "../store";
import { useHistoryStore } from "../../history";
import { useDiffStore } from "../../diff/store";
import { useDiffPrefetch } from "../../diff/hooks/useDiffPrefetch";
import { useResize } from "../../../shared/hooks/useResize";
import { useShiftKey } from "../../../shared/hooks/useShiftKey";
import { FileList } from "./FileList";

interface ConfirmState {
  message: string;
  onConfirm: () => void;
}

interface StagingPanelProps {
  repoPath: string;
  currentBranch: string | null;
  browsingHistory?: boolean;
  onCommit?: () => void;
}

export function StagingPanel({ repoPath, currentBranch, browsingHistory, onCommit }: StagingPanelProps) {
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
  const discardFile = useStagingStore((s) => s.discardFile);
  const discardAll = useStagingStore((s) => s.discardAll);
  const loading = useStagingStore((s) => s.loading);
  const emptyCommitMode = useStagingStore((s) => s.emptyCommitMode);
  const enableEmptyCommit = useStagingStore((s) => s.enableEmptyCommit);
  const stagedStats = useStagingStore((s) => s.stagedStats);
  const unstagedStats = useStagingStore((s) => s.unstagedStats);
  const fetchLog = useHistoryStore((s) => s.fetchLog);
  const selectFile = useDiffStore((s) => s.selectFile);
  const selectedFile = useDiffStore((s) => s.selectedFile);
  const [confirm, setConfirm] = useState<ConfirmState | null>(null);
  const shiftHeld = useShiftKey();

  const effectiveSummary = summary || defaultSummary;
  const canCommit = !browsingHistory && (emptyCommitMode || staged.length > 0) && effectiveSummary.length > 0 && !committing;

  const { size: commitHeight, onMouseDown: onResizeCommit } = useResize({
    direction: "vertical",
    initialSize: 160,
    minSize: 160,
    maxSize: 400,
  });

  // Prefetch diffs for all files so selecting them is instant
  useDiffPrefetch(repoPath, unstaged, staged);

  // Clear diff selection if the selected file no longer exists in either list
  // (e.g., after an external change or commit detected by polling).
  useEffect(() => {
    const { selectedFile } = useDiffStore.getState();
    if (!selectedFile) return;
    const exists =
      staged.some((e) => e.path === selectedFile) ||
      unstaged.some((e) => e.path === selectedFile);
    if (!exists) {
      useDiffStore.getState().clearSelection();
    }
  }, [staged, unstaged]);

  async function handleCommit() {
    const ok = await commitChanges(repoPath);
    if (ok) {
      fetchLog(repoPath);
      useDiffStore.getState().clearSelection();
      onCommit?.();
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

  function handleDiscardFile(path: string, area: "Unstaged" | "Staged") {
    const filename = path.split("/").pop() ?? path;
    setConfirm({
      message: `Discard changes to "${filename}"? This cannot be undone.`,
      onConfirm: () => {
        discardFile(repoPath, path, area);
        if (selectedFile === path) {
          useDiffStore.getState().clearSelection();
        }
        setConfirm(null);
      },
    });
  }

  function handleDiscardAll(area: "Unstaged" | "Staged") {
    const count = area === "Unstaged" ? unstaged.length : staged.length;
    setConfirm({
      message: `Discard all ${count} ${area.toLowerCase()} change${count !== 1 ? "s" : ""}? This cannot be undone.`,
      onConfirm: () => {
        discardAll(repoPath, area);
        useDiffStore.getState().clearSelection();
        setConfirm(null);
      },
    });
  }

  if (loading) {
    return (
      <div className="flex h-full items-center justify-center text-xs text-fg-muted">
        Loading…
      </div>
    );
  }

  return (
    <>
    <div className="flex h-full flex-col">
      {unstaged.length === 0 && staged.length === 0 ? (
        <div className="flex flex-1 flex-col items-center justify-center gap-1">
          <span className="text-xs text-fg-muted">No changes</span>
          {!emptyCommitMode && (
            <button
              onClick={enableEmptyCommit}
              className="text-xs text-accent hover:underline cursor-pointer"
            >
              Make empty commit.
            </button>
          )}
        </div>
      ) : (
        <>
          {/* Unstaged changes */}
          <div className="flex-1 overflow-auto border-b border-border">
            <div className="sticky top-0 bg-bg-surface px-3 py-2 text-xs font-medium text-fg-muted flex items-center">
              Unstaged Changes
              {unstaged.length > 0 && (
                <span className="ml-2 rounded bg-bg-hover px-1.5 py-0.5 text-[10px]">
                  {unstaged.length}
                </span>
              )}
              {unstaged.length > 0 && (
                <button
                  onClick={() => shiftHeld ? handleDiscardAll("Unstaged") : stageAll(repoPath)}
                  className={`ml-auto flex-shrink-0 rounded px-2 py-0.5 text-[10px] transition-colors cursor-pointer ${
                    shiftHeld ? "text-danger/70 hover:bg-danger/10 hover:text-danger" : "text-fg-muted hover:bg-bg-hover hover:text-fg"
                  }`}
                  title={shiftHeld ? "Discard all unstaged changes" : "Stage all changes"}
                >
                  {shiftHeld ? "Discard All ✕" : "Stage All ↓"}
                </button>
              )}
              {unstagedStats.size > 0 && (() => {
                let adds = 0, dels = 0;
                let maxAddLen = 0, maxDelLen = 0;
                for (const s of unstagedStats.values()) {
                  adds += s.additions; dels += s.deletions;
                  if (s.additions > 0) maxAddLen = Math.max(maxAddLen, String(s.additions).length);
                  if (s.deletions > 0) maxDelLen = Math.max(maxDelLen, String(s.deletions).length);
                }
                const totalAddLen = adds > 0 ? Math.max(String(adds).length, maxAddLen) : maxAddLen;
                const totalDelLen = dels > 0 ? Math.max(String(dels).length, maxDelLen) : maxDelLen;
                return (
                  <span className={`${unstaged.length === 0 ? "ml-auto" : "ml-2"} font-mono text-[11px] flex items-center`}>
                    {totalAddLen > 0 && (
                      <span className="text-success text-right" style={{ minWidth: `${totalAddLen + 1}ch` }}>
                        {adds > 0 ? `+${adds}` : ""}
                      </span>
                    )}
                    {totalAddLen > 0 && totalDelLen > 0 && <span className="w-[1ch]" />}
                    {totalDelLen > 0 && (
                      <span className="text-danger text-right" style={{ minWidth: `${totalDelLen + 1}ch` }}>
                        {dels > 0 ? `−${dels}` : ""}
                      </span>
                    )}
                  </span>
                );
              })()}
            </div>
            {unstaged.length === 0 ? (
              <div className="px-3 py-3 text-center text-xs text-fg-muted">
                No unstaged changes.
              </div>
            ) : (
              <FileList entries={unstaged} actionIcon="stage" onAction={(path) => handleStageFile(path)} onDiscard={(path) => handleDiscardFile(path, "Unstaged")} onSelect={(path) => selectFile(repoPath, path, "Unstaged")} selectedPath={selectedFile} stats={unstagedStats} />
            )}
          </div>

          {/* Staged changes */}
          <div className="flex-1 overflow-auto border-b border-border">
            <div className="sticky top-0 bg-bg-surface px-3 py-2 text-xs font-medium text-fg-muted flex items-center">
              Staged Changes
              {staged.length > 0 && (
                <span className="ml-2 rounded bg-bg-hover px-1.5 py-0.5 text-[10px]">
                  {staged.length}
                </span>
              )}
              {staged.length > 0 && (
                <button
                  onClick={() => shiftHeld ? handleDiscardAll("Staged") : unstageAll(repoPath)}
                  className={`ml-auto flex-shrink-0 rounded px-2 py-0.5 text-[10px] transition-colors cursor-pointer ${
                    shiftHeld ? "text-danger/70 hover:bg-danger/10 hover:text-danger" : "text-fg-muted hover:bg-bg-hover hover:text-fg"
                  }`}
                  title={shiftHeld ? "Discard all staged changes" : "Unstage all changes"}
                >
                  {shiftHeld ? "✕ Discard All" : "↑ Unstage All"}
                </button>
              )}
              {stagedStats.size > 0 && (() => {
                let adds = 0, dels = 0;
                let maxAddLen = 0, maxDelLen = 0;
                for (const s of stagedStats.values()) {
                  adds += s.additions; dels += s.deletions;
                  if (s.additions > 0) maxAddLen = Math.max(maxAddLen, String(s.additions).length);
                  if (s.deletions > 0) maxDelLen = Math.max(maxDelLen, String(s.deletions).length);
                }
                const totalAddLen = adds > 0 ? Math.max(String(adds).length, maxAddLen) : maxAddLen;
                const totalDelLen = dels > 0 ? Math.max(String(dels).length, maxDelLen) : maxDelLen;
                return (
                  <span className={`${staged.length === 0 ? "ml-auto" : "ml-2"} font-mono text-[11px] flex items-center`}>
                    {totalAddLen > 0 && (
                      <span className="text-success text-right" style={{ minWidth: `${totalAddLen + 1}ch` }}>
                        {adds > 0 ? `+${adds}` : ""}
                      </span>
                    )}
                    {totalAddLen > 0 && totalDelLen > 0 && <span className="w-[1ch]" />}
                    {totalDelLen > 0 && (
                      <span className="text-danger text-right" style={{ minWidth: `${totalDelLen + 1}ch` }}>
                        {dels > 0 ? `−${dels}` : ""}
                      </span>
                    )}
                  </span>
                );
              })()}
            </div>
            {staged.length === 0 ? (
              <div className="px-3 py-3 text-center">
                <p className="text-xs text-fg-muted">No staged changes.</p>
                {!emptyCommitMode && (
                  <button
                    onClick={enableEmptyCommit}
                    className="mt-1 text-xs text-accent hover:underline cursor-pointer"
                  >
                    Make empty commit.
                  </button>
                )}
              </div>
            ) : (
              <FileList entries={staged} actionIcon="unstage" onAction={(path) => handleUnstageFile(path)} onDiscard={(path) => handleDiscardFile(path, "Staged")} onSelect={(path) => selectFile(repoPath, path, "Staged")} selectedPath={selectedFile} stats={stagedStats} />
            )}
          </div>
        </>
      )}

      {/* Commit form resize handle */}
      <div
        onMouseDown={onResizeCommit}
        className="h-1 flex-shrink-0 cursor-row-resize border-t border-border hover:bg-accent/40 active:bg-accent/60"
      />

      {/* Commit form */}
      <div style={{ height: commitHeight }} className="flex flex-shrink-0 flex-col gap-2 p-3">
        <input
          type="text"
          value={summary}
          onChange={(e) => setSummary(e.target.value)}
          placeholder={defaultSummary || "Summary (required)"}
          className="w-full rounded border border-border bg-bg px-2.5 py-2 text-sm leading-normal text-fg placeholder:text-fg-muted focus:border-accent focus:outline-none"
        />
        <textarea
          value={description}
          onChange={(e) => setDescription(e.target.value)}
          placeholder="Description"
          className="w-full flex-1 resize-none rounded border border-border bg-bg px-2.5 py-1.5 text-xs text-fg placeholder:text-fg-muted focus:border-accent focus:outline-none"
        />
        <button
          onClick={handleCommit}
          disabled={!canCommit}
          title={
            browsingHistory
              ? "Cannot commit while viewing history — jump back to present or create a new branch"
              : !emptyCommitMode && staged.length === 0
                ? "No staged files to commit"
                : !effectiveSummary
                  ? "A commit summary is required"
                  : undefined
          }
          className="w-full rounded bg-accent py-2 text-sm font-medium text-accent-fg transition-colors hover:opacity-90 disabled:opacity-40 disabled:cursor-default cursor-pointer"
        >
          {committing
            ? "Committing…"
            : browsingHistory
              ? "Viewing history"
              : emptyCommitMode
                ? `Make empty commit to ${currentBranch ?? "…"}`
                : `Commit to ${currentBranch ?? "…"}`}
        </button>
      </div>
    </div>

    {/* Confirmation dialog */}
    {confirm && (
      <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/40">
        <div className="w-80 rounded-lg border border-border bg-bg-surface p-4 shadow-xl">
          <p className="mb-4 text-sm text-fg">{confirm.message}</p>
          <div className="flex justify-end gap-2">
            <button
              onClick={() => setConfirm(null)}
              className="rounded px-3 py-1.5 text-xs text-fg-muted transition-colors hover:bg-bg-hover cursor-pointer"
            >
              Cancel
            </button>
            <button
              onClick={confirm.onConfirm}
              className="rounded bg-danger px-3 py-1.5 text-xs text-white transition-colors hover:opacity-90 cursor-pointer"
            >
              Discard
            </button>
          </div>
        </div>
      </div>
    )}
    </>
  );
}
