import { useState, useCallback, useEffect, useRef } from "react";
import { commands, type RepoInfo } from "../../../ipc/bindings";
import { useAlertStore } from "../../../shared/stores/alerts";
import { AlertBanners } from "../../../shared/components/AlertBanners";
import { useResize } from "../../../shared/hooks/useResize";
import { extractErrorMessage } from "../../../shared/utils/errors";
import { useRepoPolling } from "../hooks/useRepoPolling";
import { BranchBar } from "./BranchBar";
import { LeftPanel } from "./LeftPanel";
import { DiffPanel } from "./DiffPanel";
import { CommitDiffPanel, graphWidth } from "../../history";
import { useHistoryStore } from "../../history";
import { useStagingStore } from "../../staging/store";

/** Per-repo active tab memory (staging vs history). */
const repoTabMap = new Map<string, "staging" | "history">();
/** Per-repo effective panel width memory (avoids bump on tab switch). */
const repoPanelWidthMap = new Map<string, number>();

interface RepoViewProps {
  repo: RepoInfo;
}

export function RepoView({ repo }: RepoViewProps) {
  const [switching, setSwitching] = useState(false);
  const [selectedRemote, setSelectedRemote] = useState<string | null>(null);
  const [activeTab, setActiveTab] = useState<"staging" | "history">(
    repoTabMap.get(repo.path) ?? "staging",
  );
  const addAlert = useAlertStore((s) => s.addAlert);
  const { currentBranch, tracking, refresh } = useRepoPolling(repo.path, selectedRemote);
  const selectedHash = useHistoryStore((s) => s.selectedHash);

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

  const showCommitDiff = activeTab === "history" && selectedHash !== null;

  const graphLayout = useHistoryStore((s) => s.graphLayout);
  const graphColumnCount = graphLayout?.columnCount ?? 0;
  /** Minimum width for commit text (message + author + date). */
  const MIN_TEXT_WIDTH = 320;
  const graphMinWidth = graphWidth(graphColumnCount) + MIN_TEXT_WIDTH;

  const { size: panelWidth, setSize: setPanelWidth, onMouseDown: onResizeColumn } = useResize({
    direction: "horizontal",
    initialSize: repoPanelWidthMap.get(repo.path) ?? 280,
    minSize: graphMinWidth,
    maxSize: Math.max(600, graphMinWidth),
  });

  // Restore cached panel width when switching repos.
  const prevRepoPath = useRef(repo.path);
  useEffect(() => {
    if (prevRepoPath.current !== repo.path) {
      const cached = repoPanelWidthMap.get(repo.path);
      if (cached) {
        setPanelWidth(cached);
      }
      prevRepoPath.current = repo.path;
    }
  }, [repo.path, setPanelWidth]);

  const effectivePanelWidth = Math.max(panelWidth, graphMinWidth);

  // Cache effective panel width whenever it changes.
  useEffect(() => {
    repoPanelWidthMap.set(repo.path, effectivePanelWidth);
  }, [repo.path, effectivePanelWidth]);

  const handleSwitch = useCallback(async (branchName: string) => {
    setSwitching(true);
    const result = await commands.switchBranch(repo.path, branchName);
    setSwitching(false);
    if (result.status === "error") {
      addAlert(extractErrorMessage(result.error, "Failed to switch branch"));
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
      addAlert(extractErrorMessage(result.error, "Failed to create branch"));
      return;
    }
    useStagingStore.getState().emptyCommitMode && useStagingStore.setState({ emptyCommitMode: false });
    refresh();
  }, [repo.path, refresh, addAlert]);

  const handleRemoteChange = useCallback((remote: string | null) => {
    setSelectedRemote(remote);
    // Trigger an immediate refresh so the graph updates with the new remote.
    // Use a small delay to let the state update propagate.
    setTimeout(refresh, 0);
  }, [refresh]);

  return (
    <div className="flex h-full flex-col">
      <BranchBar
        repoPath={repo.path}
        currentBranch={currentBranch}
        tracking={tracking}
        switching={switching}
        panelWidth={effectivePanelWidth}
        onSwitch={handleSwitch}
        onCreate={handleCreate}
        onRemoteComplete={refresh}
        onRemoteChange={handleRemoteChange}
      />
      <AlertBanners />
      <div className="flex flex-1 overflow-hidden">
        <div style={{ width: effectivePanelWidth }} className="flex-shrink-0 overflow-hidden">
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
