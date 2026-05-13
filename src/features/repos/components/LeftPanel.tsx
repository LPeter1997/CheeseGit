import { useState } from "react";
import { HistoryList } from "../../history";
import { StagingPanel } from "../../staging";

type Tab = "staging" | "history";

interface LeftPanelProps {
  repoPath: string;
}

export function LeftPanel({ repoPath }: LeftPanelProps) {
  const [activeTab, setActiveTab] = useState<Tab>("staging");

  return (
    <div className="flex h-full flex-col">
      <div className="flex border-b border-border">
        <TabButton
          label="Staging"
          active={activeTab === "staging"}
          onClick={() => setActiveTab("staging")}
        />
        <TabButton
          label="History"
          active={activeTab === "history"}
          onClick={() => setActiveTab("history")}
        />
      </div>

      <div className="flex-1 overflow-auto">
        {activeTab === "staging" && <StagingPanel repoPath={repoPath} />}
        {activeTab === "history" && <HistoryList />}
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
      className={`flex-1 px-3 py-1.5 text-xs font-medium transition-colors ${
        active
          ? "border-b-2 border-accent text-fg"
          : "text-fg-muted hover:text-fg"
      }`}
    >
      {label}
    </button>
  );
}
