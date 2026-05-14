import { useState } from "react";
import { useHistoryStore } from "../store";
import { FileViewer } from "../../diff/components/FileViewer";
import type { DiffViewMode } from "../../diff/store";
import type { StatusEntry } from "../../../ipc/bindings";

interface CommitDiffPanelProps {
  repoPath: string;
}

export function CommitDiffPanel({ repoPath }: CommitDiffPanelProps) {
  const commits = useHistoryStore((s) => s.commits);
  const selectedIndex = useHistoryStore((s) => s.selectedIndex);
  const commitFiles = useHistoryStore((s) => s.commitFiles);
  const commitFilesLoading = useHistoryStore((s) => s.commitFilesLoading);
  const selectedFilePath = useHistoryStore((s) => s.selectedFilePath);
  const selectedFileDiff = useHistoryStore((s) => s.selectedFileDiff);
  const selectedFileContent = useHistoryStore((s) => s.selectedFileContent);
  const selectedFileDiffLoading = useHistoryStore((s) => s.selectedFileDiffLoading);
  const selectCommitFile = useHistoryStore((s) => s.selectCommitFile);
  const [viewMode, setViewMode] = useState<DiffViewMode>("unified");

  if (selectedIndex < 0) {
    return (
      <div className="flex h-full items-center justify-center text-sm text-fg-muted">
        Select a commit to view its changes.
      </div>
    );
  }

  const commit = commits[selectedIndex];

  return (
    <div className="flex h-full flex-col overflow-hidden">
      {/* Header */}
      <div className="flex flex-shrink-0 items-center gap-2 border-b border-border bg-bg-surface px-3 py-1.5">
        <span className="truncate text-xs text-fg-muted">
          <span className="font-mono">{commit?.short_hash}</span>
          {" — "}
          {commit?.summary}
        </span>
        {selectedFilePath && (
          <div className="ml-auto flex items-center gap-1">
            <ViewModeToggle viewMode={viewMode} onChange={setViewMode} />
          </div>
        )}
      </div>

      {/* Main content: file list + diff */}
      <div className="flex flex-1 overflow-hidden">
        {/* File list */}
        <div className="w-64 flex-shrink-0 border-r border-border overflow-auto">
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
                />
              ))}
            </div>
          )}
        </div>

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
            <div className="flex h-full items-center justify-center text-sm text-fg-muted">
              Unable to load file content.
            </div>
          ) : (
            <FileViewer
              filePath={selectedFilePath}
              content={selectedFileContent}
              diff={selectedFileDiff}
              viewMode={viewMode}
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
}: {
  entry: StatusEntry;
  selected: boolean;
  onClick: () => void;
}) {
  const statusLabel = statusBadge(entry.status);

  return (
    <button
      onClick={onClick}
      className={`flex items-center gap-2 border-b border-border px-3 py-1.5 text-left text-xs transition-colors ${
        selected
          ? "bg-accent/10 text-fg"
          : "text-fg hover:bg-bg-hover"
      }`}
    >
      <span className={`flex-shrink-0 font-mono text-[10px] font-bold ${statusLabel.color}`}>
        {statusLabel.letter}
      </span>
      <span className="truncate">{entry.path}</span>
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

function ViewModeToggle({
  viewMode,
  onChange,
}: {
  viewMode: DiffViewMode;
  onChange: (mode: DiffViewMode) => void;
}) {
  return (
    <div className="flex items-center gap-1">
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
