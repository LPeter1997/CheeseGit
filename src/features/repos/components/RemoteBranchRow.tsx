import { type RemoteBranchInfo } from "../../../ipc/bindings";
import { formatRelativeDate } from "../../../shared/utils/format";

/**
 * A row for a remote-only branch in the BranchDropdown.
 * Clicking it checks out the branch (creating a local tracking branch).
 */
export function RemoteBranchRow({
  branch,
  onSelect,
}: {
  branch: RemoteBranchInfo;
  onSelect: () => void;
}) {
  return (
    <div
      data-testid={`remote-branch-row-${branch.name}`}
      className="flex w-full items-center gap-1 px-1 py-0.5 text-sm transition-colors hover:bg-bg-hover text-fg"
    >
      {/* Spacer to align with eye toggle column in local branches */}
      <div className="w-6 flex-shrink-0" />

      {/* Branch name (click to checkout) */}
      <button
        onClick={onSelect}
        className="flex min-w-0 flex-1 items-center gap-2 px-1 py-1 text-left cursor-pointer"
      >
        <span className="truncate ml-5" title={branch.name}>{branch.name}</span>
      </button>

      {/* Spacers to align with merge+delete columns */}
      <div className="w-6 flex-shrink-0" />
      <div className="w-6 flex-shrink-0" />

      {/* Last commit date — placed after button spacers to match local branch layout */}
      <span className="flex-shrink-0 text-xs text-fg-muted pr-1">
        {branch.remote} · {formatRelativeDate(new Date(branch.last_commit_date))}
      </span>
    </div>
  );
}
