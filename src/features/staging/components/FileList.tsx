import type { StatusEntry, FileStatus, FileStats } from "../../../ipc/bindings";
import { SmartPath } from "../../../shared/components/SmartPath";
import { useShiftKey } from "../../../shared/hooks/useShiftKey";

interface FileListProps {
  entries: StatusEntry[];
  actionIcon: "stage" | "unstage";
  onAction: (path: string) => void;
  onDiscard?: (path: string) => void;
  onSelect?: (path: string) => void;
  selectedPath?: string | null;
  stats?: Map<string, FileStats>;
}

export function FileList({ entries, actionIcon, onAction, onDiscard, onSelect, selectedPath, stats }: FileListProps) {
  const shiftHeld = useShiftKey();
  const discardMode = shiftHeld && !!onDiscard;
  // Compute max digit widths for column alignment.
  let maxAddLen = 0;
  let maxDelLen = 0;
  if (stats) {
    for (const s of stats.values()) {
      if (s.additions > 0) maxAddLen = Math.max(maxAddLen, String(s.additions).length);
      if (s.deletions > 0) maxDelLen = Math.max(maxDelLen, String(s.deletions).length);
    }
  }

  return (
    <div className="flex flex-col">
      {entries.map((entry) => {
        const fileStat = stats?.get(entry.path);
        return (
          <div
            key={entry.path}
            onClick={() => onSelect?.(entry.path)}
            className={`group flex items-center gap-2 px-3 py-1.5 text-sm text-fg cursor-pointer hover:bg-bg-hover ${
              selectedPath === entry.path ? "bg-bg-hover" : ""
            }`}
          >
            <StatusBadge status={entry.status} />
            <SmartPath path={entry.path} className="flex-1 text-sm" />
            <button
              onClick={(e) => {
                e.stopPropagation();
                if (discardMode) onDiscard!(entry.path);
                else onAction(entry.path);
              }}
              className="flex-shrink-0 cursor-pointer opacity-0 transition-opacity group-hover:opacity-100"
              title={discardMode ? "Discard changes" : (actionIcon === "stage" ? "Stage file" : "Unstage file")}
            >
              {discardMode ? <DiscardIcon /> : (actionIcon === "stage" ? <StageIcon /> : <UnstageIcon />)}
            </button>
            {(maxAddLen > 0 || maxDelLen > 0) && (
              <span className="flex-shrink-0 text-[11px] font-mono flex items-center">
                {maxAddLen > 0 && (
                  <span className="text-success text-right" style={{ minWidth: `${maxAddLen + 1}ch` }}>
                    {fileStat && fileStat.additions > 0 ? `+${fileStat.additions}` : ""}
                  </span>
                )}
                {maxAddLen > 0 && maxDelLen > 0 && <span className="w-[1ch]" />}
                {maxDelLen > 0 && (
                  <span className="text-danger text-right" style={{ minWidth: `${maxDelLen + 1}ch` }}>
                    {fileStat && fileStat.deletions > 0 ? `−${fileStat.deletions}` : ""}
                  </span>
                )}
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

function StatusBadge({ status }: { status: FileStatus }) {
  const { letter, color } = statusDisplay(status);
  return (
    <span
      className={`flex h-4 w-4 flex-shrink-0 items-center justify-center rounded text-[10px] font-bold ${color}`}
    >
      {letter}
    </span>
  );
}

function statusDisplay(status: FileStatus): { letter: string; color: string } {
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
    case "Untracked":
      return { letter: "U", color: "text-success" };
    default:
      return { letter: "?", color: "text-fg-muted" };
  }
}
