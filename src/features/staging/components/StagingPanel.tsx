import { useEffect, useRef, useState } from "react";
import type { MouseEvent as ReactMouseEvent } from "react";
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
  const stageFiles = useStagingStore((s) => s.stageFiles);
  const unstageFiles = useStagingStore((s) => s.unstageFiles);
  const stageAll = useStagingStore((s) => s.stageAll);
  const unstageAll = useStagingStore((s) => s.unstageAll);
  const discardFile = useStagingStore((s) => s.discardFile);
  const discardFiles = useStagingStore((s) => s.discardFiles);
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
  const [unstagedSelection, setUnstagedSelection] = useState<Set<string>>(new Set());
  const [stagedSelection, setStagedSelection] = useState<Set<string>>(new Set());
  const [unstagedAnchor, setUnstagedAnchor] = useState<string | null>(null);
  const [stagedAnchor, setStagedAnchor] = useState<string | null>(null);
  const shiftHeld = useShiftKey();

  const unstagedMultiCount = unstagedSelection.size > 1 ? unstagedSelection.size : 0;
  const stagedMultiCount = stagedSelection.size > 1 ? stagedSelection.size : 0;

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

  useEffect(() => {
    const unstagedPaths = new Set(unstaged.map((entry) => entry.path));
    const stagedPaths = new Set(staged.map((entry) => entry.path));

    setUnstagedSelection((prev) => new Set(Array.from(prev).filter((path) => unstagedPaths.has(path))));
    setStagedSelection((prev) => new Set(Array.from(prev).filter((path) => stagedPaths.has(path))));

    if (unstagedAnchor && !unstagedPaths.has(unstagedAnchor)) setUnstagedAnchor(null);
    if (stagedAnchor && !stagedPaths.has(stagedAnchor)) setStagedAnchor(null);
  }, [unstaged, staged, unstagedAnchor, stagedAnchor]);

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

  useEffect(() => {
    const onKeyDown = (e: KeyboardEvent) => {
      if (!e.ctrlKey || e.key !== "Enter") return;
      if (!canCommit) return;

      e.preventDefault();
      void handleCommit();
    };

    document.addEventListener("keydown", onKeyDown);
    return () => document.removeEventListener("keydown", onKeyDown);
  }, [canCommit, repoPath, summary, description, defaultSummary, emptyCommitMode, staged.length, browsingHistory, committing]);

  function handleStageFile(path: string) {
    stageFile(repoPath, path);
    setUnstagedSelection((prev) => {
      if (!prev.has(path)) return prev;
      const next = new Set(prev);
      next.delete(path);
      return next;
    });
    if (selectedFile === path) {
      const idx = unstaged.findIndex((e) => e.path === path);
      // Prefer the previous item; fall back to the next item (which shifts into idx)
      const neighbor = unstaged[idx - 1] ?? unstaged[idx + 1];
      if (neighbor) {
        setStagedSelection(new Set());
        selectFile(repoPath, neighbor.path, "Unstaged");
      } else {
        setStagedSelection(new Set());
        useDiffStore.getState().clearSelection();
      }
    }
  }

  function handleUnstageFile(path: string) {
    unstageFile(repoPath, path);
    setStagedSelection((prev) => {
      if (!prev.has(path)) return prev;
      const next = new Set(prev);
      next.delete(path);
      return next;
    });
    if (selectedFile === path) {
      const idx = staged.findIndex((e) => e.path === path);
      // Prefer the previous item; fall back to the next item (which shifts into idx)
      const neighbor = staged[idx - 1] ?? staged[idx + 1];
      if (neighbor) {
        setUnstagedSelection(new Set());
        selectFile(repoPath, neighbor.path, "Staged");
      } else {
        setUnstagedSelection(new Set());
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
        if (area === "Unstaged") {
          setUnstagedSelection((prev) => {
            if (!prev.has(path)) return prev;
            const next = new Set(prev);
            next.delete(path);
            return next;
          });
        } else {
          setStagedSelection((prev) => {
            if (!prev.has(path)) return prev;
            const next = new Set(prev);
            next.delete(path);
            return next;
          });
        }
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
        if (area === "Unstaged") {
          setUnstagedSelection(new Set());
        } else {
          setStagedSelection(new Set());
        }
        useDiffStore.getState().clearSelection();
        setConfirm(null);
      },
    });
  }

  function buildRange(paths: string[], from: string, to: string): string[] {
    const startIndex = paths.indexOf(from);
    const endIndex = paths.indexOf(to);
    if (startIndex === -1 || endIndex === -1) return [to];
    const [lo, hi] = startIndex <= endIndex ? [startIndex, endIndex] : [endIndex, startIndex];
    return paths.slice(lo, hi + 1);
  }

  function handleRowSelect(
    area: "Unstaged" | "Staged",
    path: string,
    event: ReactMouseEvent<HTMLDivElement>,
  ) {
    const entries = area === "Unstaged" ? unstaged : staged;
    const paths = entries.map((entry) => entry.path);
    const ctrlLike = event.ctrlKey || event.metaKey;
    const shiftLike = event.shiftKey;

    if (area === "Unstaged") {
      if (shiftLike) {
        setUnstagedSelection((prev) => {
          const anchor = unstagedAnchor ?? path;
          const range = buildRange(paths, anchor, path);
          if (ctrlLike) {
            const next = new Set(prev);
            for (const p of range) next.add(p);
            return next;
          }
          return new Set(range);
        });
      } else if (ctrlLike) {
        setUnstagedSelection((prev) => {
          const next = new Set(prev);
          if (next.has(path)) next.delete(path);
          else next.add(path);
          return next;
        });
        setUnstagedAnchor(path);
      } else {
        setUnstagedSelection(new Set([path]));
        setUnstagedAnchor(path);
        selectFile(repoPath, path, "Unstaged");
      }
      setStagedSelection(new Set());
      setStagedAnchor(null);
      return;
    }

    if (shiftLike) {
      setStagedSelection((prev) => {
        const anchor = stagedAnchor ?? path;
        const range = buildRange(paths, anchor, path);
        if (ctrlLike) {
          const next = new Set(prev);
          for (const p of range) next.add(p);
          return next;
        }
        return new Set(range);
      });
    } else if (ctrlLike) {
      setStagedSelection((prev) => {
        const next = new Set(prev);
        if (next.has(path)) next.delete(path);
        else next.add(path);
        return next;
      });
      setStagedAnchor(path);
    } else {
      setStagedSelection(new Set([path]));
      setStagedAnchor(path);
      selectFile(repoPath, path, "Staged");
    }
    setUnstagedSelection(new Set());
    setUnstagedAnchor(null);
  }

  function handleStageBulkSelected() {
    const paths = Array.from(unstagedSelection);
    if (paths.length <= 1) {
      stageAll(repoPath);
      return;
    }
    stageFiles(repoPath, paths);
    if (selectedFile && unstagedSelection.has(selectedFile)) {
      useDiffStore.getState().clearSelection();
    }
    setUnstagedSelection(new Set());
    setUnstagedAnchor(null);
  }

  function handleUnstageBulkSelected() {
    const paths = Array.from(stagedSelection);
    if (paths.length <= 1) {
      unstageAll(repoPath);
      return;
    }
    unstageFiles(repoPath, paths);
    if (selectedFile && stagedSelection.has(selectedFile)) {
      useDiffStore.getState().clearSelection();
    }
    setStagedSelection(new Set());
    setStagedAnchor(null);
  }

  function handleDiscardBulkSelected(area: "Unstaged" | "Staged") {
    const selection = area === "Unstaged" ? unstagedSelection : stagedSelection;
    if (selection.size <= 1) {
      handleDiscardAll(area);
      return;
    }

    const count = selection.size;
    setConfirm({
      message: `Discard ${count} selected ${area.toLowerCase()} change${count !== 1 ? "s" : ""}? This cannot be undone.`,
      onConfirm: () => {
        const paths = Array.from(selection);
        discardFiles(repoPath, paths, area);
        if (selectedFile && selection.has(selectedFile)) {
          useDiffStore.getState().clearSelection();
        }
        if (area === "Unstaged") {
          setUnstagedSelection(new Set());
          setUnstagedAnchor(null);
        } else {
          setStagedSelection(new Set());
          setStagedAnchor(null);
        }
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
        <div className="flex flex-1 flex-col items-center justify-center gap-2">
          <svg className="h-8 w-8 text-success/60" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.5" strokeLinecap="round" strokeLinejoin="round"><circle cx="12" cy="12" r="10" /><path d="M8 12l3 3 5-5" /></svg>
          <span className="text-xs text-fg-muted">Working tree clean</span>
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
                  onClick={() => shiftHeld ? handleDiscardBulkSelected("Unstaged") : handleStageBulkSelected()}
                  data-testid="stage-all"
                  className={`ml-auto flex-shrink-0 rounded px-2 py-0.5 text-[10px] transition-colors cursor-pointer ${
                    shiftHeld ? "text-danger/70 hover:bg-danger/10 hover:text-danger" : "text-fg-muted hover:bg-bg-hover hover:text-fg"
                  }`}
                  title={shiftHeld
                    ? (unstagedMultiCount > 1 ? `Discard ${unstagedMultiCount} selected unstaged changes` : "Discard all unstaged changes")
                    : (unstagedMultiCount > 1 ? `Stage ${unstagedMultiCount} selected files` : "Stage all changes")}
                >
                  {shiftHeld
                    ? (unstagedMultiCount > 1 ? `Discard ${unstagedMultiCount} Selected ✕` : "Discard All ✕")
                    : (unstagedMultiCount > 1 ? `Stage ${unstagedMultiCount} Selected ↓` : "Stage All ↓")}
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
              <FileList entries={unstaged} actionIcon="stage" onAction={(path) => handleStageFile(path)} onDiscard={(path) => handleDiscardFile(path, "Unstaged")} onSelect={(path, event) => handleRowSelect("Unstaged", path, event)} selectedPath={selectedFile} selectedPaths={unstagedSelection} stats={unstagedStats} />
            )}
          </div>

          {/* Staged changes */}
          <div ref={stagedRef} className="flex-1 min-h-0 overflow-auto border-b border-border" data-testid="staged-section">
            <div className="sticky top-0 z-10 bg-bg-surface px-3 py-2 text-xs font-medium text-fg-muted flex items-center">
              Staged Changes
              {staged.length > 0 && (
                <span className="ml-2 rounded bg-accent/20 text-accent px-1.5 py-0.5 text-[10px]">
                  {staged.length}
                </span>
              )}
              {staged.length > 0 && (
                <button
                  onClick={() => shiftHeld ? handleDiscardBulkSelected("Staged") : handleUnstageBulkSelected()}
                  data-testid="unstage-all"
                  className={`ml-auto flex-shrink-0 rounded px-2 py-0.5 text-[10px] transition-colors cursor-pointer ${
                    shiftHeld ? "text-danger/70 hover:bg-danger/10 hover:text-danger" : "text-fg-muted hover:bg-bg-hover hover:text-fg"
                  }`}
                  title={shiftHeld
                    ? (stagedMultiCount > 1 ? `Discard ${stagedMultiCount} selected staged changes` : "Discard all staged changes")
                    : (stagedMultiCount > 1 ? `Unstage ${stagedMultiCount} selected files` : "Unstage all changes")}
                >
                  {shiftHeld
                    ? (stagedMultiCount > 1 ? `✕ Discard ${stagedMultiCount} Selected` : "✕ Discard All")
                    : (stagedMultiCount > 1 ? `↑ Unstage ${stagedMultiCount} Selected` : "↑ Unstage All")}
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
              <FileList entries={staged} actionIcon="unstage" onAction={(path) => handleUnstageFile(path)} onDiscard={(path) => handleDiscardFile(path, "Staged")} onSelect={(path, event) => handleRowSelect("Staged", path, event)} selectedPath={selectedFile} selectedPaths={stagedSelection} stats={stagedStats} />
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
          className="w-full rounded border border-border bg-bg px-2.5 py-2 text-sm leading-normal text-fg placeholder:text-fg-muted focus:border-accent focus:outline-none focus:ring-2 focus:ring-accent/20"
        />
        <textarea
          value={description}
          onChange={(e) => setDescription(e.target.value)}
          placeholder="Description"
          data-testid="commit-description"
          className="w-full flex-1 resize-none rounded border border-border bg-bg px-2.5 py-1.5 text-xs text-fg placeholder:text-fg-muted focus:border-accent focus:outline-none focus:ring-2 focus:ring-accent/20"
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
              data-testid="commit-more-actions"
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
                data-testid="stash-staged-action"
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
