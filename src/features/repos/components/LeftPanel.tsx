import { HistoryList } from "../../history";
import { StagingPanel } from "../../staging";
import { StashList } from "../../stash";
import { useStashStore } from "../../stash/store";

export type LeftPanelTab = "staging" | "history" | "stash";

interface LeftPanelProps {
  repoPath: string;
  currentBranch: string | null;
  browsingHistory: boolean;
  activeTab: LeftPanelTab;
  onTabChange: (tab: LeftPanelTab) => void;
  onCommit?: () => void;
  onCheckoutCommit?: (hash: string) => void;
  onRevertCommit?: (hash: string) => void;
  onJumpToPresent?: () => void;
}

export function LeftPanel({ repoPath, currentBranch, browsingHistory, activeTab, onTabChange, onCommit, onCheckoutCommit, onRevertCommit, onJumpToPresent }: LeftPanelProps) {
  const stashCount = useStashStore((s) => s.stashes.length);

  return (
    <div className="flex h-full flex-col">
      <div className="flex border-b border-border">
        <TabButton
          label="Staging"
          active={activeTab === "staging"}
          onClick={() => onTabChange("staging")}
          testId="left-tab-staging"
        />
        <TabButton
          label="History"
          active={activeTab === "history"}
          onClick={() => onTabChange("history")}
          testId="left-tab-history"
        />
        {stashCount > 0 && (
          <TabButton
            label={`Stash (${stashCount})`}
            active={activeTab === "stash"}
            onClick={() => onTabChange("stash")}
            testId="left-tab-stash"
          />
        )}
      </div>

      <div className="flex-1 overflow-auto">
        {activeTab === "staging" && <StagingPanel repoPath={repoPath} currentBranch={currentBranch} browsingHistory={browsingHistory} onCommit={onCommit} />}
        {activeTab === "history" && <HistoryList repoPath={repoPath} browsingHistory={browsingHistory} onCheckoutCommit={onCheckoutCommit} onRevertCommit={onRevertCommit} onJumpToPresent={onJumpToPresent} />}
        {activeTab === "stash" && <StashList repoPath={repoPath} onApply={onCommit} />}
      </div>
    </div>
  );
}

function TabButton({
  label,
  active,
  onClick,
  testId,
}: {
  label: string;
  active: boolean;
  onClick: () => void;
  testId?: string;
}) {
  return (
    <button
      onClick={onClick}
      data-testid={testId}
      className={`flex-1 cursor-pointer px-3 py-2 text-xs font-medium transition-colors ${
        active
          ? "border-b-2 border-accent text-fg"
          : "text-fg-muted hover:text-fg"
      }`}
    >
      {label}
    </button>
  );
}
