import { create } from "zustand";
import { commands, type RepoInfo } from "../../ipc/bindings";

interface ReposState {
  repos: RepoInfo[];
  activeIndex: number;
  initialized: boolean;
  initialize: () => Promise<void>;
  openRepo: (path: string) => Promise<string | null>;
  setActiveIndex: (index: number) => void;
  closeRepo: (index: number) => void;
  moveRepo: (fromIndex: number, toIndex: number) => void;
}

function persistState(repos: RepoInfo[], activeIndex: number) {
  commands.saveAppState({
    open_repos: repos.map((r) => r.path),
    active_index: activeIndex,
  });
}

export const useReposStore = create<ReposState>((set, get) => ({
  repos: [],
  activeIndex: -1,
  initialized: false,

  initialize: async () => {
    if (get().initialized) return;

    const saved = await commands.getAppState();
    const paths = saved.open_repos ?? [];

    const results = await Promise.all(
      paths.map((p) => commands.openRepository(p)),
    );

    const repos: RepoInfo[] = [];
    for (const result of results) {
      if (result.status === "ok") {
        repos.push(result.data);
      }
    }

    const activeIndex = Math.min(
      saved.active_index ?? 0,
      repos.length - 1,
    );

    set({ repos, activeIndex: repos.length > 0 ? activeIndex : -1, initialized: true });
  },

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
      persistState(repos, existing);
      return null;
    }

    const newRepos = [...repos, info];
    const newIndex = newRepos.length - 1;
    set({ repos: newRepos, activeIndex: newIndex });
    persistState(newRepos, newIndex);
    return null;
  },

  setActiveIndex: (index: number) => {
    set({ activeIndex: index });
    persistState(get().repos, index);
  },

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
    persistState(newRepos, newActive);
  },

  moveRepo: (fromIndex: number, toIndex: number) => {
    if (fromIndex === toIndex) return;
    const { repos, activeIndex } = get();
    const newRepos = [...repos];
    const [moved] = newRepos.splice(fromIndex, 1);
    newRepos.splice(toIndex, 0, moved);

    // Adjust activeIndex to follow the active tab
    let newActive = activeIndex;
    if (activeIndex === fromIndex) {
      newActive = toIndex;
    } else if (fromIndex < activeIndex && toIndex >= activeIndex) {
      newActive = activeIndex - 1;
    } else if (fromIndex > activeIndex && toIndex <= activeIndex) {
      newActive = activeIndex + 1;
    }

    set({ repos: newRepos, activeIndex: newActive });
    persistState(newRepos, newActive);
  },
}));
