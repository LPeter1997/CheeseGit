import { useEffect, useMemo, useRef } from "react";
import type { FileDiff } from "../../../ipc/bindings";
import type { LineSelection } from "../../../ipc/bindings";
import type { DiffViewMode } from "../store";
import { useDiffToolbarStore, type DiffToolbarContext } from "../toolbar-store";
import { useHighlightedLines } from "../hooks/useHighlightedLines";
import { useDiffSearch } from "../hooks/useDiffSearch";
import { UnifiedDiffView } from "./UnifiedDiffView";
import { SplitDiffView } from "./SplitDiffView";
import { PlainFileView } from "./PlainFileView";
import { reconstructOldContent } from "./DiffViewShared";
import { canDisplayDiff } from "../utils/diffCapabilities";

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
          highlights: [],
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
  toolbarContext?: DiffToolbarContext;
  onStageLines?: (selections: LineSelection[]) => void;
  onUnstageLines?: (selections: LineSelection[]) => void;
  onDiscardLines?: (selections: LineSelection[]) => void;
}

export function FileViewer({
  filePath,
  content,
  diff,
  viewMode = "unified",
  toolbarContext = "staging",
  onStageLines,
  onUnstageLines,
  onDiscardLines,
}: FileViewerProps) {
  const isBinary = !canDisplayDiff(filePath);
  const effectiveContent = isBinary ? "" : content;

  // Compute which line indices the diff actually needs so the backend can
  // skip expensive tokenization for lines that will never be displayed.
  const neededNewLines = useMemo(() => {
    if (!diff || !diff.hunks.length) return null; // tokenize all
    const set = new Set<number>();
    for (const hunk of diff.hunks) {
      for (const line of hunk.lines) {
        if (line.new_lineno !== null) set.add(line.new_lineno - 1);
      }
    }
    return [...set].sort((a, b) => a - b);
  }, [diff]);

  const neededOldLines = useMemo(() => {
    if (!diff || !diff.hunks.length) return null;
    const set = new Set<number>();
    for (const hunk of diff.hunks) {
      for (const line of hunk.lines) {
        if (line.old_lineno !== null) set.add(line.old_lineno - 1);
      }
    }
    return [...set].sort((a, b) => a - b);
  }, [diff]);

  const { lines: newLines } = useHighlightedLines(filePath, effectiveContent, neededNewLines);
  const oldContent = useMemo(
    () => (diff && diff.hunks.length > 0 ? reconstructOldContent(effectiveContent, diff) : ""),
    [effectiveContent, diff],
  );
  const { lines: oldLines } = useHighlightedLines(filePath, oldContent, neededOldLines);
  const lines = useMemo(() => {
    if (!newLines) return null;
    if (oldLines) return [...newLines, ...oldLines];
    return newLines;
  }, [newLines, oldLines]);
  const newLineCount = useMemo(() => effectiveContent.split("\n").length, [effectiveContent]);
  const plainLines = useMemo(() => content.split("\n"), [content]);

  const hasDiff = diff && diff.hunks.length > 0;
  const searchSourceDiff = useMemo(
    () => (hasDiff ? diff : buildSyntheticDiffFromContent(filePath, content)),
    [hasDiff, diff, filePath, content],
  );

  // Search state
  const search = useDiffSearch(searchSourceDiff);
  const { clearSearch } = search;
  const containerRef = useRef<HTMLDivElement>(null);
  const toolbarQuery = useDiffToolbarStore((s) => s.query[toolbarContext]);
  const toolbarFocusSignal = useDiffToolbarStore((s) => s.focusSignal[toolbarContext]);
  const toolbarNextSignal = useDiffToolbarStore((s) => s.nextSignal[toolbarContext]);
  const toolbarPreviousSignal = useDiffToolbarStore((s) => s.previousSignal[toolbarContext]);
  const setToolbarQuery = useDiffToolbarStore((s) => s.setQuery);
  const setMatchStatus = useDiffToolbarStore((s) => s.setMatchStatus);
  const requestFocus = useDiffToolbarStore((s) => s.requestFocus);
  const lastNextSignalRef = useRef(toolbarNextSignal);
  const lastPreviousSignalRef = useRef(toolbarPreviousSignal);
  const lastFocusSignalRef = useRef(toolbarFocusSignal);

  // Ctrl+F to open search (listen on document for global availability)
  useEffect(() => {
    const handleKeyDown = (e: KeyboardEvent) => {
      if ((e.ctrlKey || e.metaKey) && e.key === "f") {
        e.preventDefault();
        requestFocus(toolbarContext);
      }
    };

    document.addEventListener("keydown", handleKeyDown);
    return () => {
      document.removeEventListener("keydown", handleKeyDown);
    };
  }, [requestFocus, toolbarContext]);

  // Reset search when switching files
  useEffect(() => {
    clearSearch();
    setToolbarQuery(toolbarContext, "");
  }, [filePath, clearSearch, setToolbarQuery, toolbarContext]);

  // BranchBar query is the source of truth for the search string.
  useEffect(() => {
    if (search.query !== toolbarQuery) {
      search.setQuery(toolbarQuery);
    }
  }, [search.query, search.setQuery, toolbarQuery]);

  useEffect(() => {
    setMatchStatus(toolbarContext, {
      currentIndex: search.currentIndex,
      totalMatches: search.matches.length,
      isSearching: search.isSearching,
    });
  }, [search.currentIndex, search.isSearching, search.matches.length, setMatchStatus, toolbarContext]);

  useEffect(() => {
    if (toolbarNextSignal !== lastNextSignalRef.current) {
      lastNextSignalRef.current = toolbarNextSignal;
      search.goToNext();
    }
  }, [search.goToNext, toolbarNextSignal]);

  useEffect(() => {
    if (toolbarPreviousSignal !== lastPreviousSignalRef.current) {
      lastPreviousSignalRef.current = toolbarPreviousSignal;
      search.goToPrevious();
    }
  }, [search.goToPrevious, toolbarPreviousSignal]);

  useEffect(() => {
    if (toolbarFocusSignal !== lastFocusSignalRef.current) {
      lastFocusSignalRef.current = toolbarFocusSignal;
      const input = document.querySelector<HTMLInputElement>("[data-testid='diff-search-input']");
      if (input) {
        input.focus();
        input.select();
      }
    }
  }, [toolbarFocusSignal]);

  if (isBinary) {
    return (
      <div className="flex h-full flex-col overflow-hidden">
        <div className="flex flex-1 items-center justify-center text-sm text-fg-muted">
          Diff view is not supported for this file type.
        </div>
      </div>
    );
  }

  return (
    <div className="flex h-full flex-col overflow-hidden" data-testid="diff-viewer" ref={containerRef}>
      {lines === null ? (
        <div className="flex-1 p-3 text-fg-muted">Highlighting…</div>
      ) : hasDiff ? (
        viewMode === "split" ? (
          <SplitDiffView
            key={filePath}
            diff={diff}
            filePath={filePath}
            tokenizedLines={lines}
            newLineCount={newLineCount}
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
            newLineCount={newLineCount}
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
          searchMatches={search.matches}
          currentMatch={search.currentMatch}
        />
      )}
    </div>
  );
}
