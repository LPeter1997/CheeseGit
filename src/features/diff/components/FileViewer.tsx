import { useEffect, useState, useMemo, useRef } from "react";
import type { FileDiff } from "../../../ipc/bindings";
import type { LineSelection } from "../../../ipc/bindings";
import type { DiffViewMode } from "../store";
import { useHighlightedLines } from "../hooks/useHighlightedLines";
import { useDiffSearch } from "../hooks/useDiffSearch";
import { SmartPath } from "../../../shared/components/SmartPath";
import { DiffSearchBar } from "./DiffSearchBar";
import { UnifiedDiffView } from "./UnifiedDiffView";
import { SplitDiffView } from "./SplitDiffView";
import { PlainFileView } from "./PlainFileView";

/** Known binary/non-text file extensions that cannot be meaningfully diffed. */
const BINARY_EXTENSIONS = new Set([
  "png", "jpg", "jpeg", "gif", "bmp", "ico", "webp", "avif", "tiff", "tif",
  "svg", "pdf",
  "woff", "woff2", "ttf", "otf", "eot",
  "zip", "gz", "tar", "bz2", "xz", "7z", "rar",
  "exe", "dll", "so", "dylib", "bin",
  "wasm",
  "mp3", "mp4", "ogg", "wav", "flac", "avi", "mkv", "mov", "webm",
  "class", "jar", "pyc", "pyo",
  "ds_store",
]);

function isBinaryFile(filePath: string): boolean {
  const ext = filePath.split(".").pop()?.toLowerCase() ?? "";
  return BINARY_EXTENSIONS.has(ext);
}

function buildSyntheticDiffFromContent(filePath: string, content: string): FileDiff {
  const plainLines = content.split("\n");
  return {
    path: filePath,
    hunks: [
      {
        header: "@@ synthetic @@",
        old_start: 1,
        new_start: 1,
        lines: plainLines.map((line, index) => ({
          kind: "Addition" as const,
          content: line,
          old_lineno: null,
          new_lineno: index + 1,
        })),
      },
    ],
  };
}

interface FileViewerProps {
  filePath: string;
  content: string;
  diff?: FileDiff | null;
  viewMode?: DiffViewMode;
  onViewModeChange?: (mode: DiffViewMode) => void;
  onStageLines?: (selections: LineSelection[]) => void;
  onUnstageLines?: (selections: LineSelection[]) => void;
  onDiscardLines?: (selections: LineSelection[]) => void;
}

export function FileViewer({
  filePath,
  content,
  diff,
  viewMode = "unified",
  onViewModeChange,
  onStageLines,
  onUnstageLines,
  onDiscardLines,
}: FileViewerProps) {
  const isBinary = isBinaryFile(filePath);
  const { lines, bg } = useHighlightedLines(filePath, isBinary ? "" : content);
  const plainLines = useMemo(() => content.split("\n"), [content]);

  const hasDiff = diff && diff.hunks.length > 0;
  const searchSourceDiff = useMemo(
    () => (hasDiff ? diff : buildSyntheticDiffFromContent(filePath, content)),
    [hasDiff, diff, filePath, content],
  );

  // Search state
  const [searchFocusSignal, setSearchFocusSignal] = useState(0);
  const search = useDiffSearch(searchSourceDiff);
  const { clearSearch } = search;
  const containerRef = useRef<HTMLDivElement>(null);

  // Ctrl+F to open search (listen on document for global availability)
  useEffect(() => {
    const handleKeyDown = (e: KeyboardEvent) => {
      if ((e.ctrlKey || e.metaKey) && e.key === "f") {
        e.preventDefault();
        setSearchFocusSignal((s) => s + 1);
      }
    };

    document.addEventListener("keydown", handleKeyDown);
    return () => {
      document.removeEventListener("keydown", handleKeyDown);
    };
  }, []);

  // Reset search when switching files
  useEffect(() => {
    clearSearch();
  }, [filePath, clearSearch]);

  if (isBinary) {
    return (
      <div className="flex h-full flex-col overflow-hidden">
        <div className="flex flex-shrink-0 items-center border-b border-border bg-bg-surface px-4 py-2 text-xs text-fg-muted">
          <SmartPath path={filePath} className="flex-1 text-xs" />
        </div>
        <div className="flex flex-1 items-center justify-center text-sm text-fg-muted">
          Diff view is not supported for this file type.
        </div>
      </div>
    );
  }

  return (
    <div className="flex h-full flex-col overflow-hidden" data-testid="diff-viewer" ref={containerRef}>
      <div className="flex flex-shrink-0 items-center border-b border-border bg-bg-surface px-4 py-2 text-xs text-fg-muted">
        <SmartPath path={filePath} className="flex-1 text-xs" />
        <div className="ml-3 flex items-center gap-2">
          <DiffSearchBar
            query={search.query}
            onQueryChange={search.setQuery}
            currentIndex={search.currentIndex}
            totalMatches={search.matches.length}
            onNext={search.goToNext}
            onPrevious={search.goToPrevious}
            isSearching={search.isSearching}
            focusSignal={searchFocusSignal}
          />
          {hasDiff && onViewModeChange && (
            <>
              <button
                onClick={() => onViewModeChange("unified")}
                data-testid="diff-mode-unified"
                className={`cursor-pointer rounded px-2 py-0.5 text-xs transition-colors ${
                  viewMode === "unified"
                    ? "bg-accent text-accent-fg"
                    : "text-fg-muted hover:bg-bg-hover hover:text-fg"
                }`}
              >
                Unified
              </button>
              <button
                onClick={() => onViewModeChange("split")}
                data-testid="diff-mode-split"
                className={`cursor-pointer rounded px-2 py-0.5 text-xs transition-colors ${
                  viewMode === "split"
                    ? "bg-accent text-accent-fg"
                    : "text-fg-muted hover:bg-bg-hover hover:text-fg"
                }`}
              >
                Split
              </button>
            </>
          )}
        </div>
      </div>
      {lines === null ? (
        <div className="flex-1 p-3 text-fg-muted">Highlighting…</div>
      ) : hasDiff ? (
        viewMode === "split" ? (
          <SplitDiffView
            key={filePath}
            diff={diff}
            filePath={filePath}
            tokenizedLines={lines}
            bg={bg}
            onStageLines={onStageLines}
            onUnstageLines={onUnstageLines}
            onDiscardLines={onDiscardLines}
            searchMatches={search.matches}
            currentMatch={search.currentMatch}
          />
        ) : (
          <UnifiedDiffView
            key={filePath}
            diff={diff}
            filePath={filePath}
            tokenizedLines={lines}
            bg={bg}
            onStageLines={onStageLines}
            onUnstageLines={onUnstageLines}
            onDiscardLines={onDiscardLines}
            searchMatches={search.matches}
            currentMatch={search.currentMatch}
          />
        )
      ) : (
        <PlainFileView
          key={filePath}
          filePath={filePath}
          lines={lines}
          plainLines={plainLines}
          bg={bg}
          searchMatches={search.matches}
          currentMatch={search.currentMatch}
        />
      )}
    </div>
  );
}
