import { useState, useCallback } from "react";
import { commands, type RepoInfo } from "../../../ipc/bindings";
import { useToastStore } from "../../../shared/stores/toast";
import { useRepoPolling } from "../hooks/useRepoPolling";
import { BranchBar } from "./BranchBar";
import { LeftPanel } from "./LeftPanel";
import { DiffPanel } from "./DiffPanel";
import { CommitDiffPanel } from "../../history";
import { useHistoryStore } from "../../history";

interface RepoViewProps {
  repo: RepoInfo;
}

export function RepoView({ repo }: RepoViewProps) {
  const [switching, setSwitching] = useState(false);
  const [activeTab, setActiveTab] = useState<"staging" | "history">("staging");
  const addToast = useToastStore((s) => s.addToast);
  const { currentBranch, tracking, refresh } = useRepoPolling(repo.path);
  const selectedIndex = useHistoryStore((s) => s.selectedIndex);

  const showCommitDiff = activeTab === "history" && selectedIndex >= 0;

  const handleSwitch = useCallback(async (branchName: string) => {
    setSwitching(true);
    const result = await commands.switchBranch(repo.path, branchName);
    setSwitching(false);
    if (result.status === "error") {
      const err = result.error;
      addToast(err.Git ?? err.Io ?? err.Other ?? "Failed to switch branch");
      return;
    }
    refresh();
  }, [repo.path, refresh, addToast]);

  const handleCreate = useCallback(async (branchName: string) => {
    setSwitching(true);
    const result = await commands.createBranch(repo.path, branchName);
    setSwitching(false);
    if (result.status === "error") {
      const err = result.error;
      addToast(err.Git ?? err.Io ?? err.Other ?? "Failed to create branch");
      return;
    }
    refresh();
  }, [repo.path, refresh, addToast]);

  return (
    <div className="flex h-full flex-col">
      <BranchBar
        repoPath={repo.path}
        currentBranch={currentBranch}
        tracking={tracking}
        switching={switching}
        onSwitch={handleSwitch}
        onCreate={handleCreate}
        onRemoteComplete={refresh}
      />
      <div className="flex flex-1 overflow-hidden">
        <div className="w-80 flex-shrink-0 border-r border-border overflow-hidden">
          <LeftPanel repoPath={repo.path} activeTab={activeTab} onTabChange={setActiveTab} />
        </div>
        <div className="flex-1 overflow-auto">
          {showCommitDiff ? <CommitDiffPanel repoPath={repo.path} /> : <DiffPanel repoPath={repo.path} />}
        </div>
      </div>
    </div>
  );
}
