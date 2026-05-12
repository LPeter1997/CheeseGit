import { create } from "zustand";
import { commands, type CommitInfo } from "../../ipc/bindings";

interface HistoryState {
  commits: CommitInfo[];
  selectedIndex: number;
  loading: boolean;
  fetchLog: (repoPath: string) => Promise<void>;
  selectCommit: (index: number) => void;
  clear: () => void;
}

export const useHistoryStore = create<HistoryState>((set) => ({
  commits: [],
  selectedIndex: -1,
  loading: false,

  fetchLog: async (repoPath: string) => {
    set({ loading: true });
    const result = await commands.getCommitLog(repoPath, 200);
    if (result.status === "ok") {
      set({ commits: result.data, loading: false });
    } else {
      set({ commits: [], loading: false });
    }
  },

  selectCommit: (index: number) => set({ selectedIndex: index }),

  clear: () => set({ commits: [], selectedIndex: -1, loading: false }),
}));
