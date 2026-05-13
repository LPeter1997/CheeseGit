import { create } from "zustand";
import { commands, type StatusEntry } from "../../ipc/bindings";
import { useToastStore } from "../../shared/stores/toast";

interface StagingState {
  staged: StatusEntry[];
  unstaged: StatusEntry[];
  summary: string;
  description: string;
  loading: boolean;
  committing: boolean;
  defaultSummary: string;
  /** Whether fetchStatus has completed at least once. */
  initialized: boolean;
  fetchStatus: (repoPath: string) => Promise<void>;
  setSummary: (summary: string) => void;
  setDescription: (description: string) => void;
  commit: (repoPath: string) => Promise<boolean>;
  stageFile: (repoPath: string, path: string) => Promise<void>;
  unstageFile: (repoPath: string, path: string) => Promise<void>;
  stageAll: (repoPath: string) => Promise<void>;
  unstageAll: (repoPath: string) => Promise<void>;
  clear: () => void;
}

function computeDefaultSummary(staged: StatusEntry[]): string {
  if (staged.length === 1) {
    const filename = staged[0].path.split("/").pop() ?? staged[0].path;
    return `Update ${filename}`;
  }
  return "";
}

export const useStagingStore = create<StagingState>((set, get) => ({
  staged: [],
  unstaged: [],
  summary: "",
  description: "",
  loading: false,
  committing: false,
  defaultSummary: "",
  initialized: false,

  fetchStatus: async (repoPath: string) => {
    // Only show the loading spinner on the very first fetch.
    // Subsequent refreshes update data silently to avoid flicker.
    if (!get().initialized) {
      set({ loading: true });
    }
    const result = await commands.getStatus(repoPath);
    if (result.status === "ok") {
      const data = result.data;
      const defaultSummary = computeDefaultSummary(data.staged);
      set({
        staged: data.staged,
        unstaged: data.unstaged,
        loading: false,
        initialized: true,
        defaultSummary,
      });
    } else {
      set({ staged: [], unstaged: [], loading: false, initialized: true, defaultSummary: "" });
    }
  },

  setSummary: (summary: string) => set({ summary }),
  setDescription: (description: string) => set({ description }),

  commit: async (repoPath: string): Promise<boolean> => {
    const { summary, defaultSummary, description } = get();
    const msg = summary || defaultSummary;
    if (!msg) return false;

    set({ committing: true });
    const result = await commands.commit(repoPath, msg, description);
    set({ committing: false });

    if (result.status === "error") {
      const err = result.error;
      useToastStore.getState().addToast(
        err.Git ?? err.Io ?? err.Other ?? "Failed to commit",
      );
      return false;
    }

    // Reset form and refresh status.
    set({ summary: "", description: "", defaultSummary: "" });
    get().fetchStatus(repoPath);
    return true;
  },

  stageFile: async (repoPath: string, path: string) => {
    // Optimistic update: move file from unstaged → staged immediately
    const { staged, unstaged } = get();
    const entry = unstaged.find((e) => e.path === path);
    if (entry) {
      const newStaged = [...staged, entry];
      set({
        staged: newStaged,
        unstaged: unstaged.filter((e) => e.path !== path),
        defaultSummary: computeDefaultSummary(newStaged),
      });
    }
    const result = await commands.stageFiles(repoPath, [path]);
    if (result.status === "ok") {
      get().fetchStatus(repoPath);
    }
  },

  unstageFile: async (repoPath: string, path: string) => {
    // Optimistic update: move file from staged → unstaged immediately
    const { staged, unstaged } = get();
    const entry = staged.find((e) => e.path === path);
    if (entry) {
      const newStaged = staged.filter((e) => e.path !== path);
      set({
        staged: newStaged,
        unstaged: [...unstaged, entry],
        defaultSummary: computeDefaultSummary(newStaged),
      });
    }
    const result = await commands.unstageFiles(repoPath, [path]);
    if (result.status === "ok") {
      get().fetchStatus(repoPath);
    }
  },

  stageAll: async (repoPath: string) => {
    const { staged, unstaged } = get();
    if (unstaged.length === 0) return;
    // Optimistic update: move all unstaged → staged
    const paths = unstaged.map((e) => e.path);
    const newStaged = [...staged, ...unstaged];
    set({
      staged: newStaged,
      unstaged: [],
      defaultSummary: computeDefaultSummary(newStaged),
    });
    const result = await commands.stageFiles(repoPath, paths);
    if (result.status === "ok") {
      get().fetchStatus(repoPath);
    }
  },

  unstageAll: async (repoPath: string) => {
    const { staged, unstaged } = get();
    if (staged.length === 0) return;
    // Optimistic update: move all staged → unstaged
    const paths = staged.map((e) => e.path);
    set({
      staged: [],
      unstaged: [...unstaged, ...staged],
      defaultSummary: "",
    });
    const result = await commands.unstageFiles(repoPath, paths);
    if (result.status === "ok") {
      get().fetchStatus(repoPath);
    }
  },

  clear: () =>
    set({
      staged: [],
      unstaged: [],
      summary: "",
      description: "",
      loading: false,
      committing: false,
      defaultSummary: "",
      initialized: false,
    }),
}));
