import { create } from "zustand";
import { commands, type CommitInfo, type BranchGraphData, type FileDiff, type StatusEntry, type FileStats } from "../../ipc/bindings";
import { LruCache } from "../../shared/utils/lru-cache";
import { PerRepoStateCache } from "../../shared/utils/per-repo-state-cache";
import { computeGraphLayout, computeRequiredBranches, type GraphLayout } from "./graph/layout";
import { ROW_HEIGHT } from "./graph/constants";

/** Fast equality check for BranchGraphData — compares commit hashes, branch list, and ref positions. */
function graphDataEqual(a: BranchGraphData, b: BranchGraphData): boolean {
  if (a.commits.length !== b.commits.length) return false;
  if (a.branches.length !== b.branches.length) return false;
  if (a.local_only_commits.length !== b.local_only_commits.length) return false;
  // Compare first and last commit hashes as a quick fingerprint.
  if (a.commits.length > 0) {
    if (a.commits[0].hash !== b.commits[0].hash) return false;
    const last = a.commits.length - 1;
    if (a.commits[last].hash !== b.commits[last].hash) return false;
  }
  // Compare branch lists (order matters).
  for (let i = 0; i < a.branches.length; i++) {
    if (a.branches[i] !== b.branches[i]) return false;
  }
  // Compare local-only commit sets (may differ when remote changes).
  const aSet = new Set(a.local_only_commits);
  for (const h of b.local_only_commits) {
    if (!aSet.has(h)) return false;
  }
  // Compare ref→commit mappings to detect branch pointer moves (e.g. after pull).
  const aRefs = new Map<string, string>();
  for (const c of a.commits) {
    for (const r of c.refs) {
      aRefs.set(r, c.hash);
    }
  }
  const bRefs = new Map<string, string>();
  for (const c of b.commits) {
    for (const r of c.refs) {
      bRefs.set(r, c.hash);
    }
  }
  if (aRefs.size !== bRefs.size) return false;
  for (const [ref, hash] of aRefs) {
    if (bRefs.get(ref) !== hash) return false;
  }
  return true;
}

interface CommitDiffCacheEntry {
  fileDiff: FileDiff | null;
  fileContent: string | null;
}

const commitDiffCache = new LruCache<string, CommitDiffCacheEntry>(10);

/** Number of commits to load per page for the graph. */
const GRAPH_PAGE_SIZE = 500;

interface SavedHistorySelection {
  selectedHash: string | null;
  commitFiles: StatusEntry[];
  selectedFilePath: string | null;
  selectedFileDiff: FileDiff | null;
  selectedFileContent: string | null;
  commits: CommitInfo[];
  graphData: BranchGraphData | null;
  graphLayout: GraphLayout | null;
  visibleBranches: string[];
  requiredBranches: string[];
  allBranches: string[];
  _layoutParams: { currentBranch: string; remote: string | null } | null;
  graphMaxCommits: number;
  hasMoreCommits: boolean;
}

/** Per-repo history state cache. */
const repoHistory = new PerRepoStateCache<SavedHistorySelection>();

interface HistoryState {
  commits: CommitInfo[];
  selectedHash: string | null;
  loading: boolean;
  /** Branch graph data from the backend. */
  graphData: BranchGraphData | null;
  /** Computed graph layout for rendering. */
  graphLayout: GraphLayout | null;
  /** The branch currently being hovered in the graph. */
  hoveredBranch: string | null;
  /** Branches currently visible in the graph. */
  visibleBranches: string[];
  /** Branches that cannot be hidden (current + waterfall ancestors). */
  requiredBranches: string[];
  /** All available branches from the graph data. */
  allBranches: string[];
  /** Cached parameters for re-computing layout on visibility change. */
  _layoutParams: { currentBranch: string; remote: string | null } | null;
  /** Current max commits limit for the graph. */
  graphMaxCommits: number;
  /** Whether there are more commits to load beyond the current limit. */
  hasMoreCommits: boolean;
  /** Whether a loadMore request is in-flight. */
  loadingMore: boolean;
  /** List of files changed in the selected commit. */
  commitFiles: StatusEntry[];
  commitFilesLoading: boolean;
  /** Per-file stats for the selected commit. */
  commitFileStats: Map<string, FileStats>;
  /** Currently selected file within the commit. */
  selectedFilePath: string | null;
  selectedFileDiff: FileDiff | null;
  selectedFileContent: string | null;
  selectedFileDiffLoading: boolean;
  fetchLog: (repoPath: string) => Promise<void>;
  fetchGraph: (repoPath: string, branches: string[], remote: string | null, currentBranch: string) => Promise<void>;
  /** Load more commits by increasing the graph limit. */
  loadMoreGraph: (repoPath: string) => Promise<void>;
  setHoveredBranch: (branch: string | null) => void;
  toggleBranchVisibility: (branch: string) => void;
  showAllBranches: () => void;
  hideNonRequired: () => void;
  selectCommit: (hash: string, repoPath: string) => void;
  selectCommitFile: (filePath: string, repoPath: string) => void;
  clear: () => void;
  /** Save current state for `from` repo and restore state for `to` repo. */
  switchRepo: (from: string | null, to: string) => void;
}

