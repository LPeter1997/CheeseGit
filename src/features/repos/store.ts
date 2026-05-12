import { create } from "zustand";
import { commands, type RepoInfo } from "../../ipc/bindings";

interface ReposState {
  repos: RepoInfo[];
  activeIndex: number;
  openRepo: (path: string) => Promise<string | null>;
  setActiveIndex: (index: number) => void;
  closeRepo: (index: number) => void;
}

export const useReposStore = create<ReposState>((set, get) => ({
  repos: [],
  activeIndex: -1,

  openRepo: async (path: string): Promise<string | null> => {
    const result = await commands.openRepository(path);

    if (result.status === "error") {
      const err = result.error;
      return err.Git ?? err.Io ?? err.Other ?? "Unknown error";
    }

    const info = result.data;
    const { repos } = get();

    // If already open, just navigate to it
    const existing = repos.findIndex((r) => r.path === info.path);
    if (existing !== -1) {
      set({ activeIndex: existing });
      return null;
    }

    const newRepos = [...repos, info];
    set({ repos: newRepos, activeIndex: newRepos.length - 1 });
    return null;
  },

  setActiveIndex: (index: number) => set({ activeIndex: index }),

  closeRepo: (index: number) => {
    const { repos, activeIndex } = get();
    const newRepos = repos.filter((_, i) => i !== index);
    let newActive = activeIndex;
    if (newRepos.length === 0) {
      newActive = -1;
    } else if (index <= activeIndex) {
      newActive = Math.max(0, activeIndex - 1);
    }
    set({ repos: newRepos, activeIndex: newActive });
  },
}));
