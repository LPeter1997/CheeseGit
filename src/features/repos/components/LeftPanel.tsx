import { HistoryList } from "../../history";
import { StagingPanel } from "../../staging";

export type LeftPanelTab = "staging" | "history";

interface LeftPanelProps {
  repoPath: string;
  currentBranch: string | null;
  activeTab: LeftPanelTab;
  onTabChange: (tab: LeftPanelTab) => void;
  onCommit?: () => void;
}

export function LeftPanel({ repoPath, currentBranch, activeTab, onTabChange, onCommit }: LeftPanelProps) {
  return (
    <div className="flex h-full flex-col">
      <div className="flex border-b border-border">
        <TabButton
          label="Staging"
          active={activeTab === "staging"}
          onClick={() => onTabChange("staging")}
        />
        <TabButton
          label="History"
          active={activeTab === "history"}
          onClick={() => onTabChange("history")}
        />
      </div>

      <div className="flex-1 overflow-auto">
        {activeTab === "staging" && <StagingPanel repoPath={repoPath} currentBranch={currentBranch} onCommit={onCommit} />}
        {activeTab === "history" && <HistoryList repoPath={repoPath} />}
      </div>
    </div>
  );
}

function TabButton({
  label,
  active,
  onClick,
}: {
  label: string;
  active: boolean;
  onClick: () => void;
}) {
  return (
    <button
      onClick={onClick}
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