export const useHistoryStore = create<HistoryState>((set, get) => ({
  commits: [],
  selectedHash: null,
  loading: false,
  graphData: null,
  graphLayout: null,
  hoveredBranch: null,
  visibleBranches: [],
  requiredBranches: [],
  allBranches: [],
  _layoutParams: null,
  graphMaxCommits: GRAPH_PAGE_SIZE,
  hasMoreCommits: false,
  loadingMore: false,
  commitFiles: [],
  commitFilesLoading: false,
  commitFileStats: new Map(),
  selectedFilePath: null,
  selectedFileDiff: null,
  selectedFileContent: null,
  selectedFileDiffLoading: false,

  fetchLog: async (repoPath: string) => {
    const hasExistingData = get().commits.length > 0;
    if (!hasExistingData) {
      set({ loading: true });
    }
    const result = await commands.getCommitLog(repoPath, 200);
    if (result.status === "ok") {
      // Skip state update if the log hasn't changed (avoids re-renders).
      const prev = get().commits;
      if (
        prev.length === result.data.length &&
        prev.length > 0 &&
        prev[0].hash === result.data[0].hash &&
        prev[prev.length - 1].hash === result.data[result.data.length - 1].hash
      ) {
        return;
      }
      set({ commits: result.data, loading: false });
    } else {
      if (get().commits.length === 0 && !get().loading) return;
      set({ commits: [], loading: false });
    }
  },

  fetchGraph: async (repoPath: string, branches: string[], remote: string | null, currentBranch: string) => {
    const maxCommits = get().graphMaxCommits;
    const result = await commands.getBranchGraph(repoPath, branches, remote, maxCommits);
    if (result.status === "ok") {
      const data = result.data;

      // Skip expensive layout recomputation if the graph data hasn't changed
      // AND we already have visible branches set (guards against race where
      // switchRepo resets visibleBranches after fetchGraph populated them).
      const prev = get();
      if (prev.graphData && prev.visibleBranches.length > 0 && graphDataEqual(prev.graphData, data)) {
        return;
      }

      const localOnly = new Set(data.local_only_commits);
      const required = computeRequiredBranches(data.commits, data.branches, currentBranch, remote);

      // Preserve user's visibility choices: start from existing visible set,
      // but always include required branches. On first load, default to required branches.
      const prevVisible = prev.visibleBranches;
      let visible: string[];
      const allSet = new Set(data.branches);
      const requiredVisible = required.filter((b) => allSet.has(b));
      if (prevVisible.length === 0) {
        // First load: show only the necessary branch chain.
        visible = requiredVisible.length > 0 ? requiredVisible : [...data.branches];
      } else {
        // Keep previous choices, but ensure required branches are included
        // and remove branches that no longer exist.
        visible = [...new Set([...requiredVisible, ...prevVisible.filter((b) => allSet.has(b))])];
      }

      const layout = computeGraphLayout(data.commits, visible, localOnly, currentBranch, ROW_HEIGHT, remote);
      set({
        graphData: data,
        graphLayout: layout,
        visibleBranches: visible,
        requiredBranches: required,
        allBranches: data.branches,
        _layoutParams: { currentBranch, remote },
        hasMoreCommits: data.commits.length >= maxCommits,
      });
    }
  },

  loadMoreGraph: async (repoPath: string) => {
    const { _layoutParams, loadingMore, hasMoreCommits, graphMaxCommits } = get();
    if (!_layoutParams || loadingMore || !hasMoreCommits) return;

    const newMax = graphMaxCommits + GRAPH_PAGE_SIZE;
    set({ loadingMore: true, graphMaxCommits: newMax });

    const result = await commands.getBranchGraph(repoPath, [], _layoutParams.remote, newMax);
    if (result.status === "ok") {
      const data = result.data;
      const localOnly = new Set(data.local_only_commits);
      const { currentBranch, remote } = _layoutParams;
      const required = computeRequiredBranches(data.commits, data.branches, currentBranch, remote);

      const prevVisible = get().visibleBranches;
      const allSet = new Set(data.branches);
      const visible = [...new Set([...required, ...prevVisible.filter((b) => allSet.has(b))])];

      const layout = computeGraphLayout(data.commits, visible, localOnly, currentBranch, ROW_HEIGHT, remote);
      set({
        graphData: data,
        graphLayout: layout,
        visibleBranches: visible,
        requiredBranches: required,
        allBranches: data.branches,
        hasMoreCommits: data.commits.length >= newMax,
        loadingMore: false,
      });
    } else {
      set({ loadingMore: false });
    }
  },

  setHoveredBranch: (branch: string | null) => {
    set({ hoveredBranch: branch });
  },

  toggleBranchVisibility: (branch: string) => {
    const { requiredBranches, visibleBranches, graphData, _layoutParams } = get();
    if (!graphData || !_layoutParams) return;
    // Required branches can't be hidden.
    if (requiredBranches.includes(branch)) return;
    const visSet = new Set(visibleBranches);
    if (visSet.has(branch)) {
      visSet.delete(branch);
    } else {
      visSet.add(branch);
    }
    const visible = [...visSet];
    const localOnly = new Set(graphData.local_only_commits);
    const layout = computeGraphLayout(graphData.commits, visible, localOnly, _layoutParams.currentBranch, ROW_HEIGHT, _layoutParams.remote);
    set({ visibleBranches: visible, graphLayout: layout });
  },

  showAllBranches: () => {
    const { allBranches, graphData, _layoutParams } = get();
    if (!graphData || !_layoutParams) return;
    const localOnly = new Set(graphData.local_only_commits);
    const layout = computeGraphLayout(graphData.commits, allBranches, localOnly, _layoutParams.currentBranch, ROW_HEIGHT, _layoutParams.remote);
    set({ visibleBranches: [...allBranches], graphLayout: layout });
  },

  hideNonRequired: () => {
    const { requiredBranches, graphData, _layoutParams } = get();
    if (!graphData || !_layoutParams) return;
    const localOnly = new Set(graphData.local_only_commits);
    const layout = computeGraphLayout(graphData.commits, requiredBranches, localOnly, _layoutParams.currentBranch, ROW_HEIGHT, _layoutParams.remote);
    set({ visibleBranches: [...requiredBranches], graphLayout: layout });
  },

  selectCommit: async (hash: string, repoPath: string) => {
    set({
      selectedHash: hash,
      commitFiles: [],
      commitFilesLoading: true,
      commitFileStats: new Map(),
      selectedFilePath: null,
      selectedFileDiff: null,
      selectedFileContent: null,
      selectedFileDiffLoading: false,
    });
    const [result, statsResult] = await Promise.all([
      commands.listCommitFiles(repoPath, hash),
      commands.getCommitFileStats(repoPath, hash),
    ]);
    if (result.status === "ok") {
      const statsMap = new Map<string, FileStats>();
      if (statsResult.status === "ok") {
        for (const s of statsResult.data) statsMap.set(s.path, s);
      }
      set({ commitFiles: result.data, commitFilesLoading: false, commitFileStats: statsMap });
    } else {
      set({ commitFiles: [], commitFilesLoading: false });
    }
  },

  selectCommitFile: async (filePath: string, repoPath: string) => {
    const hash = get().selectedHash;
    const cached = hash ? commitDiffCache.get(`${hash}:${filePath}`) : undefined;

    set({
      selectedFilePath: filePath,
      selectedFileDiff: cached?.fileDiff ?? null,
      selectedFileContent: cached?.fileContent ?? null,
      selectedFileDiffLoading: !cached,
    });

    if (!hash) {
      set({ selectedFileDiffLoading: false });
      return;
    }

    const [diffResult, contentResult] = await Promise.all([
      commands.getCommitFileDiff(repoPath, hash, filePath),
      commands.getFileAtCommit(repoPath, hash, filePath),
    ]);

    const fileDiff = diffResult.status === "ok" ? diffResult.data : null;
    const fileContent = contentResult.status === "ok" ? contentResult.data : null;

    commitDiffCache.set(`${hash}:${filePath}`, { fileDiff, fileContent });

    // Only apply if still viewing this file.
    if (get().selectedFilePath === filePath) {
      set({ selectedFileDiff: fileDiff, selectedFileContent: fileContent, selectedFileDiffLoading: false });
    }
  },

  clear: () => set({
    commits: [],
    selectedHash: null,
    loading: false,
    graphData: null,
    graphLayout: null,
    hoveredBranch: null,
    visibleBranches: [],
    requiredBranches: [],
    allBranches: [],
    _layoutParams: null,
    graphMaxCommits: GRAPH_PAGE_SIZE,
    hasMoreCommits: false,
    loadingMore: false,
    commitFiles: [],
    commitFilesLoading: false,
    commitFileStats: new Map(),
    selectedFilePath: null,
    selectedFileDiff: null,
    selectedFileContent: null,
    selectedFileDiffLoading: false,
  }),

  switchRepo: (from: string | null, to: string) => {
    const current = get();
    // Save current state for the old repo.
    repoHistory.save(from, {
      selectedHash: current.selectedHash,
      commitFiles: current.commitFiles,
      selectedFilePath: current.selectedFilePath,
      selectedFileDiff: current.selectedFileDiff,
      selectedFileContent: current.selectedFileContent,
      commits: current.commits,
      graphData: current.graphData,
      graphLayout: current.graphLayout,
      visibleBranches: current.visibleBranches,
      requiredBranches: current.requiredBranches,
      allBranches: current.allBranches,
      _layoutParams: current._layoutParams,
      graphMaxCommits: current.graphMaxCommits,
      hasMoreCommits: current.hasMoreCommits,
    });
    // Restore state for the new repo.
    const saved = repoHistory.load(to);
    if (saved) {
      set({
        commits: saved.commits,
        selectedHash: saved.selectedHash,
        loading: false,
        // Don't restore graph — let the poll fetch fresh data so we never
        // show a stale graph from a different branch/state.
        graphData: null,
        graphLayout: null,
        hoveredBranch: null,
        visibleBranches: saved.visibleBranches,
        requiredBranches: saved.requiredBranches,
        allBranches: saved.allBranches,
        _layoutParams: saved._layoutParams,
        graphMaxCommits: saved.graphMaxCommits,
        hasMoreCommits: saved.hasMoreCommits,
        loadingMore: false,
        commitFiles: saved.commitFiles,
        commitFilesLoading: false,
        selectedFilePath: saved.selectedFilePath,
        selectedFileDiff: saved.selectedFileDiff,
        selectedFileContent: saved.selectedFileContent,
        selectedFileDiffLoading: false,
      });
    } else if (from) {
      // Only reset when actually switching away from another repo.
      // On cold start (from=null), the store is already in its initial state
      // and resetting would race with an in-flight fetchGraph.
      set({
        commits: [],
        selectedHash: null,
        loading: false,
        graphData: null,
        graphLayout: null,
        hoveredBranch: null,
        visibleBranches: [],
        requiredBranches: [],
        allBranches: [],
        _layoutParams: null,
        graphMaxCommits: GRAPH_PAGE_SIZE,
        hasMoreCommits: false,
        loadingMore: false,
        commitFiles: [],
        commitFilesLoading: false,
        selectedFilePath: null,
        selectedFileDiff: null,
        selectedFileContent: null,
        selectedFileDiffLoading: false,
      });
    }
  },
}));
