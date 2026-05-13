import type { StatusEntry, FileStatus } from "../../../ipc/bindings";

interface FileListProps {
  entries: StatusEntry[];
  actionIcon: "stage" | "unstage";
  onAction: (path: string) => void;
  onSelect?: (path: string) => void;
  selectedPath?: string | null;
}

export function FileList({ entries, actionIcon, onAction, onSelect, selectedPath }: FileListProps) {
  return (
    <div className="flex flex-col">
      {entries.map((entry) => (
        <div
          key={entry.path}
          onClick={() => onSelect?.(entry.path)}
          className={`group flex items-center gap-2 px-3 py-1 text-sm text-fg cursor-pointer hover:bg-bg-hover ${
            selectedPath === entry.path ? "bg-bg-hover" : ""
          }`}
        >
          <StatusBadge status={entry.status} />
          <span className="flex-1 truncate">{entry.path}</span>
          <button
            onClick={(e) => {
              e.stopPropagation();
              onAction(entry.path);
            }}
            className="flex-shrink-0 opacity-0 transition-opacity group-hover:opacity-100"
            title={actionIcon === "stage" ? "Stage file" : "Unstage file"}
          >
            {actionIcon === "stage" ? <StageIcon /> : <UnstageIcon />}
          </button>
        </div>
      ))}
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
