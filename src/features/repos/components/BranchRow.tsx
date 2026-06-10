import { useState } from "react";
import { type BranchInfo } from "../../../ipc/bindings";
import { formatRelativeDate } from "../../../shared/utils/format";
import { useHistoryStore } from "../../history";
import { DeleteBranchDialog } from "./DeleteBranchDialog";
import { TrashIcon, MergeIcon, EyeIcon } from "./BranchBarIcons";

/**
 * Single branch row within the BranchDropdown.
 * Shows branch name, date, visibility toggle, and action buttons.
 */
export function BranchRow({
  branch,
  isCurrent,
  repoPath,
  onSelect,
  onMerge,
  onDelete,
}: {
  branch: BranchInfo;
  isCurrent: boolean;
  repoPath: string;
  onSelect: (name: string) => void;
  onMerge: (name: string) => void;
  onDelete: () => void;
}) {
  const visibleBranches = useHistoryStore((s) => s.visibleBranches);
  const requiredBranches = useHistoryStore((s) => s.requiredBranches);
  const toggle = useHistoryStore((s) => s.toggleBranchVisibility);
  const [deleteDialog, setDeleteDialog] = useState(false);

  const isVisible = visibleBranches.includes(branch.name);
  const isRequired = requiredBranches.includes(branch.name);

  function handleTrashClick(e: React.MouseEvent) {
    e.stopPropagation();
    setDeleteDialog(true);
  }

  return (
    <>
      <div
        data-testid={`branch-row-${branch.name}`}
        className={`flex w-full items-center gap-1 px-1 py-0.5 text-sm transition-colors hover:bg-bg-hover ${
          isCurrent ? "text-accent" : "text-fg"
        }`}
      >
        {/* Action buttons grouped on the left: show/hide, merge, delete */}
        {/* Eye toggle */}
        <button
          onClick={(e) => {
            e.stopPropagation();
            if (!isRequired) toggle(branch.name);
          }}
          className={`flex-shrink-0 rounded p-1 transition-colors cursor-pointer ${
            isRequired
              ? "text-fg-muted/40 cursor-default"
              : isVisible
                ? "text-fg-muted hover:text-fg"
                : "text-fg-muted/30 hover:text-fg-muted"
          }`}
          title={isRequired ? "Required branch (always visible)" : isVisible ? "Hide from graph" : "Show in graph"}
        >
          <EyeIcon open={isVisible} />
        </button>

        {/* Merge button — only shown for non-current branches */}
        <button
          onClick={isCurrent ? undefined : (e) => { e.stopPropagation(); onMerge(branch.name); }}
          disabled={isCurrent}
          data-testid={isCurrent ? undefined : `merge-branch-${branch.name}`}
          className={`flex-shrink-0 rounded p-1 ${isCurrent ? "invisible" : "text-fg-muted/40 transition-colors hover:text-accent cursor-pointer disabled:opacity-40"}`}
          title={isCurrent ? undefined : `Merge ${branch.name} into current branch`}
          tabIndex={isCurrent ? -1 : undefined}
        >
          <MergeIcon />
        </button>

        {/* Delete button — invisible placeholder for current branch to keep alignment */}
        <button
          onClick={isCurrent ? undefined : handleTrashClick}
          disabled={isCurrent}
          data-testid={isCurrent ? undefined : `delete-branch-${branch.name}`}
          className={`flex-shrink-0 rounded p-1 ${isCurrent ? "invisible" : "text-fg-muted/40 transition-colors hover:text-danger cursor-pointer disabled:opacity-40"}`}
          title={isCurrent ? undefined : "Delete branch"}
          tabIndex={isCurrent ? -1 : undefined}
        >
          <TrashIcon />
        </button>

        {/* Branch name (click to switch) — takes all remaining space */}
        <button
          onClick={() => onSelect(branch.name)}
          className="flex min-w-0 flex-1 items-center gap-2 px-1 py-1 text-left cursor-pointer"
        >
          {isCurrent && <span className="text-accent">✓</span>}
          <span className={`truncate ${isCurrent ? "" : "ml-5"}`} title={branch.name}>
            {branch.name}
          </span>
        </button>

        {/* Last commit date — fixed-width column keeps dates aligned across rows */}
        <span className="w-16 flex-shrink-0 truncate text-right text-xs text-fg-muted pr-1">
          {formatRelativeDate(new Date(branch.last_commit_date))}
        </span>
      </div>

      {deleteDialog && (
        <DeleteBranchDialog
          branchName={branch.name}
          repoPath={repoPath}
          onClose={() => setDeleteDialog(false)}
          onDeleted={() => {
            setDeleteDialog(false);
            onDelete();
          }}
        />
      )}
    </>
  );
}
