import { useState, useEffect, useRef, useCallback } from "react";
import { useMergeStore, type ResolutionChoice } from "../store";
import { useMergeToolsStore } from "../merge-tools-store";
import type { ConflictResolution, FileConflictInfo } from "../../../ipc/bindings";
import { useRepoWatcher } from "../../../shared/hooks/useRepoWatcher";

function MergeToolIcon({ icon, size = 14 }: { icon: string | null | undefined; size?: number }) {
  if (!icon) return null;
  return (
    <img
      src={`/icons/merge-tools/${icon}`}
      alt=""
      width={size}
      height={size}
      className="inline-block shrink-0"
    />
  );
}

interface MergeConflictDialogProps {
  repoPath: string;
  onResolved?: () => void;
}

const RESOLUTION_OPTIONS: { value: ConflictResolution; label: string }[] = [
  { value: "AcceptCurrent", label: "Current" },
  { value: "AcceptIncoming", label: "Incoming" },
  { value: "AcceptBoth", label: "Both" },
];

export function MergeConflictDialog({ repoPath, onResolved }: MergeConflictDialogProps) {
  const merging = useMergeStore((s) => s.merging);
  const incomingBranch = useMergeStore((s) => s.incomingBranch);
  const conflictFiles = useMergeStore((s) => s.conflictFiles);
  const resolutions = useMergeStore((s) => s.resolutions);
  const resolvedExternally = useMergeStore((s) => s.resolvedExternally);
  const loading = useMergeStore((s) => s.loading);
  const isRevert = useMergeStore((s) => s.isRevert);
  const abortMerge = useMergeStore((s) => s.abortMerge);
  const applyAndFinalize = useMergeStore((s) => s.applyAndFinalize);
  const refreshConflicts = useMergeStore((s) => s.refreshConflicts);
  const setResolution = useMergeStore((s) => s.setResolution);
  const openInMergeTool = useMergeStore((s) => s.openInMergeTool);

  const availableTools = useMergeToolsStore((s) => s.availableTools);
  const selectedToolId = useMergeToolsStore((s) => s.selectedToolId);
  const selectTool = useMergeToolsStore((s) => s.selectTool);
  const initialized = useMergeToolsStore((s) => s.initialized);
  const initialize = useMergeToolsStore((s) => s.initialize);

  const [commitMessage, setCommitMessage] = useState("");
  const pollRef = useRef<ReturnType<typeof setInterval> | null>(null);

  // Initialize merge tools store if not done yet
  useEffect(() => {
    if (!initialized) {
      initialize();
    }
  }, [initialized, initialize]);

  const selectedTool = availableTools.find((t) => t.id === selectedToolId) ?? null;

  // Watch for file changes and refresh immediately (skip while loading/finalizing)
  const handleFilesChanged = useCallback(() => {
    if (!useMergeStore.getState().loading) {
      refreshConflicts(repoPath);
    }
  }, [repoPath, refreshConflicts]);
  useRepoWatcher(merging ? repoPath : null, handleFilesChanged);

  // Set default commit message when merge/revert starts
  useEffect(() => {
    if (merging && incomingBranch) {
      if (isRevert) {
        setCommitMessage(`Revert "${incomingBranch}"`);
      } else {
        setCommitMessage(`Merge branch '${incomingBranch}'`);
      }
    }
  }, [merging, incomingBranch, isRevert]);

  // Poll for conflict changes (files may be resolved externally)
  useEffect(() => {
    if (!merging) {
      if (pollRef.current) {
        clearInterval(pollRef.current);
        pollRef.current = null;
      }
      return;
    }

    pollRef.current = setInterval(() => {
      // Don't poll while an operation is in flight
      if (!useMergeStore.getState().loading) {
        refreshConflicts(repoPath);
      }
    }, 2000);

    return () => {
      if (pollRef.current) {
        clearInterval(pollRef.current);
        pollRef.current = null;
      }
    };
  }, [merging, repoPath, refreshConflicts]);

  const handleAbort = useCallback(async () => {
    await abortMerge(repoPath);
  }, [repoPath, abortMerge]);

  const handleFinalize = useCallback(async () => {
    const success = await applyAndFinalize(repoPath, commitMessage);
    if (success) {
      onResolved?.();
    }
  }, [repoPath, commitMessage, applyAndFinalize, onResolved]);

  if (!merging) {
    return null;
  }

  // All files must either be resolved externally or have a resolution chosen
  const allFilesHandled = conflictFiles.every(
    (f) => resolvedExternally.has(f.path) || resolutions[f.path] != null,
  ) && (conflictFiles.length > 0 || resolvedExternally.size > 0);

  // Combine active conflict files + externally resolved for display
  const allFiles = [
    ...conflictFiles,
    ...[...resolvedExternally]
      .filter((p) => !conflictFiles.some((f) => f.path === p))
      .map((p) => ({ path: p, conflict_count: 0 })),
  ];

  return (
    <div data-testid="merge-dialog-overlay" className="fixed inset-0 z-50 flex items-center justify-center bg-black/50">
      <div data-testid="merge-dialog" className="flex max-h-[80vh] w-[520px] flex-col rounded-lg border border-border bg-bg-surface shadow-xl">
        {/* Header */}
        <div className="flex items-center justify-between border-b border-border px-4 py-3">
          <div>
            <h2 className="text-sm font-semibold text-fg">
              {isRevert ? "Revert Conflicts" : "Merge Conflicts"}
            </h2>
            <p className="text-xs text-fg-muted">
              {isRevert ? "Reverting" : "Merging"} <span className="font-mono font-medium">{incomingBranch}</span>{isRevert ? "" : " into current branch"}
            </p>
          </div>
          <button
            onClick={handleAbort}
            disabled={loading}
            data-testid="merge-abort-button"
            className="cursor-pointer rounded px-3 py-1 text-xs font-medium text-danger hover:bg-bg-hover disabled:opacity-50"
          >
            {isRevert ? "Abort Revert" : "Abort Merge"}
          </button>
        </div>

        {/* File list */}
        <div className="flex-1 overflow-visible px-4 py-3">
          <div className="flex flex-col gap-1">
            {allFiles.map((file) => (
              <ConflictFileRow
                key={file.path}
                file={file}
                resolution={resolutions[file.path] ?? null}
                isResolvedExternally={resolvedExternally.has(file.path)}
                onSetResolution={(r) => setResolution(file.path, r)}
                onOpenInTool={() => {
                  if (selectedToolId) {
                    openInMergeTool(repoPath, file.path, selectedToolId);
                  }
                }}
                availableTools={availableTools}
                selectedTool={selectedTool}
                onSelectTool={selectTool}
              />
            ))}
          </div>
        </div>

        {/* Footer */}
        <div className="flex items-center gap-2 border-t border-border px-4 py-3">
          <input
            type="text"
            value={commitMessage}
            onChange={(e) => setCommitMessage(e.target.value)}
            placeholder="Commit message"
            data-testid="merge-commit-message"
            className="flex-1 rounded border border-border bg-bg-surface px-2 py-1.5 text-xs text-fg outline-none focus:border-accent"
          />
          <button
            onClick={handleFinalize}
            disabled={!allFilesHandled || loading}
            data-testid="merge-complete-button"
            className="cursor-pointer rounded bg-accent px-4 py-1.5 text-xs font-medium text-accent-fg hover:opacity-90 disabled:opacity-50"
          >
            {loading ? "Completing..." : isRevert ? "Complete Revert" : "Complete Merge"}
          </button>
        </div>
      </div>
    </div>
  );
}

