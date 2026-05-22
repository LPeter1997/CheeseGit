import { create } from "zustand";
import { commands, type StatusEntry, type FileStats } from "../../ipc/bindings";
import { useAlertStore } from "../../shared/stores/alerts";
import { extractErrorMessage } from "../../shared/utils/errors";

interface SavedStagingState {
  staged: StatusEntry[];
  unstaged: StatusEntry[];
  summary: string;
  description: string;
  defaultSummary: string;
  initialized: boolean;
  emptyCommitMode: boolean;
  stagedStats: Map<string, FileStats>;
  unstagedStats: Map<string, FileStats>;
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
  /** Per-file stats for staged changes. */
  stagedStats: Map<string, FileStats>;
  /** Per-file stats for unstaged changes. */
  unstagedStats: Map<string, FileStats>;
  fetchStatus: (repoPath: string) => Promise<void>;
  setSummary: (summary: string) => void;
  setDescription: (description: string) => void;
  commit: (repoPath: string) => Promise<boolean>;
  stageFile: (repoPath: string, path: string) => Promise<void>;
  unstageFile: (repoPath: string, path: string) => Promise<void>;
  stageAll: (repoPath: string) => Promise<void>;
  unstageAll: (repoPath: string) => Promise<void>;
  discardFile: (repoPath: string, path: string, area: "Unstaged" | "Staged") => Promise<void>;
  discardAll: (repoPath: string, area: "Unstaged" | "Staged") => Promise<void>;
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
  stagedStats: new Map(),
  unstagedStats: new Map(),

