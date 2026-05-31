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
import { CherryPickBranchPicker, CommitDiffPanel, graphWidth } from "../../history";
import { useHistoryStore } from "../../history";
import { useDiffStore } from "../../diff/store";
import { useDiffToolbarStore, type DiffToolbarContext } from "../../diff/toolbar-store";
import { useStagingStore } from "../../staging/store";
import { useStashStore, StashDiffPanel } from "../../stash";
import { formatStashMessage } from "../../stash/utils/format-stash-message";
import { useMergeStore, MergeConflictDialog } from "../../merge";

/** Per-repo active tab memory (staging vs history vs stash). */
const repoTabMap = new Map<string, "staging" | "history" | "stash">();
/** Per-repo effective panel width memory (avoids bump on tab switch). */
const repoPanelWidthMap = new Map<string, number>();

interface RepoViewProps {
  repo: RepoInfo;
}

export function RepoView({ repo }: RepoViewProps) {
  const [switching, setSwitching] = useState(false);
  const [cherryPicking, setCherryPicking] = useState(false);
  const [selectedRemote, setSelectedRemote] = useState<string | null>(null);
  const [pendingCherryPickHashes, setPendingCherryPickHashes] = useState<string[] | null>(null);
  const [activeTab, setActiveTab] = useState<"staging" | "history" | "stash">(
    repoTabMap.get(repo.path) ?? "staging",
  );
  const addAlert = useAlertStore((s) => s.addAlert);
  const { currentBranch, tracking, browsingHistory, graphAnchor, setGraphAnchor, refresh } = useRepoPolling(repo.path, selectedRemote);
  const selectedHash = useHistoryStore((s) => s.selectedHash);
  const historyCommits = useHistoryStore((s) => s.commits);
  const historyGraphData = useHistoryStore((s) => s.graphData);
  const stagingSelectedFile = useDiffStore((s) => s.selectedFile);
  const stagingViewMode = useDiffStore((s) => s.viewMode);
  const setStagingViewMode = useDiffStore((s) => s.setViewMode);
  const historySelectedFile = useHistoryStore((s) => s.selectedFilePath);
  const stashSelectedFile = useStashStore((s) => s.selectedFilePath);
  const stashes = useStashStore((s) => s.stashes);
  const toolbarViewMode = useDiffToolbarStore((s) => s.viewMode);
  const toolbarQuery = useDiffToolbarStore((s) => s.query);
  const toolbarMatchStatus = useDiffToolbarStore((s) => s.matchStatus);
  const toolbarFocusSignal = useDiffToolbarStore((s) => s.focusSignal);
  const setToolbarQuery = useDiffToolbarStore((s) => s.setQuery);
  const requestToolbarNext = useDiffToolbarStore((s) => s.requestNext);
  const requestToolbarPrevious = useDiffToolbarStore((s) => s.requestPrevious);
  const setToolbarViewMode = useDiffToolbarStore((s) => s.setViewMode);

  // Track the branch the user was on before entering history-browsing mode.
  const previousBranchRef = useRef<string | null>(null);
  useEffect(() => {
    if (!browsingHistory && currentBranch && currentBranch !== "HEAD") {
      previousBranchRef.current = currentBranch;
    }
  }, [currentBranch, browsingHistory]);

  // Restore per-repo tab when the repo changes (component is reused across tabs).
  useEffect(() => {
    setActiveTab(repoTabMap.get(repo.path) ?? "staging");
  }, [repo.path]);

  // Check for an in-progress merge/revert when the repo is first loaded.
  const addMergeInProgressAlert = useAlertStore((s) => s.addMergeInProgressAlert);
  useEffect(() => {
    let cancelled = false;
    commands.checkMergeState(repo.path).then((result) => {
      if (cancelled || result.status === "error") return;
      const state = result.data;
      if (state === "None") return;
      if ("Merging" in state) {
        const info = state.Merging!;
        addMergeInProgressAlert(repo.path, false, info.incoming_branch, info.conflict_count);
      } else if ("Reverting" in state) {
        const info = state.Reverting!;
        addMergeInProgressAlert(repo.path, true, info.incoming_branch, info.conflict_count);
      }
    });
    return () => { cancelled = true; };
  }, [repo.path, addMergeInProgressAlert]);

  const handleTabChange = useCallback(
    (tab: "staging" | "history" | "stash") => {
      repoTabMap.set(repo.path, tab);
      setActiveTab(tab);
    },
    [repo.path],
  );

  const showCommitDiff = activeTab === "history" && selectedHash !== null;
  const stashSelectedIndex = useStashStore((s) => s.selectedIndex);
  const showStashDiff = activeTab === "stash" && stashSelectedIndex !== null;
  const toolbarContext: DiffToolbarContext = activeTab;
  const selectedDiffFilePath = activeTab === "history"
    ? historySelectedFile
    : activeTab === "stash"
      ? stashSelectedFile
      : stagingSelectedFile;
  const historyContextLabel = (() => {
    if (!showCommitDiff || !selectedHash) return null;
    const source = historyGraphData?.commits ?? historyCommits;
    const commit = source.find((c) => c.hash === selectedHash);
    if (!commit) return null;
    return `${commit.short_hash} - ${commit.summary}`;
  })();
  const stashContextLabel = (() => {
    if (!showStashDiff || stashSelectedIndex === null) return null;
    const stash = stashes.find((s) => s.index === stashSelectedIndex);
    if (!stash) return null;
    const parsed = formatStashMessage(stash.message);
    return `${stash.short_hash} - ${parsed.title}${parsed.context ? ` (${parsed.context})` : ""}`;
  })();
  const selectionContextLabel = activeTab === "history"
    ? historyContextLabel
    : activeTab === "stash"
      ? stashContextLabel
      : null;
  const activeSearchStatus = toolbarMatchStatus[toolbarContext];
  const activeViewMode = activeTab === "staging"
    ? stagingViewMode
    : toolbarViewMode[toolbarContext];

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

  // Track drag-resize to disable width transition during manual resize.
  const [isResizing, setIsResizing] = useState(false);
  const handleResizeStart = useCallback((e: React.MouseEvent) => {
    setIsResizing(true);
    onResizeColumn(e);
  }, [onResizeColumn]);
  useEffect(() => {
    if (!isResizing) return;
    const onUp = () => setIsResizing(false);
    document.addEventListener("mouseup", onUp);
    return () => document.removeEventListener("mouseup", onUp);
  }, [isResizing]);

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

  const mergeBranch = useMergeStore((s) => s.mergeBranch);
  const revertCommit = useMergeStore((s) => s.revertCommit);
  const merging = useMergeStore((s) => s.merging);
  const undoLastCommit = useStagingStore((s) => s.undoLastCommit);

  const handleMerge = useCallback(async (branchName: string) => {
    const success = await mergeBranch(repo.path, branchName);
    if (success) {
      // Clean merge — just refresh
      refresh();
    }
    // If conflicts, the MergeConflictDialog will show automatically via merging state
  }, [repo.path, mergeBranch, refresh]);

  const handleRevert = useCallback(async (hash: string) => {
    const success = await revertCommit(repo.path, hash);
    if (success) {
      refresh();
    }
    // If conflicts, the MergeConflictDialog will show automatically
  }, [repo.path, revertCommit, refresh]);

  const handleUndoLastCommit = useCallback(async (_hash: string) => {
    const success = await undoLastCommit(repo.path);
    if (success) {
      refresh();
      handleTabChange("staging");
    }
  }, [repo.path, undoLastCommit, refresh, handleTabChange]);

  const handleCheckoutCommit = useCallback(async (hash: string) => {
    // If the target commit is the tip of a local branch, switch to that
    // branch instead (reattaches HEAD automatically).
    const branchesResult = await commands.listBranches(repo.path);
    if (branchesResult.status === "ok") {
      const localBranchNames = new Set(branchesResult.data.map((b) => b.name));
      const graphData = useHistoryStore.getState().graphData;
      if (graphData) {
        const commit = graphData.commits.find((c) => c.hash === hash);
        if (commit) {
          // Find a local branch ref on this commit.
          const localBranch = commit.refs.find((r) => localBranchNames.has(r));
          if (localBranch) {
            const result = await commands.switchBranch(repo.path, localBranch);
            if (result.status === "error") {
              addAlert(extractErrorMessage(result.error, "Failed to switch branch"));
              return;
            }
            refresh();
            return;
          }
        }
      }
    }

    // Determine which branch this commit belongs to from the graph layout,
    // so BranchBar shows the correct "exploring history" context.
    const graphLayout = useHistoryStore.getState().graphLayout;
    if (graphLayout) {
      const node = graphLayout.nodes.find((n) => n.hash === hash);
      if (node) {
        setGraphAnchor(node.branch);
      }
    }

    const result = await commands.checkoutCommit(repo.path, hash);
    if (result.status === "error") {
      addAlert(extractErrorMessage(result.error, "Failed to checkout commit"));
      return;
    }
    refresh();
  }, [repo.path, refresh, addAlert, setGraphAnchor]);

  const handleJumpToPresent = useCallback(async () => {
    // Jump to the branch whose history we're exploring (graphAnchor),
    // falling back to the branch we were on before detaching.
    let branch = graphAnchor ?? previousBranchRef.current;
    if (!branch) return;
    // Verify the branch exists locally before switching. If it doesn't
    // (e.g. a remote-only branch like "origin/main"), try the local
    // counterpart or fall back to the previous branch.
    const branchesResult = await commands.listBranches(repo.path);
    if (branchesResult.status === "ok") {
      const localNames = new Set(branchesResult.data.map((b) => b.name));
      if (!localNames.has(branch)) {
        // Try stripping the remote prefix (e.g. "origin/main" → "main").
        const slashIdx = branch.indexOf("/");
        const localName = slashIdx >= 0 ? branch.slice(slashIdx + 1) : null;
        if (localName && localNames.has(localName)) {
          branch = localName;
        } else {
          // No local equivalent; fall back to previous branch.
          branch = previousBranchRef.current;
          if (!branch) return;
        }
      }
    }
    const result = await commands.switchBranch(repo.path, branch);
    if (result.status === "error") {
      addAlert(extractErrorMessage(result.error, "Failed to return to branch"));
      return;
    }
    refresh();
  }, [repo.path, refresh, addAlert, graphAnchor]);

  const handleOpenCherryPick = useCallback((hashes: string[]) => {
    if (hashes.length === 0) return;
    setPendingCherryPickHashes(hashes);
  }, []);

  const handleCherryPickToBranch = useCallback(async (branchName: string, createBranch: boolean) => {
    if (!pendingCherryPickHashes || pendingCherryPickHashes.length === 0) return;
    setCherryPicking(true);
    const result = await commands.cherryPickCommits(repo.path, pendingCherryPickHashes, branchName, createBranch);
    setCherryPicking(false);

    if (result.status === "error") {
      addAlert(extractErrorMessage(result.error, "Failed to cherry-pick commits"));
      return;
    }

    addAlert(
      `Cherry-picked ${pendingCherryPickHashes.length} commit${pendingCherryPickHashes.length === 1 ? "" : "s"} to ${branchName}`,
      "info",
    );
    setPendingCherryPickHashes(null);
    refresh();
  }, [pendingCherryPickHashes, repo.path, addAlert, refresh]);

  return (
    <div className="flex h-full flex-col">
      <BranchBar
        repoPath={repo.path}
        currentBranch={browsingHistory ? (graphAnchor ?? previousBranchRef.current) : currentBranch}
        browsingHistory={browsingHistory}
        tracking={browsingHistory ? null : tracking}
        switching={switching}
        panelWidth={effectivePanelWidth}
        onSwitch={handleSwitch}
        onCreate={handleCreate}
        onMerge={handleMerge}
        onRemoteComplete={refresh}
        onRemoteChange={handleRemoteChange}
        selectionContextLabel={selectionContextLabel}
        selectedFilePath={selectedDiffFilePath}
        searchQuery={toolbarQuery[toolbarContext]}
        onSearchQueryChange={(query) => setToolbarQuery(toolbarContext, query)}
        searchCurrentIndex={activeSearchStatus.currentIndex}
        searchTotalMatches={activeSearchStatus.totalMatches}
        searchIsSearching={activeSearchStatus.isSearching}
        onSearchNext={() => requestToolbarNext(toolbarContext)}
        onSearchPrevious={() => requestToolbarPrevious(toolbarContext)}
        searchFocusSignal={toolbarFocusSignal[toolbarContext]}
        viewMode={activeViewMode}
        onViewModeChange={(mode) => {
          if (activeTab === "staging") {
            setStagingViewMode(mode);
            setToolbarViewMode("staging", mode);
            return;
          }
          setToolbarViewMode(toolbarContext, mode);
        }}
      />
      <AlertBanners />
      <div className="flex flex-1 overflow-hidden">
        <div style={{ width: effectivePanelWidth }} className={`flex-shrink-0 overflow-hidden${isResizing ? '' : ' transition-[width] duration-200 ease-out'}`}>
          <LeftPanel repoPath={repo.path} currentBranch={currentBranch} browsingHistory={browsingHistory} activeTab={activeTab} onTabChange={handleTabChange} onCommit={refresh} onCheckoutCommit={handleCheckoutCommit} onRevertCommit={handleRevert} onUndoLastCommit={handleUndoLastCommit} onJumpToPresent={handleJumpToPresent} onCherryPickCommits={handleOpenCherryPick} />
        </div>
        <div
          onMouseDown={handleResizeStart}
          className="w-1 flex-shrink-0 cursor-col-resize border-l border-border hover:bg-accent/40 active:bg-accent/60"
        />
        <div className="flex-1 overflow-auto">
          {showCommitDiff ? <CommitDiffPanel repoPath={repo.path} viewMode={toolbarViewMode.history} onViewModeChange={(mode) => setToolbarViewMode("history", mode)} /> : showStashDiff ? <StashDiffPanel repoPath={repo.path} viewMode={toolbarViewMode.stash} onViewModeChange={(mode) => setToolbarViewMode("stash", mode)} /> : <DiffPanel repoPath={repo.path} />}
        </div>
      </div>
      {pendingCherryPickHashes && (
        <CherryPickBranchPicker
          repoPath={repo.path}
          selectedCount={pendingCherryPickHashes.length}
          busy={cherryPicking}
          onSelect={handleCherryPickToBranch}
          onClose={() => {
            if (cherryPicking) return;
            setPendingCherryPickHashes(null);
          }}
        />
      )}
      {merging && <MergeConflictDialog repoPath={repo.path} onResolved={refresh} />}
    </div>
  );
}
