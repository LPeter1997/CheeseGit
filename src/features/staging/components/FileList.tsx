import type { MouseEvent as ReactMouseEvent } from "react";
import type { StatusEntry, FileStats } from "../../../ipc/bindings";
import { SmartPath } from "../../../shared/components/SmartPath";
import { DiffStats, computeStatWidths } from "../../../shared/components/DiffStats";
import { FileStatusBadge } from "../../../shared/components/FileStatusBadge";
import { useShiftKey } from "../../../shared/hooks/useShiftKey";

interface FileListProps {
  entries: StatusEntry[];
  actionIcon: "stage" | "unstage";
  onAction: (path: string) => void;
  onDiscard?: (path: string) => void;
  onSelect?: (path: string, event: ReactMouseEvent<HTMLDivElement>) => void;
  selectedPath?: string | null;
  selectedPaths?: ReadonlySet<string>;
  stats?: Map<string, FileStats>;
}

export function FileList({ entries, actionIcon, onAction, onDiscard, onSelect, selectedPath, selectedPaths, stats }: FileListProps) {
  const shiftHeld = useShiftKey();
  const discardMode = shiftHeld && !!onDiscard;
  const hasStats = stats && stats.size > 0;
  const { addWidth, delWidth } = hasStats ? computeStatWidths(stats) : { addWidth: undefined, delWidth: undefined };

  return (
    <div className="flex flex-col">
      {entries.map((entry) => {
        const fileStat = stats?.get(entry.path);
        const hasVisibleStats = !!fileStat && (fileStat.additions > 0 || fileStat.deletions > 0);
        const selected = (selectedPaths?.has(entry.path) || selectedPath === entry.path) ?? false;
        return (
          <div
            key={entry.path}
            onClick={(event) => onSelect?.(entry.path, event)}
            data-testid="file-row"
            data-filepath={entry.path}
            data-selected={selected ? "true" : "false"}
            className={`group flex items-center gap-2 px-3 py-1.5 text-sm text-fg cursor-pointer select-none hover:bg-bg-hover ${
              selected ? "bg-accent/10" : ""
            }`}
          >
            <FileStatusBadge status={entry.status} />
            <SmartPath path={entry.path} className="flex-1 text-sm" />
            <button
              onClick={(e) => {
                e.stopPropagation();
                if (discardMode) onDiscard!(entry.path);
                else onAction(entry.path);
              }}
              data-testid="file-action"
              className={`flex-shrink-0 cursor-pointer transition-opacity group-hover:opacity-100 ${selected ? "opacity-40" : "opacity-0"}`}
              title={discardMode ? "Discard changes" : (actionIcon === "stage" ? "Stage file" : "Unstage file")}
            >
              {discardMode ? <DiscardIcon /> : (actionIcon === "stage" ? <StageIcon /> : <UnstageIcon />)}
            </button>
            {hasStats && hasVisibleStats && fileStat && (
              <DiffStats additions={fileStat.additions} deletions={fileStat.deletions} className="text-[11px]" addWidth={addWidth} delWidth={delWidth} />
            )}
            {hasStats && !hasVisibleStats && (addWidth != null || delWidth != null) && (
              <span className="invisible flex-shrink-0 tabular-nums whitespace-nowrap text-[11px]" aria-hidden="true">
                {addWidth != null && <span className="inline-block text-right" style={{ minWidth: `${addWidth}ch` }}>+0</span>}
                {addWidth != null && delWidth != null && " "}
                {delWidth != null && <span className="inline-block text-right" style={{ minWidth: `${delWidth}ch` }}>-0</span>}
              </span>
            )}
          </div>
        );
      })}
    </div>
  );
}

function StageIcon() {
  return (
    <svg className="h-4 w-4 text-fg-muted hover:text-fg" viewBox="0 0 16 16" fill="none" stroke="currentColor" strokeWidth="1.5" strokeLinecap="round" strokeLinejoin="round">
      <path d="M8 3v10M4 9l4 4 4-4" />
    </svg>
  );
}

function UnstageIcon() {
  return (
    <svg className="h-4 w-4 text-fg-muted hover:text-fg" viewBox="0 0 16 16" fill="none" stroke="currentColor" strokeWidth="1.5" strokeLinecap="round" strokeLinejoin="round">
      <path d="M8 13V3M4 7l4-4 4 4" />
    </svg>
  );
}

function DiscardIcon() {
  return (
    <svg className="h-4 w-4 text-fg-muted hover:text-danger" viewBox="0 0 16 16" fill="none" stroke="currentColor" strokeWidth="1.5" strokeLinecap="round" strokeLinejoin="round">
      <path d="M4 4l8 8M12 4l-8 8" />
    </svg>
  );
}

