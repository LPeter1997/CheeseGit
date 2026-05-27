import { useState, useRef } from "react";
import { type BranchTrackingStatus } from "../../../ipc/bindings";
import { RemoteButton } from "./RemoteButton";
import { OptionsMenu } from "./OptionsMenu";
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
}

export function BranchBar({ repoPath, currentBranch, browsingHistory, tracking, switching, panelWidth, onSwitch, onCreate, onMerge, onRemoteComplete, onRemoteChange }: BranchBarProps) {
  const [open, setOpen] = useState(false);
  const toggleRef = useRef<HTMLButtonElement>(null);

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
                // Refresh after branch deletion
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

      <div className="flex-1" />

      <OptionsMenu />
    </div>
  );
}
