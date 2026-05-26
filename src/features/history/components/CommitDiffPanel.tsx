import { useState, useRef, useEffect } from "react";
import { useHistoryStore } from "../store";
import { FileViewer } from "../../diff/components/FileViewer";
import type { DiffViewMode } from "../../diff/store";
import type { StatusEntry, FileStats } from "../../../ipc/bindings";
import { SmartPath } from "../../../shared/components/SmartPath";
import { DiffStats, computeStatWidths } from "../../../shared/components/DiffStats";
import { useResize } from "../../../shared/hooks/useResize";

interface CommitDiffPanelProps {
  repoPath: string;
}

export function CommitDiffPanel({ repoPath }: CommitDiffPanelProps) {
  const commits = useHistoryStore((s) => s.commits);
  const graphData = useHistoryStore((s) => s.graphData);
  const selectedHash = useHistoryStore((s) => s.selectedHash);
  const commitFiles = useHistoryStore((s) => s.commitFiles);
  const commitFilesLoading = useHistoryStore((s) => s.commitFilesLoading);
  const commitFileStats = useHistoryStore((s) => s.commitFileStats);
  const selectedFilePath = useHistoryStore((s) => s.selectedFilePath);
  const selectedFileDiff = useHistoryStore((s) => s.selectedFileDiff);
  const selectedFileContent = useHistoryStore((s) => s.selectedFileContent);
  const selectedFileDiffLoading = useHistoryStore((s) => s.selectedFileDiffLoading);
  const selectCommitFile = useHistoryStore((s) => s.selectCommitFile);
  const [viewMode, setViewMode] = useState<DiffViewMode>("unified");
  const fileListRef = useRef<HTMLDivElement>(null);
  const { size: fileListWidth, onMouseDown: onResizeFileList } = useResize({
    direction: "horizontal",
    initialSize: 256,
    minSize: 150,
    maxSize: 600,
  });

  useEffect(() => {
    if (fileListRef.current) fileListRef.current.scrollTop = 0;
  }, [selectedHash]);

  const { addWidth, delWidth } = computeStatWidths(commitFileStats);

  if (!selectedHash) {
    return (
      <div className="flex h-full items-center justify-center text-sm text-fg-muted">
        Select a commit to view its changes.
      </div>
    );
  }

  const source = graphData?.commits ?? commits;
  const commit = source.find((c) => c.hash === selectedHash);

  return (
    <div className="flex h-full flex-col overflow-hidden">
      {/* Header */}
      <div className="flex flex-shrink-0 items-center gap-2 border-b border-border bg-bg-surface px-4 py-2">
        <span className="truncate text-xs text-fg-muted">
          <span className="font-mono">{commit?.short_hash}</span>
          {" — "}
          {commit?.summary}
        </span>
      </div>

      {/* Main content: file list + diff */}
      <div className="flex flex-1 overflow-hidden">
        {/* File list */}
        <div ref={fileListRef} style={{ width: fileListWidth }} className="flex-shrink-0 border-r border-border overflow-auto">
          {commitFilesLoading ? (
            <div className="flex h-full items-center justify-center text-xs text-fg-muted">
              Loading…
            </div>
          ) : commitFiles.length === 0 ? (
            <div className="flex h-full items-center justify-center text-xs text-fg-muted">
              No file changes.
            </div>
          ) : (
            <div className="flex flex-col">
              {commitFiles.map((entry) => (
                  <CommitFileEntry
                    key={entry.path}
                    entry={entry}
                    selected={entry.path === selectedFilePath}
                    onClick={() => selectCommitFile(entry.path, repoPath)}
                    stats={commitFileStats.get(entry.path)}
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
                onViewModeChange={setViewMode}
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
              onViewModeChange={setViewMode}
            />
          )}
        </div>
      </div>
    </div>
  );
}

function CommitFileEntry({
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
  const statusLabel = statusBadge(entry.status);

  return (
    <button
      onClick={onClick}
      data-testid="commit-file-entry"
      className={`flex items-baseline gap-2 border-b border-border px-3 py-1.5 text-left text-xs transition-colors ${
        selected
          ? "bg-accent/10 text-fg"
          : "text-fg hover:bg-bg-hover"
      }`}
    >
      <span className={`flex-shrink-0 font-mono text-xs font-bold leading-none ${statusLabel.color}`}>
        {statusLabel.letter}
      </span>
      <SmartPath path={entry.path} className="flex-1 text-xs" />
      {stats && <DiffStats additions={stats.additions} deletions={stats.deletions} className="text-[10px]" addWidth={addWidth} delWidth={delWidth} />}
    </button>
  );
}

function statusBadge(status: StatusEntry["status"]): { letter: string; color: string } {
  switch (status) {
    case "Added":
      return { letter: "A", color: "text-success" };
    case "Modified":
      return { letter: "M", color: "text-accent" };
    case "Deleted":
      return { letter: "D", color: "text-danger" };
    case "Renamed":
      return { letter: "R", color: "text-fg-muted" };
    case "Copied":
      return { letter: "C", color: "text-fg-muted" };
    default:
      return { letter: "?", color: "text-fg-muted" };
  }
}
