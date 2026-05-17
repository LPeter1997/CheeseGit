import { create } from "zustand";
import { commands, type StatusEntry } from "../../ipc/bindings";
import { useAlertStore } from "../../shared/stores/alerts";

interface SavedStagingState {
  staged: StatusEntry[];
  unstaged: StatusEntry[];
  summary: string;
  description: string;
  defaultSummary: string;
  initialized: boolean;
  emptyCommitMode: boolean;
}

/** Per-repo staging state cache. */
const repoStaging = new Map<string, SavedStagingState>();

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
  /** When true, the next commit will use --allow-empty. */
  emptyCommitMode: boolean;
  fetchStatus: (repoPath: string) => Promise<void>;
  setSummary: (summary: string) => void;
  setDescription: (description: string) => void;
  commit: (repoPath: string) => Promise<boolean>;
  stageFile: (repoPath: string, path: string) => Promise<void>;
  unstageFile: (repoPath: string, path: string) => Promise<void>;
  stageAll: (repoPath: string) => Promise<void>;
  unstageAll: (repoPath: string) => Promise<void>;
  enableEmptyCommit: () => void;
  clear: () => void;
  /** Save current state for `from` repo and restore state for `to` repo. */
  switchRepo: (from: string | null, to: string) => void;
}

function computeDefaultSummary(staged: StatusEntry[]): string {
  if (staged.length === 1) {
    const entry = staged[0];
    const filename = entry.path.split("/").pop() ?? entry.path;
    switch (entry.status) {
      case "Added":
      case "Untracked":
        return `Add ${filename}`;
      case "Deleted":
        return `Delete ${filename}`;
      case "Renamed":
        return `Rename ${filename}`;
      case "Copied":
        return `Copy ${filename}`;
      default:
        return `Update ${filename}`;
    }
  }
  return "";
}

function statusFingerprint(entries: StatusEntry[]): string {
  return entries.map((e) => `${e.path}:${e.status}`).sort().join("\n");
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
  emptyCommitMode: false,

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
      // Cancel empty commit mode only when the actual file status changed
      // (not on every periodic re-fetch that returns the same data).
      const prev = get();
      const statusChanged =
        statusFingerprint(prev.staged) !== statusFingerprint(data.staged) ||
        statusFingerprint(prev.unstaged) !== statusFingerprint(data.unstaged);
      const cancelEmpty = prev.emptyCommitMode && statusChanged;
      set({
        staged: data.staged,
        unstaged: data.unstaged,
        loading: false,
        initialized: true,
        defaultSummary,
        ...(cancelEmpty ? { emptyCommitMode: false } : {}),
      });
    } else {
      set({ staged: [], unstaged: [], loading: false, initialized: true, defaultSummary: "" });
    }
  },

  setSummary: (summary: string) => set({ summary }),
  setDescription: (description: string) => set({ description }),

  commit: async (repoPath: string): Promise<boolean> => {
    const { summary, defaultSummary, description, emptyCommitMode, staged } = get();
    const msg = summary || defaultSummary;
    if (!msg) return false;
    // Must have staged files unless in empty commit mode
    if (!emptyCommitMode && staged.length === 0) return false;

    set({ committing: true });
    const result = await commands.commit(repoPath, msg, description, emptyCommitMode);
    set({ committing: false });

    if (result.status === "error") {
      const err = result.error;
      useAlertStore.getState().addAlert(
        err.Git ?? err.Io ?? err.Other ?? "Failed to commit",
      );
      return false;
    }

    // Reset form and refresh status.
    set({ summary: "", description: "", defaultSummary: "", emptyCommitMode: false });
    get().fetchStatus(repoPath);
    return true;
  },

  stageFile: async (repoPath: string, path: string) => {
    // Cancel empty commit mode on any staging operation
    if (get().emptyCommitMode) set({ emptyCommitMode: false });
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
    // Cancel empty commit mode on any staging operation
    if (get().emptyCommitMode) set({ emptyCommitMode: false });
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
    // Cancel empty commit mode on any staging operation
    if (get().emptyCommitMode) set({ emptyCommitMode: false });
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
    // Cancel empty commit mode on any staging operation
    if (get().emptyCommitMode) set({ emptyCommitMode: false });
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

  enableEmptyCommit: () => set({ emptyCommitMode: true }),

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
      emptyCommitMode: false,
    }),

  switchRepo: (from: string | null, to: string) => {
    const current = get();
    // Save current state for the old repo.
    if (from) {
      repoStaging.set(from, {
        staged: current.staged,
        unstaged: current.unstaged,
        summary: current.summary,
        description: current.description,
        defaultSummary: current.defaultSummary,
        initialized: current.initialized,
        emptyCommitMode: current.emptyCommitMode,
      });
    }
    // Restore state for the new repo.
    const saved = repoStaging.get(to);
    if (saved) {
      set({
        staged: saved.staged,
        unstaged: saved.unstaged,
        summary: saved.summary,
        description: saved.description,
        defaultSummary: saved.defaultSummary,
        initialized: saved.initialized,
        emptyCommitMode: saved.emptyCommitMode,
        loading: false,
        committing: false,
      });
    } else {
      set({
        staged: [],
        unstaged: [],
        summary: "",
        description: "",
        defaultSummary: "",
        initialized: false,
        emptyCommitMode: false,
        loading: false,
        committing: false,
      });
    }
  },
}));
