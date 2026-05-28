import { useHistoryStore } from "../../history";

/**
 * Toggle to show/hide all branches in the branch graph visualization.
 * Integrated into the BranchDropdown for convenient access.
 */
export function GraphVisibilityBar() {
  const visibleBranches = useHistoryStore((s) => s.visibleBranches);
  const allBranches = useHistoryStore((s) => s.allBranches);
  const showAll = useHistoryStore((s) => s.showAllBranches);
  const hideNonReq = useHistoryStore((s) => s.hideNonRequired);

  if (allBranches.length === 0) return null;

  const allVisible = visibleBranches.length >= allBranches.length;

  return (
    <div className="flex items-center border-b border-border px-3 py-1">
      <button
        onClick={(e) => {
          e.stopPropagation();
          if (allVisible) hideNonReq(); else showAll();
        }}
        className="text-[10px] text-fg-muted transition-colors hover:text-fg cursor-pointer"
      >
        {allVisible ? "Hide all" : "Show all"}
      </button>
    </div>
  );
}