  fetchStatus: async (repoPath: string) => {
    // Only show the loading spinner on the very first fetch.
    // Subsequent refreshes update data silently to avoid flicker.
    if (!get().initialized) {
      set({ loading: true });
    }
    const result = await commands.getStatus(repoPath);
    if (result.status === "ok") {
      const data = result.data;
      const prev = get();
      const statusChanged =
        statusFingerprint(prev.staged) !== statusFingerprint(data.staged) ||
        statusFingerprint(prev.unstaged) !== statusFingerprint(data.unstaged);

      // Skip file list state update when nothing changed — avoids replacing
      // array references which would trigger re-renders of all subscribers
      // (including the commit summary input) on every poll cycle.
      // Still refetch stats below since they may be stale after stage/unstage.
      if (statusChanged || !prev.initialized) {
        const defaultSummary = computeDefaultSummary(data.staged);
        const cancelEmpty = prev.emptyCommitMode && statusChanged;
        set({
          staged: data.staged,
          unstaged: data.unstaged,
          loading: false,
          initialized: true,
          defaultSummary,
          ...(cancelEmpty ? { emptyCommitMode: false } : {}),
        });
      }

      // Fetch stats in the background (non-blocking).
      Promise.all([
        commands.getDiffStats(repoPath, "Unstaged"),
        commands.getDiffStats(repoPath, "Staged"),
      ]).then(([unstagedResult, stagedResult]) => {
        const unstagedStats = new Map<string, FileStats>();
        const stagedStats = new Map<string, FileStats>();
        if (unstagedResult.status === "ok") {
          for (const s of unstagedResult.data) unstagedStats.set(s.path, s);
        }
        if (stagedResult.status === "ok") {
          for (const s of stagedResult.data) stagedStats.set(s.path, s);
        }
        set({ unstagedStats, stagedStats });
      });
    } else {
      const prev = get();
      if (prev.staged.length === 0 && prev.unstaged.length === 0 && prev.initialized) {
        return;
      }
      set({ staged: [], unstaged: [], loading: false, initialized: true, defaultSummary: "", stagedStats: new Map(), unstagedStats: new Map() });
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
      useAlertStore.getState().addAlert(
        extractErrorMessage(result.error, "Failed to commit"),
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
    const { staged, unstaged, defaultSummary: prevDefault, stagedStats, unstagedStats } = get();
    const entry = unstaged.find((e) => e.path === path);
    if (entry) {
      const newStaged = [...staged, entry];
      const newStagedStats = new Map(stagedStats);
      const newUnstagedStats = new Map(unstagedStats);
      const stat = newUnstagedStats.get(path);
      if (stat) {
        newStagedStats.set(path, stat);
        newUnstagedStats.delete(path);
      }
      set({
        staged: newStaged,
        unstaged: unstaged.filter((e) => e.path !== path),
        defaultSummary: computeDefaultSummary(newStaged),
        stagedStats: newStagedStats,
        unstagedStats: newUnstagedStats,
      });
    }
    const result = await commands.stageFiles(repoPath, [path]);
    if (result.status === "ok") {
      get().fetchStatus(repoPath);
    } else {
      // Rollback optimistic update on failure
      set({ staged, unstaged, defaultSummary: prevDefault, stagedStats, unstagedStats });
      useAlertStore.getState().addAlert(extractErrorMessage(result.error, "Failed to stage file"));
    }
  },

  unstageFile: async (repoPath: string, path: string) => {
    // Cancel empty commit mode on any staging operation
    if (get().emptyCommitMode) set({ emptyCommitMode: false });
    // Optimistic update: move file from staged → unstaged immediately
    const { staged, unstaged, defaultSummary: prevDefault, stagedStats, unstagedStats } = get();
    const entry = staged.find((e) => e.path === path);
    if (entry) {
      const newStaged = staged.filter((e) => e.path !== path);
      const newStagedStats = new Map(stagedStats);
      const newUnstagedStats = new Map(unstagedStats);
      const stat = newStagedStats.get(path);
      if (stat) {
        newUnstagedStats.set(path, stat);
        newStagedStats.delete(path);
      }
      set({
        staged: newStaged,
        unstaged: [...unstaged, entry],
        defaultSummary: computeDefaultSummary(newStaged),
        stagedStats: newStagedStats,
        unstagedStats: newUnstagedStats,
      });
    }
    const result = await commands.unstageFiles(repoPath, [path]);
    if (result.status === "ok") {
      get().fetchStatus(repoPath);
    } else {
      // Rollback optimistic update on failure
      set({ staged, unstaged, defaultSummary: prevDefault, stagedStats, unstagedStats });
      useAlertStore.getState().addAlert(extractErrorMessage(result.error, "Failed to unstage file"));
    }
  },

  stageAll: async (repoPath: string) => {
    // Cancel empty commit mode on any staging operation
    if (get().emptyCommitMode) set({ emptyCommitMode: false });
    const { staged, unstaged, defaultSummary: prevDefault, stagedStats, unstagedStats } = get();
    if (unstaged.length === 0) return;
    // Optimistic update: move all unstaged → staged
    const paths = unstaged.map((e) => e.path);
    const newStaged = [...staged, ...unstaged];
    const newStagedStats = new Map(stagedStats);
    const newUnstagedStats = new Map<string, FileStats>();
    for (const [k, v] of unstagedStats) newStagedStats.set(k, v);
    set({
      staged: newStaged,
      unstaged: [],
      defaultSummary: computeDefaultSummary(newStaged),
      stagedStats: newStagedStats,
      unstagedStats: newUnstagedStats,
    });
    const result = await commands.stageFiles(repoPath, paths);
    if (result.status === "ok") {
      get().fetchStatus(repoPath);
    } else {
      // Rollback optimistic update on failure
      set({ staged, unstaged, defaultSummary: prevDefault, stagedStats, unstagedStats });
      useAlertStore.getState().addAlert(extractErrorMessage(result.error, "Failed to stage files"));
    }
  },

  unstageAll: async (repoPath: string) => {
    // Cancel empty commit mode on any staging operation
    if (get().emptyCommitMode) set({ emptyCommitMode: false });
    const { staged, unstaged, defaultSummary: prevDefault, stagedStats, unstagedStats } = get();
    if (staged.length === 0) return;
    // Optimistic update: move all staged → unstaged
    const paths = staged.map((e) => e.path);
    const newUnstagedStats = new Map(unstagedStats);
    const newStagedStats = new Map<string, FileStats>();
    for (const [k, v] of stagedStats) newUnstagedStats.set(k, v);
    set({
      staged: [],
      unstaged: [...unstaged, ...staged],
      defaultSummary: "",
      stagedStats: newStagedStats,
      unstagedStats: newUnstagedStats,
    });
    const result = await commands.unstageFiles(repoPath, paths);
    if (result.status === "ok") {
      get().fetchStatus(repoPath);
    } else {
      // Rollback optimistic update on failure
      set({ staged, unstaged, defaultSummary: prevDefault, stagedStats, unstagedStats });
      useAlertStore.getState().addAlert(extractErrorMessage(result.error, "Failed to unstage files"));
    }
  },

  discardFile: async (repoPath: string, path: string, area: "Unstaged" | "Staged") => {
    const result = area === "Unstaged"
      ? await commands.discardUnstagedFiles(repoPath, [path])
      : await commands.discardStagedFiles(repoPath, [path]);
    if (result.status === "ok") {
      get().fetchStatus(repoPath);
    } else {
      useAlertStore.getState().addAlert(extractErrorMessage(result.error, "Failed to discard changes"));
    }
  },

  discardAll: async (repoPath: string, area: "Unstaged" | "Staged") => {
    const { staged, unstaged } = get();
    const paths = (area === "Unstaged" ? unstaged : staged).map((e) => e.path);
    if (paths.length === 0) return;
    const result = area === "Unstaged"
      ? await commands.discardUnstagedFiles(repoPath, paths)
      : await commands.discardStagedFiles(repoPath, paths);
    if (result.status === "ok") {
      get().fetchStatus(repoPath);
    } else {
      useAlertStore.getState().addAlert(extractErrorMessage(result.error, "Failed to discard changes"));
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
      stagedStats: new Map(),
      unstagedStats: new Map(),
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
        stagedStats: current.stagedStats,
        unstagedStats: current.unstagedStats,
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
        stagedStats: saved.stagedStats,
        unstagedStats: saved.unstagedStats,
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
        stagedStats: new Map(),
        unstagedStats: new Map(),
        loading: false,
        committing: false,
      });
    }
  },
}));