interface ConflictFileRowProps {
  file: FileConflictInfo;
  resolution: ResolutionChoice;
  isResolvedExternally: boolean;
  onSetResolution: (r: ResolutionChoice) => void;
  onOpenInTool: () => void;
  availableTools: { id: string; display_name: string; icon: string | null }[];
  selectedTool: { id: string; display_name: string; icon: string | null } | null;
  onSelectTool: (toolId: string) => void;
}

function ConflictFileRow({
  file,
  resolution,
  isResolvedExternally,
  onSetResolution,
  onOpenInTool,
  availableTools,
  selectedTool,
  onSelectTool,
}: ConflictFileRowProps) {
  const [dropdownOpen, setDropdownOpen] = useState(false);
  const dropdownRef = useRef<HTMLDivElement>(null);
  const filename = file.path.split("/").pop() ?? file.path;
  const dir = file.path.includes("/") ? file.path.slice(0, file.path.lastIndexOf("/") + 1) : "";

  const hasTools = availableTools.length > 0;
  const hasMultipleTools = availableTools.length > 1;

  // Close dropdown when clicking outside
  useEffect(() => {
    if (!dropdownOpen) return;
    const handler = (e: MouseEvent) => {
      if (dropdownRef.current && !dropdownRef.current.contains(e.target as Node)) {
        setDropdownOpen(false);
      }
    };
    document.addEventListener("mousedown", handler);
    return () => document.removeEventListener("mousedown", handler);
  }, [dropdownOpen]);

  return (
    <div data-testid="conflict-file-row" className="flex items-center gap-2 rounded border border-border px-3 py-2">
      {/* File info */}
      <div className="flex min-w-0 flex-1 items-center gap-2">
        {isResolvedExternally ? (
          <span className="inline-flex h-4 w-4 items-center justify-center rounded bg-success/20 text-[10px] font-bold text-success">
            ✓
          </span>
        ) : (
          <span className="inline-flex h-4 w-4 items-center justify-center rounded bg-danger/20 text-[10px] font-bold text-danger">
            {file.conflict_count}
          </span>
        )}
        <span className="truncate text-xs" title={file.path}>
          {dir && <span className="text-fg-muted">{dir}</span>}
          <span className="font-medium text-fg">{filename}</span>
        </span>
      </div>

      {/* Resolution pills */}
      {isResolvedExternally ? (
        <span className="text-xs text-success">Resolved</span>
      ) : (
        <div className="flex items-center gap-1">
          {RESOLUTION_OPTIONS.map((opt) => (
            <button
              key={opt.value}
              onClick={() =>
                onSetResolution(resolution === opt.value ? null : opt.value)
              }
              data-testid={`resolution-${opt.value}`}
              className={`cursor-pointer rounded px-2 py-0.5 text-[11px] font-medium transition-colors ${
                resolution === opt.value
                  ? "bg-accent text-accent-fg"
                  : "text-fg-muted hover:bg-bg-hover hover:text-fg"
              }`}
            >
              {opt.label}
            </button>
          ))}

          {/* Open in tool button */}
          <div className="relative" ref={dropdownRef}>
            <div className="flex items-center">
              <button
                onClick={onOpenInTool}
                disabled={!hasTools}
                data-testid="open-in-tool-button"
                className="flex cursor-pointer items-center gap-1 rounded-l rounded-r px-2 py-0.5 text-[11px] font-medium text-accent hover:bg-bg-hover disabled:cursor-not-allowed disabled:opacity-50"
                title={
                  hasTools
                    ? `Open in ${selectedTool?.display_name ?? "merge tool"}`
                    : "No supported merge tool found on your system"
                }
              >
                {hasTools
                  ? <>
                      <MergeToolIcon icon={selectedTool?.icon} size={13} />
                      {`Open in ${selectedTool?.display_name ?? "Tool"}`}
                    </>
                  : "No Merge Tool"}
              </button>
              {hasMultipleTools && (
                <button
                  onClick={() => setDropdownOpen(!dropdownOpen)}
                  data-testid="merge-tool-dropdown-trigger"
                  className="cursor-pointer rounded-r px-1 py-0.5 text-[11px] text-accent hover:bg-bg-hover"
                  title="Choose merge tool"
                >
                  ▾
                </button>
              )}
            </div>

            {/* Dropdown menu */}
            {dropdownOpen && (
              <div
                data-testid="merge-tool-dropdown"
                className="absolute bottom-full right-0 z-50 mb-1 min-w-[180px] rounded border border-border bg-bg-surface py-1 shadow-lg"
              >
                {availableTools.map((tool) => (
                  <button
                    key={tool.id}
                    onClick={() => {
                      onSelectTool(tool.id);
                      setDropdownOpen(false);
                    }}
                    data-testid={`merge-tool-option-${tool.id}`}
                    className={`flex w-full cursor-pointer items-center gap-2 px-3 py-1.5 text-left text-[11px] hover:bg-bg-hover ${
                      tool.id === selectedTool?.id ? "font-semibold text-accent" : "text-fg"
                    }`}
                  >
                    <MergeToolIcon icon={tool.icon} size={13} />
                    <span>{tool.display_name}</span>
                    {tool.id === selectedTool?.id && (
                      <span className="ml-auto text-accent">✓</span>
                    )}
                  </button>
                ))}
              </div>
            )}
          </div>
        </div>
      )}
    </div>
  );
}
