import { useRef, useEffect } from "react";
import { useStashStore } from "../store";
import { FileViewer } from "../../diff/components/FileViewer";
import type { DiffViewMode } from "../../diff/store";
import type { StatusEntry, FileStats } from "../../../ipc/bindings";
import { SmartPath } from "../../../shared/components/SmartPath";
import { DiffStats, computeStatWidths } from "../../../shared/components/DiffStats";
import { FileStatusBadge } from "../../../shared/components/FileStatusBadge";
import { useResize } from "../../../shared/hooks/useResize";

interface StashDiffPanelProps {
  repoPath: string;
  viewMode: DiffViewMode;
  onViewModeChange: (mode: DiffViewMode) => void;
}

export function StashDiffPanel({ repoPath, viewMode, onViewModeChange }: StashDiffPanelProps) {
  const selectedIndex = useStashStore((s) => s.selectedIndex);
  const stashFiles = useStashStore((s) => s.stashFiles);
  const stashFilesLoading = useStashStore((s) => s.stashFilesLoading);
  const stashFileStats = useStashStore((s) => s.stashFileStats);
  const selectedFilePath = useStashStore((s) => s.selectedFilePath);
  const selectedFileDiff = useStashStore((s) => s.selectedFileDiff);
  const selectedFileContent = useStashStore((s) => s.selectedFileContent);
  const selectedFileDiffLoading = useStashStore((s) => s.selectedFileDiffLoading);
  const selectStashFile = useStashStore((s) => s.selectStashFile);
  const fileListRef = useRef<HTMLDivElement>(null);
  const { size: fileListWidth, onMouseDown: onResizeFileList } = useResize({
    direction: "horizontal",
    initialSize: 256,
    minSize: 150,
    maxSize: 600,
  });

  useEffect(() => {
    if (fileListRef.current) fileListRef.current.scrollTop = 0;
  }, [selectedIndex]);

  const { addWidth, delWidth } = computeStatWidths(stashFileStats);

  if (selectedIndex === null) {
    return (
      <div className="flex h-full items-center justify-center text-sm text-fg-muted">
        Select a stash to view its changes.
      </div>
    );
  }

  return (
    <div className="flex h-full flex-col overflow-hidden">
      {/* Main content: file list + diff */}
      <div className="flex flex-1 overflow-hidden">
        {/* File list */}
        <div ref={fileListRef} style={{ width: fileListWidth }} className="flex-shrink-0 border-r border-border overflow-auto">
          {stashFilesLoading ? (
            <div className="flex h-full items-center justify-center text-xs text-fg-muted">
              Loading…
            </div>
          ) : stashFiles.length === 0 ? (
            <div className="flex h-full items-center justify-center text-xs text-fg-muted">
              No file changes.
            </div>
          ) : (
            <div className="flex flex-col">
              {stashFiles.map((entry) => (
                  <StashFileEntry
                    key={entry.path}
                    entry={entry}
                    selected={entry.path === selectedFilePath}
                    onClick={() => selectStashFile(entry.path, repoPath)}
                    stats={stashFileStats.get(entry.path)}
                    addWidth={addWidth}
                    delWidth={delWidth}
                  />
                ))}
            </div>
          )}
        </div>
        <div
          onMouseDown={onResizeFileList}
          className="w-1 flex-shrink-0 cursor-col-resize border-r border-border hover:bg-accent/40 active:bg-accent/60"
        />

        {/* Diff viewer */}
        <div className="flex-1 overflow-hidden">
          {!selectedFilePath ? (
            <div className="flex h-full items-center justify-center text-sm text-fg-muted">
              Select a file to view its diff.
            </div>
          ) : selectedFileDiffLoading ? (
            <div className="flex h-full items-center justify-center text-sm text-fg-muted">
              Loading…
            </div>
          ) : selectedFileContent === null ? (
            selectedFileDiff && selectedFileDiff.hunks.length > 0 ? (
              <FileViewer
                filePath={selectedFilePath}
                content={selectedFileDiff.hunks
                  .flatMap((h) => h.lines)
                  .filter((l) => l.kind === "Deletion" || l.kind === "Context")
                  .map((l) => l.content)
                  .join("\n")}
                diff={selectedFileDiff}
                viewMode={viewMode}
                onViewModeChange={onViewModeChange}
                toolbarContext="stash"
              />
            ) : (
              <div className="flex h-full items-center justify-center text-sm text-fg-muted">
                Unable to load file content.
              </div>
            )
          ) : (
            <FileViewer
              filePath={selectedFilePath}
              content={selectedFileContent}
              diff={selectedFileDiff}
              viewMode={viewMode}
              onViewModeChange={onViewModeChange}
              toolbarContext="stash"
            />
          )}
        </div>
      </div>
    </div>
  );
}

function StashFileEntry({
  entry,
  selected,
  onClick,
  stats,
  addWidth,
  delWidth,
}: {
  entry: StatusEntry;
  selected: boolean;
  onClick: () => void;
  stats?: FileStats;
  addWidth?: number;
  delWidth?: number;
}) {
  return (
    <button
      onClick={onClick}
      className={`flex items-baseline gap-2 border-b border-border px-3 py-1.5 text-left text-xs transition-colors ${
        selected
          ? "bg-accent/10 text-fg"
          : "text-fg hover:bg-bg-hover"
      }`}
    >
      <FileStatusBadge status={entry.status} variant="inline" />
      <SmartPath path={entry.path} className="flex-1 text-xs" />
      {stats && <DiffStats additions={stats.additions} deletions={stats.deletions} className="text-[10px]" addWidth={addWidth} delWidth={delWidth} />}
    </button>
  );
}

