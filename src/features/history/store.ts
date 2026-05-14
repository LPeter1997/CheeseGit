import { create } from "zustand";
import { commands, type CommitInfo, type FileDiff, type StatusEntry } from "../../ipc/bindings";

interface HistoryState {
  commits: CommitInfo[];
  selectedIndex: number;
  loading: boolean;
  /** List of files changed in the selected commit. */
  commitFiles: StatusEntry[];
  commitFilesLoading: boolean;
  /** Currently selected file within the commit. */
  selectedFilePath: string | null;
  selectedFileDiff: FileDiff | null;
  selectedFileContent: string | null;
  selectedFileDiffLoading: boolean;
  fetchLog: (repoPath: string) => Promise<void>;
  selectCommit: (index: number, repoPath: string) => void;
  selectCommitFile: (filePath: string, repoPath: string) => void;
  clear: () => void;
}

export const useHistoryStore = create<HistoryState>((set, get) => ({
  commits: [],
  selectedIndex: -1,
  loading: false,
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
    set({
      selectedFilePath: filePath,
      selectedFileDiff: null,
      selectedFileContent: null,
      selectedFileDiffLoading: true,
    });
    const commit = get().commits[get().selectedIndex];
    if (!commit) {
      set({ selectedFileDiffLoading: false });
      return;
    }

    const [diffResult, contentResult] = await Promise.all([
      commands.getCommitFileDiff(repoPath, commit.hash, filePath),
      commands.getFileAtCommit(repoPath, commit.hash, filePath),
    ]);

    set({
      selectedFileDiff: diffResult.status === "ok" ? diffResult.data : null,
      selectedFileContent: contentResult.status === "ok" ? contentResult.data : null,
      selectedFileDiffLoading: false,
    });
  },

  clear: () => set({
    commits: [],
    selectedIndex: -1,
    loading: false,
    commitFiles: [],
    commitFilesLoading: false,
    selectedFilePath: null,
    selectedFileDiff: null,
    selectedFileContent: null,
    selectedFileDiffLoading: false,
  }),
}));
