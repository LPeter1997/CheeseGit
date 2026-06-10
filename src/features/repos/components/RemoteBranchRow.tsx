import { type RemoteBranchInfo } from "../../../ipc/bindings";
import { formatRelativeDate } from "../../../shared/utils/format";
import { MergeIcon } from "./BranchBarIcons";

/**
 * A row for a remote-only branch in the BranchDropdown.
 * Clicking it checks out the branch (creating a local tracking branch).
 */
export function RemoteBranchRow({
  branch,
  onSelect,
  onMerge,
}: {
  branch: RemoteBranchInfo;
  onSelect: () => void;
  onMerge: () => void;
}) {
  return (
    <div
      data-testid={`remote-branch-row-${branch.name}`}
      className="flex w-full items-center gap-1 px-1 py-0.5 text-sm transition-colors hover:bg-bg-hover text-fg"
    >
      {/* Action buttons grouped on the left (mirrors local BranchRow) */}
      {/* Spacer to align with the eye-toggle column on local branches */}
      <div className="w-[1.375rem] flex-shrink-0" />

      {/* Merge button — merges the remote ref into the current branch */}
      <button
        onClick={(e) => { e.stopPropagation(); onMerge(); }}
        data-testid={`merge-remote-branch-${branch.name}`}
        className="flex-shrink-0 rounded p-1 text-fg-muted/40 transition-colors hover:text-accent cursor-pointer"
        title={`Merge ${branch.remote}/${branch.name} into current branch`}
      >
        <MergeIcon />
      </button>

      {/* Spacer to align with the delete column on local branches */}
      <div className="w-[1.375rem] flex-shrink-0" />

      {/* Branch name (click to checkout) — takes all remaining space */}
      <button
        onClick={onSelect}
        className="flex min-w-0 flex-1 items-center gap-2 px-1 py-1 text-left cursor-pointer"
      >
        <span className="truncate ml-5" title={branch.name}>{branch.name}</span>
      </button>

      {/* Remote name — fixed-width column so it isn't bumped by the date */}
      <span className="w-12 flex-shrink-0 truncate text-right text-xs text-fg-muted" title={branch.remote}>
        {branch.remote}
      </span>

      {/* Last commit date — fixed-width column keeps dates aligned across rows */}
      <span className="w-16 flex-shrink-0 truncate text-right text-xs text-fg-muted pr-1">
        {formatRelativeDate(new Date(branch.last_commit_date))}
      </span>
    </div>
  );
}
