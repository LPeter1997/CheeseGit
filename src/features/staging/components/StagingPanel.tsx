import { useEffect, useRef, useState } from "react";
import { useStagingStore } from "../store";
import { useHistoryStore } from "../../history";
import { useStashStore } from "../../stash/store";
import { useDiffStore } from "../../diff/store";
import { useDiffPrefetch } from "../../diff/hooks/useDiffPrefetch";
import { useResize } from "../../../shared/hooks/useResize";
import { useShiftKey } from "../../../shared/hooks/useShiftKey";
import { useClickOutside } from "../../../shared/hooks/useClickOutside";
import { useScrollClamp } from "../../../shared/hooks/useScrollClamp";
import { DiffStats } from "../../../shared/components/DiffStats";
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
  const stashStaged = useStagingStore((s) => s.stashStaged);
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
  const canStash = !browsingHistory && staged.length > 0 && effectiveSummary.length > 0 && !committing;
  const [showStashDropdown, setShowStashDropdown] = useState(false);
  const dropdownRef = useRef<HTMLDivElement>(null);
  useClickOutside([dropdownRef], () => setShowStashDropdown(false));

  const unstagedRef = useRef<HTMLDivElement>(null);
  const stagedRef = useRef<HTMLDivElement>(null);
  useScrollClamp(unstagedRef, unstaged.length);
  useScrollClamp(stagedRef, staged.length);

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

  async function handleStash() {
    setShowStashDropdown(false);
    const ok = await stashStaged(repoPath);
    if (ok) {
      useStashStore.getState().fetchStashes(repoPath);
      useDiffStore.getState().clearSelection();
      onCommit?.();
    }
  }

  function handleStageFile(path: string) {
    stageFile(repoPath, path);
    if (selectedFile === path) {
      const idx = unstaged.findIndex((e) => e.path === path);
      // Prefer the previous item; fall back to the next item (which shifts into idx)
      const neighbor = unstaged[idx - 1] ?? unstaged[idx + 1];
      if (neighbor) {
        selectFile(repoPath, neighbor.path, "Unstaged");
      } else {
        useDiffStore.getState().clearSelection();
      }
    }
  }

  function handleUnstageFile(path: string) {
    unstageFile(repoPath, path);
    if (selectedFile === path) {
      const idx = staged.findIndex((e) => e.path === path);
      // Prefer the previous item; fall back to the next item (which shifts into idx)
      const neighbor = staged[idx - 1] ?? staged[idx + 1];
      if (neighbor) {
        selectFile(repoPath, neighbor.path, "Staged");
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
          <div ref={unstagedRef} className="flex-1 min-h-0 overflow-auto border-b border-border" data-testid="unstaged-section">
            <div className="sticky top-0 z-10 bg-bg-surface px-3 py-2 text-xs font-medium text-fg-muted flex items-center">
              Unstaged Changes
              {unstaged.length > 0 && (
                <span className="ml-2 rounded bg-bg-hover px-1.5 py-0.5 text-[10px]">
                  {unstaged.length}
                </span>
              )}
              {unstaged.length > 0 && (
                <button
                  onClick={() => shiftHeld ? handleDiscardAll("Unstaged") : stageAll(repoPath)}
                  data-testid="stage-all"
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
                for (const s of unstagedStats.values()) {
                  adds += s.additions; dels += s.deletions;
                }
                return (
                  <DiffStats additions={adds} deletions={dels} className={`${unstaged.length === 0 ? "ml-auto" : "ml-2"} text-[11px]`} />
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
          <div ref={stagedRef} className="flex-1 min-h-0 overflow-auto border-b border-border" data-testid="staged-section">
            <div className="sticky top-0 z-10 bg-bg-surface px-3 py-2 text-xs font-medium text-fg-muted flex items-center">
              Staged Changes
              {staged.length > 0 && (
                <span className="ml-2 rounded bg-bg-hover px-1.5 py-0.5 text-[10px]">
                  {staged.length}
                </span>
              )}
              {staged.length > 0 && (
                <button
                  onClick={() => shiftHeld ? handleDiscardAll("Staged") : unstageAll(repoPath)}
                  data-testid="unstage-all"
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
                for (const s of stagedStats.values()) {
                  adds += s.additions; dels += s.deletions;
                }
                return (
                  <DiffStats additions={adds} deletions={dels} className={`${staged.length === 0 ? "ml-auto" : "ml-2"} text-[11px]`} />
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
          data-testid="commit-summary"
          className="w-full rounded border border-border bg-bg px-2.5 py-2 text-sm leading-normal text-fg placeholder:text-fg-muted focus:border-accent focus:outline-none"
        />
        <textarea
          value={description}
          onChange={(e) => setDescription(e.target.value)}
          placeholder="Description"
          data-testid="commit-description"
          className="w-full flex-1 resize-none rounded border border-border bg-bg px-2.5 py-1.5 text-xs text-fg placeholder:text-fg-muted focus:border-accent focus:outline-none"
        />
        <div className="relative flex">
          <button
            onClick={handleCommit}
            disabled={!canCommit}
            data-testid="commit-button"
            title={
              browsingHistory
                ? "Cannot commit while viewing history — jump back to present or create a new branch"
                : !emptyCommitMode && staged.length === 0
                  ? "No staged files to commit"
                  : !effectiveSummary
                    ? "A commit summary is required"
                    : undefined
            }
            className={`flex-1 rounded-l py-2 text-sm font-medium text-accent-fg transition-colors hover:opacity-90 disabled:opacity-40 disabled:cursor-default cursor-pointer ${
              staged.length > 0 && !browsingHistory ? "rounded-r-none" : "rounded-r"
            } bg-accent`}
          >
            {committing
              ? "Committing…"
              : browsingHistory
                ? "Viewing history"
                : emptyCommitMode
                  ? `Make empty commit to ${currentBranch ?? "…"}`
                  : `Commit to ${currentBranch ?? "…"}`}
          </button>
          {staged.length > 0 && !browsingHistory && (
            <button
              onClick={() => setShowStashDropdown((v) => !v)}
              disabled={committing}
              className="rounded-r border-l border-accent-fg/20 bg-accent px-2 py-2 text-accent-fg transition-colors hover:opacity-90 disabled:opacity-40 disabled:cursor-default cursor-pointer"
              title="More actions"
            >
              <svg width="10" height="6" viewBox="0 0 10 6" fill="currentColor">
                <path d="M1 1l4 4 4-4" stroke="currentColor" strokeWidth="1.5" fill="none" strokeLinecap="round" strokeLinejoin="round" />
              </svg>
            </button>
          )}
          {showStashDropdown && (
            <div ref={dropdownRef} className="absolute bottom-full left-0 right-0 mb-1 rounded border border-border bg-bg-surface shadow-xl z-10">
              <button
                onClick={handleStash}
                disabled={!canStash}
                className="w-full px-3 py-2 text-left text-xs text-fg transition-colors hover:bg-bg-hover disabled:opacity-40 disabled:cursor-default cursor-pointer"
              >
                Stash staged changes
              </button>
            </div>
          )}
        </div>
      </div>
    </div>

    {/* Confirmation dialog */}
    {confirm && (
      <div data-testid="discard-dialog-overlay" className="fixed inset-0 z-50 flex items-center justify-center bg-black/40">
        <div data-testid="discard-dialog" className="w-80 rounded-lg border border-border bg-bg-surface p-4 shadow-xl">
          <p className="mb-4 text-sm text-fg">{confirm.message}</p>
          <div className="flex justify-end gap-2">
            <button
              data-testid="discard-cancel-button"
              onClick={() => setConfirm(null)}
              className="rounded px-3 py-1.5 text-xs text-fg-muted transition-colors hover:bg-bg-hover cursor-pointer"
            >
              Cancel
            </button>
            <button
              data-testid="discard-confirm-button"
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
