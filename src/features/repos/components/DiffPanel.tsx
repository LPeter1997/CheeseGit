import { useCallback } from "react";
import { useDiffStore } from "../../diff/store";
import { FileViewer } from "../../diff/components/FileViewer";
import { useStagingStore } from "../../staging";
import { commands, type LineSelection } from "../../../ipc/bindings";

interface DiffPanelProps {
  repoPath: string;
}

export function DiffPanel({ repoPath }: DiffPanelProps) {
  const selectedFile = useDiffStore((s) => s.selectedFile);
  const selectedArea = useDiffStore((s) => s.selectedArea);
  const fileContent = useDiffStore((s) => s.fileContent);
  const fileDiff = useDiffStore((s) => s.fileDiff);
  const viewMode = useDiffStore((s) => s.viewMode);
  const setViewMode = useDiffStore((s) => s.setViewMode);
  const refreshFile = useDiffStore((s) => s.refreshFile);
  const loading = useDiffStore((s) => s.loading);
  const fetchStatus = useStagingStore((s) => s.fetchStatus);

  const handleStageLines = useCallback(
    async (selections: LineSelection[]) => {
      if (!selectedFile || !fileDiff) return;
      const result = await commands.stageLines(repoPath, selectedFile, fileDiff, selections);
      if (result.status === "ok") {
        fetchStatus(repoPath);
        // Refresh the diff without clearing existing view.
        refreshFile(repoPath, selectedFile, selectedArea ?? "Unstaged");
      }
    },
    [repoPath, selectedFile, fileDiff, selectedArea, fetchStatus, refreshFile],
  );

  const handleUnstageLines = useCallback(
    async (selections: LineSelection[]) => {
      if (!selectedFile || !fileDiff) return;
      const result = await commands.unstageLines(repoPath, selectedFile, fileDiff, selections);
      if (result.status === "ok") {
        fetchStatus(repoPath);
        refreshFile(repoPath, selectedFile, selectedArea ?? "Staged");
      }
    },
    [repoPath, selectedFile, fileDiff, selectedArea, fetchStatus, refreshFile],
  );

  if (!selectedFile) {
    return (
      <div className="flex h-full items-center justify-center text-sm text-fg-muted">
        Select a file to view its diff.
      </div>
    );
  }

  if (loading) {
    return (
      <div className="flex h-full items-center justify-center text-sm text-fg-muted">
        Loading…
      </div>
    );
  }

  if (fileContent === null) {
    return (
      <div className="flex h-full items-center justify-center text-sm text-fg-muted">
        Unable to read file contents.
      </div>
    );
  }

  // Determine the staging action based on the area being viewed.
  const onStageLines = selectedArea === "Unstaged" ? handleStageLines : undefined;
  const onUnstageLines = selectedArea === "Staged" ? handleUnstageLines : undefined;

  return (
    <div className="flex h-full flex-col overflow-hidden">
      {fileDiff && fileDiff.hunks.length > 0 && (
        <ViewModeToggle viewMode={viewMode} onChange={setViewMode} />
      )}
      <div className="flex-1 overflow-hidden">
        <FileViewer
          filePath={selectedFile}
          content={fileContent}
          diff={fileDiff}
          viewMode={viewMode}
          onStageLines={onStageLines}
          onUnstageLines={onUnstageLines}
        />
      </div>
    </div>
  );
}

function ViewModeToggle({
  viewMode,
  onChange,
}: {
  viewMode: "unified" | "split";
  onChange: (mode: "unified" | "split") => void;
}) {
  return (
    <div className="flex flex-shrink-0 items-center gap-1 border-b border-border bg-bg-surface px-3 py-1">
      <button
        onClick={() => onChange("unified")}
        className={`rounded px-2 py-0.5 text-xs transition-colors ${
          viewMode === "unified"
            ? "bg-accent text-accent-fg"
            : "text-fg-muted hover:bg-bg-hover hover:text-fg"
        }`}
      >
        Unified
      </button>
      <button
        onClick={() => onChange("split")}
        className={`rounded px-2 py-0.5 text-xs transition-colors ${
          viewMode === "split"
            ? "bg-accent text-accent-fg"
            : "text-fg-muted hover:bg-bg-hover hover:text-fg"
        }`}
      >
        Split
      </button>
    </div>
  );
}
