import { useState, useCallback, useEffect } from "react";
import { commands, type RepoInfo } from "../../../ipc/bindings";
import { useAlertStore } from "../../../shared/stores/alerts";
import { AlertBanners } from "../../../shared/components/AlertBanners";
import { useResize } from "../../../shared/hooks/useResize";
import { useRepoPolling } from "../hooks/useRepoPolling";
import { BranchBar } from "./BranchBar";
import { LeftPanel } from "./LeftPanel";
import { DiffPanel } from "./DiffPanel";
import { CommitDiffPanel } from "../../history";
import { useHistoryStore } from "../../history";
import { useStagingStore } from "../../staging/store";

/** Per-repo active tab memory (staging vs history). */
const repoTabMap = new Map<string, "staging" | "history">();

interface RepoViewProps {
  repo: RepoInfo;
}

export function RepoView({ repo }: RepoViewProps) {
  const [switching, setSwitching] = useState(false);
  const [activeTab, setActiveTab] = useState<"staging" | "history">(
    repoTabMap.get(repo.path) ?? "staging",
  );
  const addAlert = useAlertStore((s) => s.addAlert);
  const { currentBranch, tracking, refresh } = useRepoPolling(repo.path);
  const selectedIndex = useHistoryStore((s) => s.selectedIndex);

  // Restore per-repo tab when the repo changes (component is reused across tabs).
  useEffect(() => {
    setActiveTab(repoTabMap.get(repo.path) ?? "staging");
  }, [repo.path]);

  const handleTabChange = useCallback(
    (tab: "staging" | "history") => {
      repoTabMap.set(repo.path, tab);
      setActiveTab(tab);
    },
    [repo.path],
  );

  const showCommitDiff = activeTab === "history" && selectedIndex >= 0;

  const { size: panelWidth, onMouseDown: onResizeColumn } = useResize({
    direction: "horizontal",
    initialSize: 320,
    minSize: 200,
    maxSize: 600,
  });

  const handleSwitch = useCallback(async (branchName: string) => {
    setSwitching(true);
    const result = await commands.switchBranch(repo.path, branchName);
    setSwitching(false);
    if (result.status === "error") {
      const err = result.error;
      addAlert(err.Git ?? err.Io ?? err.Other ?? "Failed to switch branch");
      return;
    }
    useStagingStore.getState().emptyCommitMode && useStagingStore.setState({ emptyCommitMode: false });
    refresh();
  }, [repo.path, refresh, addAlert]);

  const handleCreate = useCallback(async (branchName: string) => {
    setSwitching(true);
    const result = await commands.createBranch(repo.path, branchName);
    setSwitching(false);
    if (result.status === "error") {
      const err = result.error;
      addAlert(err.Git ?? err.Io ?? err.Other ?? "Failed to create branch");
      return;
    }
    useStagingStore.getState().emptyCommitMode && useStagingStore.setState({ emptyCommitMode: false });
    refresh();
  }, [repo.path, refresh, addAlert]);

  return (
    <div className="flex h-full flex-col">
      <BranchBar
        repoPath={repo.path}
        currentBranch={currentBranch}
        tracking={tracking}
        switching={switching}
        panelWidth={panelWidth}
        onSwitch={handleSwitch}
        onCreate={handleCreate}
        onRemoteComplete={refresh}
      />
      <AlertBanners />
      <div className="flex flex-1 overflow-hidden">
        <div style={{ width: panelWidth }} className="flex-shrink-0 overflow-hidden">
          <LeftPanel repoPath={repo.path} currentBranch={currentBranch} activeTab={activeTab} onTabChange={handleTabChange} onCommit={refresh} />
        </div>
        <div
          onMouseDown={onResizeColumn}
          className="w-1 flex-shrink-0 cursor-col-resize border-r border-border hover:bg-accent/40 active:bg-accent/60"
        />
        <div className="flex-1 overflow-auto">
          {showCommitDiff ? <CommitDiffPanel repoPath={repo.path} /> : <DiffPanel repoPath={repo.path} />}
        </div>
      </div>
    </div>
  );
}
