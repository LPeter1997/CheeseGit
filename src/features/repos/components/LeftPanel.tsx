import { useState, useEffect } from "react";
import { HistoryList, useHistoryStore } from "../../history";

type Tab = "staging" | "history";

interface LeftPanelProps {
  repoPath: string;
}

export function LeftPanel({ repoPath }: LeftPanelProps) {
  const [activeTab, setActiveTab] = useState<Tab>("history");
  const fetchLog = useHistoryStore((s) => s.fetchLog);

  useEffect(() => {
    fetchLog(repoPath);
  }, [repoPath, fetchLog]);

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
        {activeTab === "staging" && <StagingPlaceholder />}
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

function StagingPlaceholder() {
  return (
    <div className="flex h-full items-center justify-center text-xs text-fg-muted">
      Staging area coming soon.
    </div>
  );
}
