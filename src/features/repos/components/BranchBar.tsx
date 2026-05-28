import { useState, useRef } from "react";
import { type BranchTrackingStatus } from "../../../ipc/bindings";
import { SmartPath } from "../../../shared/components/SmartPath";
import type { DiffViewMode } from "../../diff/store";
import { DiffSearchBar } from "../../diff/components/DiffSearchBar";
import { canSearchDiff } from "../../diff/utils/diffCapabilities";
import { RemoteButton } from "./RemoteButton";
import { BranchDropdown } from "./BranchDropdown";
import { BranchIcon, ChevronIcon } from "./BranchBarIcons";

interface BranchBarProps {
  repoPath: string;
  currentBranch: string | null;
  browsingHistory: boolean;
  tracking: BranchTrackingStatus | null;
  switching: boolean;
  panelWidth: number;
  onSwitch: (branchName: string) => void;
  onCreate: (branchName: string) => void;
  onMerge: (branchName: string) => void;
  onRemoteComplete: () => void;
  onRemoteChange?: (remote: string | null) => void;
  selectionContextLabel?: string | null;
  selectedFilePath?: string | null;
  searchQuery?: string;
  onSearchQueryChange?: (query: string) => void;
  searchCurrentIndex?: number;
  searchTotalMatches?: number;
  searchIsSearching?: boolean;
  onSearchNext?: () => void;
  onSearchPrevious?: () => void;
  searchFocusSignal?: number;
  viewMode?: DiffViewMode;
  onViewModeChange?: (mode: DiffViewMode) => void;
}

export function BranchBar({ repoPath, currentBranch, browsingHistory, tracking, switching, panelWidth, onSwitch, onCreate, onMerge, onRemoteComplete, onRemoteChange, selectionContextLabel = null, selectedFilePath = null, searchQuery = "", onSearchQueryChange, searchCurrentIndex = 0, searchTotalMatches = 0, searchIsSearching = false, onSearchNext, onSearchPrevious, searchFocusSignal = 0, viewMode, onViewModeChange }: BranchBarProps) {
  const [open, setOpen] = useState(false);
  const toggleRef = useRef<HTMLButtonElement>(null);
  const showDiffControls =
    !!selectedFilePath &&
    canSearchDiff(selectedFilePath) &&
    !!onSearchQueryChange &&
    !!onSearchNext &&
    !!onSearchPrevious;

  const diffControls = showDiffControls && (
    <div className="ml-3 flex shrink-0 items-center gap-2">
      <DiffSearchBar
        query={searchQuery}
        onQueryChange={onSearchQueryChange!}
        currentIndex={searchCurrentIndex}
        totalMatches={searchTotalMatches}
        onNext={onSearchNext!}
        onPrevious={onSearchPrevious!}
        isSearching={searchIsSearching}
        focusSignal={searchFocusSignal}
      />
      {onViewModeChange && (
        <div className="ml-1 flex items-center gap-1">
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
        </div>
      )}
    </div>
  );

  const fileSection = selectedFilePath && (
    <>
      <SmartPath
        path={selectedFilePath}
        className="min-w-0 flex-1 truncate text-xs text-fg-muted"
        data-testid="branch-selected-file"
      />
      {diffControls}
    </>
  );

  return (
    <div className="flex h-10 items-center border-b border-border bg-bg-surface" data-testid="branch-bar">
      <div style={{ width: panelWidth }} className="flex flex-shrink-0 items-center gap-1 border-r border-border px-2">
        <div className="relative flex-1 min-w-0">
          <button
            ref={toggleRef}
            onClick={() => setOpen(!open)}
            title={currentBranch ?? undefined}
            data-testid="branch-selector"
            className="flex w-full items-center gap-2 rounded px-2 py-1.5 text-sm transition-colors hover:bg-bg-hover cursor-pointer"
          >
            <BranchIcon />
            <span className="min-w-0 flex-1 truncate font-medium text-fg">
              {currentBranch ?? "…"}
            </span>
            {browsingHistory && (
              <span className="text-[10px] text-fg-muted">(history)</span>
            )}
            <ChevronIcon open={open} />
          </button>

          {open && (
            <BranchDropdown
              repoPath={repoPath}
              currentBranch={currentBranch}
              toggleRef={toggleRef}
              onSelect={(name) => {
                setOpen(false);
                if (name !== currentBranch) {
                  onSwitch(name);
                }
              }}
              onCreate={(name) => {
                setOpen(false);
                onCreate(name);
              }}
              onMerge={(name) => {
                setOpen(false);
                onMerge(name);
              }}
              onDelete={() => {
                onRemoteComplete();
              }}
              onClose={() => setOpen(false)}
            />
          )}
        </div>

        {switching && (
          <span className="text-xs text-fg-muted animate-pulse">…</span>
        )}

        <div className="flex-1 min-w-0">
          <RemoteButton repoPath={repoPath} tracking={tracking} disabled={browsingHistory} onComplete={onRemoteComplete} onRemoteChange={onRemoteChange} />
        </div>
      </div>

      <div className="flex min-w-0 flex-1 items-center px-3">
        {selectionContextLabel ? (
          <>
            <span className="mr-2 max-w-56 shrink-0 truncate text-xs text-fg-muted" title={selectionContextLabel}>
              {selectionContextLabel}
            </span>
            {fileSection}
          </>
        ) : fileSection ?? (
          <div className="h-4 w-full" data-testid="branch-selected-file-empty" />
        )}
      </div>
    </div>
  );
}
