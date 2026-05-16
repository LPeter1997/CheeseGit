import { create } from "zustand";
import { commands, type CommitInfo, type BranchGraphData, type FileDiff, type StatusEntry } from "../../ipc/bindings";
import { LruCache } from "../../shared/utils/lru-cache";
import { computeGraphLayout, computeRequiredBranches, type GraphLayout } from "./graph/layout";
import { ROW_HEIGHT } from "./graph/constants";

interface CommitDiffCacheEntry {
  fileDiff: FileDiff | null;
  fileContent: string | null;
}

const commitDiffCache = new LruCache<string, CommitDiffCacheEntry>(10);

interface HistoryState {
  commits: CommitInfo[];
  selectedIndex: number;
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
  /** List of files changed in the selected commit. */
  commitFiles: StatusEntry[];
  commitFilesLoading: boolean;
  /** Currently selected file within the commit. */
  selectedFilePath: string | null;
  selectedFileDiff: FileDiff | null;
  selectedFileContent: string | null;
  selectedFileDiffLoading: boolean;
  fetchLog: (repoPath: string) => Promise<void>;
  fetchGraph: (repoPath: string, branches: string[], remote: string | null, currentBranch: string) => Promise<void>;
  setHoveredBranch: (branch: string | null) => void;
  toggleBranchVisibility: (branch: string) => void;
  showAllBranches: () => void;
  hideNonRequired: () => void;
  selectCommit: (index: number, repoPath: string) => void;
  selectCommitFile: (filePath: string, repoPath: string) => void;
  clear: () => void;
}

export const useHistoryStore = create<HistoryState>((set, get) => ({
  commits: [],
  selectedIndex: -1,
  loading: false,
  graphData: null,
  graphLayout: null,
  hoveredBranch: null,
  visibleBranches: [],
  requiredBranches: [],
  allBranches: [],
  _layoutParams: null,
  commitFiles: [],
  commitFilesLoading: false,
  selectedFilePath: null,
  selectedFileDiff: null,
  selectedFileContent: null,
  selectedFileDiffLoading: false,

  fetchLog: async (repoPath: string) => {
    const hasExistingData = useHistoryStore.getState().commits.length > 0;
    if (!hasExistingData) {
      set({ loading: true });
    }
    const result = await commands.getCommitLog(repoPath, 200);
    if (result.status === "ok") {
      set({ commits: result.data, loading: false });
    } else {
      set({ commits: [], loading: false });
    }
  },

  fetchGraph: async (repoPath: string, branches: string[], remote: string | null, currentBranch: string) => {
    const result = await commands.getBranchGraph(repoPath, branches, remote, 500);
    if (result.status === "ok") {
      const data = result.data;
      const localOnly = new Set(data.local_only_commits);
      const required = computeRequiredBranches(data.commits, data.branches, currentBranch, remote);

      // Preserve user's visibility choices: start from existing visible set,
      // but always include required branches. On first load, default to required only.
      const prev = get().visibleBranches;
      const prevSet = new Set(prev);
      let visible: string[];
      if (prev.length === 0) {
        // First load: default to required only.
        visible = required;
      } else {
        // Keep previous choices, but ensure required branches are included
        // and remove branches that no longer exist.
        const allSet = new Set(data.branches);
        visible = [...new Set([...required, ...prev.filter((b) => allSet.has(b))])];
      }

      const layout = computeGraphLayout(data.commits, visible, localOnly, currentBranch, ROW_HEIGHT, remote);
      set({
        graphData: data,
        graphLayout: layout,
        visibleBranches: visible,
        requiredBranches: required,
        allBranches: data.branches,
        _layoutParams: { currentBranch, remote },
      });
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

  selectCommit: async (index: number, repoPath: string) => {
    set({
      selectedIndex: index,
      commitFiles: [],
      commitFilesLoading: true,
      selectedFilePath: null,
      selectedFileDiff: null,
      selectedFileContent: null,
      selectedFileDiffLoading: false,
    });
    const commit = get().commits[index];
    if (!commit) {
      set({ commitFilesLoading: false });
      return;
    }
    const result = await commands.listCommitFiles(repoPath, commit.hash);
    if (result.status === "ok") {
      set({ commitFiles: result.data, commitFilesLoading: false });
    } else {
      set({ commitFiles: [], commitFilesLoading: false });
    }
  },

  selectCommitFile: async (filePath: string, repoPath: string) => {
    const commit = get().commits[get().selectedIndex];
    const cached = commit ? commitDiffCache.get(`${commit.hash}:${filePath}`) : undefined;

    set({
      selectedFilePath: filePath,
      selectedFileDiff: cached?.fileDiff ?? null,
      selectedFileContent: cached?.fileContent ?? null,
      selectedFileDiffLoading: !cached,
    });

    if (!commit) {
      set({ selectedFileDiffLoading: false });
      return;
    }

    const [diffResult, contentResult] = await Promise.all([
      commands.getCommitFileDiff(repoPath, commit.hash, filePath),
      commands.getFileAtCommit(repoPath, commit.hash, filePath),
    ]);

    const fileDiff = diffResult.status === "ok" ? diffResult.data : null;
    const fileContent = contentResult.status === "ok" ? contentResult.data : null;

    commitDiffCache.set(`${commit.hash}:${filePath}`, { fileDiff, fileContent });

    // Only apply if still viewing this file.
    if (get().selectedFilePath === filePath) {
      set({ selectedFileDiff: fileDiff, selectedFileContent: fileContent, selectedFileDiffLoading: false });
    }
  },

  clear: () => set({
    commits: [],
    selectedIndex: -1,
    loading: false,
    graphData: null,
    graphLayout: null,
    hoveredBranch: null,
    visibleBranches: [],
    requiredBranches: [],
    allBranches: [],
    _layoutParams: null,
    commitFiles: [],
    commitFilesLoading: false,
    selectedFilePath: null,
    selectedFileDiff: null,
    selectedFileContent: null,
    selectedFileDiffLoading: false,
  }),
}));
