import type { FileStatus } from "../../ipc/bindings";

interface FileStatusBadgeProps {
  status: FileStatus;
  /**
   * "badge"  — small square with coloured letter, used in staging file lists.
   * "inline" — bare coloured letter in monospace, used in history/stash file lists.
   */
  variant?: "badge" | "inline";
}

export function FileStatusBadge({ status, variant = "badge" }: FileStatusBadgeProps) {
  const { letter, color, label } = statusInfo(status);

  if (variant === "inline") {
    return (
      <span
        title={label}
        className={`flex-shrink-0 font-mono text-xs font-bold leading-none ${color}`}
      >
        {letter}
      </span>
    );
  }

  return (
    <span
      title={label}
      className={`flex h-4 w-4 flex-shrink-0 items-center justify-center rounded text-[10px] font-bold ${color}`}
    >
      {letter}
    </span>
  );
}

function statusInfo(status: FileStatus): { letter: string; color: string; label: string } {
  switch (status) {
    case "Added":
      return { letter: "A", color: "text-success", label: "Added" };
    case "Modified":
      return { letter: "M", color: "text-accent", label: "Modified" };
    case "Deleted":
      return { letter: "D", color: "text-danger", label: "Deleted" };
    case "Renamed":
      return { letter: "R", color: "text-fg-muted", label: "Renamed" };
    case "Copied":
      return { letter: "C", color: "text-fg-muted", label: "Copied" };
    case "Untracked":
      return { letter: "U", color: "text-success", label: "Untracked" };
    default:
      return { letter: "?", color: "text-fg-muted", label: "Unknown" };
  }
}
