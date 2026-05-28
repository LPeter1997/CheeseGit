import { useCallback } from "react";
import { useDiffStore } from "../../diff/store";
import { FileViewer } from "../../diff/components/FileViewer";
import { useStagingStore } from "../../staging";
import { commands, type DiffArea, type LineSelection } from "../../../ipc/bindings";
import { useRepoWatcher } from "../../../shared/hooks/useRepoWatcher";

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
  const selectFile = useDiffStore((s) => s.selectFile);
  const loading = useDiffStore((s) => s.loading);
  const fetchStatus = useStagingStore((s) => s.fetchStatus);

  // Refresh the diff view when the file changes on disk
  const handleRepoFilesChanged = useCallback(() => {
    const { selectedFile: sf, selectedArea: sa } = useDiffStore.getState();
    if (sf && sa) {
      refreshFile(repoPath, sf, sa);
    }
  }, [repoPath, refreshFile]);
  useRepoWatcher(repoPath, handleRepoFilesChanged);

  /** Navigate to the next file in the same area, or clear selection. */
  const navigateToNextFile = useCallback(
    (currentFile: string, area: "Unstaged" | "Staged") => {
      const list = area === "Unstaged"
        ? useStagingStore.getState().unstaged
        : useStagingStore.getState().staged;
      const remaining = list.filter((e) => e.path !== currentFile);
      if (remaining.length > 0) {
        selectFile(repoPath, remaining[0].path, area);
      } else {
        useDiffStore.getState().clearSelection();
      }
    },
    [repoPath, selectFile],
  );

  const handleStageLines = useCallback(
    async (selections: LineSelection[]) => {
      if (!selectedFile || !fileDiff) return;
      const result = await commands.stageLines(repoPath, selectedFile, fileDiff, selections);
      if (result.status === "ok") {
        await fetchStatus(repoPath);
        // Check if the file has been fully staged (no longer in unstaged list)
        const stillUnstaged = useStagingStore.getState().unstaged.some((e) => e.path === selectedFile);
        if (!stillUnstaged) {
          navigateToNextFile(selectedFile, "Unstaged");
        } else {
          refreshFile(repoPath, selectedFile, selectedArea ?? "Unstaged");
        }
      }
    },
    [repoPath, selectedFile, fileDiff, selectedArea, fetchStatus, refreshFile, navigateToNextFile],
  );

  const handleUnstageLines = useCallback(
    async (selections: LineSelection[]) => {
      if (!selectedFile || !fileDiff) return;
      const result = await commands.unstageLines(repoPath, selectedFile, fileDiff, selections);
      if (result.status === "ok") {
        await fetchStatus(repoPath);
        // Check if the file has been fully unstaged (no longer in staged list)
        const stillStaged = useStagingStore.getState().staged.some((e) => e.path === selectedFile);
        if (!stillStaged) {
          navigateToNextFile(selectedFile, "Staged");
        } else {
          refreshFile(repoPath, selectedFile, selectedArea ?? "Staged");
        }
      }
    },
    [repoPath, selectedFile, fileDiff, selectedArea, fetchStatus, refreshFile, navigateToNextFile],
  );

  const handleDiscardLines = useCallback(
    async (selections: LineSelection[]) => {
      if (!selectedFile || !fileDiff || !selectedArea) return;
      const area: DiffArea = selectedArea;
      const result = await commands.discardLines(repoPath, selectedFile, fileDiff, selections, area);
      if (result.status === "ok") {
        await fetchStatus(repoPath);
        // Check if the file still exists in the current area
        const list = area === "Unstaged"
          ? useStagingStore.getState().unstaged
          : useStagingStore.getState().staged;
        const stillExists = list.some((e) => e.path === selectedFile);
        if (!stillExists) {
          navigateToNextFile(selectedFile, area);
        } else {
          refreshFile(repoPath, selectedFile, area);
        }
      }
    },
    [repoPath, selectedFile, fileDiff, selectedArea, fetchStatus, refreshFile, navigateToNextFile],
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

  // Determine the staging action based on the area being viewed.
  const onStageLines = selectedArea === "Unstaged" ? handleStageLines : undefined;
  const onUnstageLines = selectedArea === "Staged" ? handleUnstageLines : undefined;

  if (fileContent === null) {
    // For deleted files, reconstruct content from the diff's deletion lines.
    if (fileDiff && fileDiff.hunks.length > 0) {
      const reconstructed = fileDiff.hunks
        .flatMap((h) => h.lines)
        .filter((l) => l.kind === "Deletion" || l.kind === "Context")
        .map((l) => l.content)
        .join("\n");

      return (
        <div className="flex h-full flex-col overflow-hidden">
          <div className="flex-1 overflow-hidden">
            <FileViewer
              filePath={selectedFile}
              content={reconstructed}
              diff={fileDiff}
              viewMode={viewMode}
              onViewModeChange={setViewMode}
              toolbarContext="staging"
              onStageLines={onStageLines}
              onUnstageLines={onUnstageLines}
              onDiscardLines={handleDiscardLines}
            />
          </div>
        </div>
      );
    }
    return (
      <div className="flex h-full items-center justify-center text-sm text-fg-muted">
        Unable to read file contents.
      </div>
    );
  }

  return (
    <div className="flex h-full flex-col overflow-hidden">
      <div className="flex-1 overflow-hidden">
        <FileViewer
          filePath={selectedFile}
          content={fileContent}
          diff={fileDiff}
          viewMode={viewMode}
          onViewModeChange={fileDiff && fileDiff.hunks.length > 0 ? setViewMode : undefined}
          toolbarContext="staging"
          onStageLines={onStageLines}
          onUnstageLines={onUnstageLines}
          onDiscardLines={handleDiscardLines}
        />
      </div>
    </div>
  );
}

