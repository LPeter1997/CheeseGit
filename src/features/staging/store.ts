import { create } from "zustand";
import { commands, type StatusEntry, type FileStats } from "../../ipc/bindings";
import { useAlertStore } from "../../shared/stores/alerts";
import { extractErrorMessage } from "../../shared/utils/errors";
import { getDiffCache } from "../diff/store";

// ── Per-repo state cache ──────────────────────────────────────────────

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

const repoStaging = new Map<string, SavedStagingState>();

// ── Helpers ──────────────────────────────────────────────────────────

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

// ── Fetch coordination ───────────────────────────────────────────────
//
// Two counters work together to prevent stale data from reaching the UI:
//
//   fetchSeq — incremented at the start of every fetchStatus call.
//              Only the result from the HIGHEST seq is applied.
//              This handles: rapid polls, multiple fetchStatus calls in flight.
//
//   mutationSeq — incremented before every optimistic update.
//                 If mutationSeq changed during a fetchStatus await,
//                 that fetch is stale (it reflects pre-mutation state)
//                 and its result is silently discarded. The mutation's
//                 own post-success fetchStatus will provide fresh data.
//
// Together these guarantee:
// • A poll's fetch NEVER overwrites an optimistic update.
// • Two rapid fetchStatus calls don't fight — the latest one wins.
// • No fragile "generation" checks scattered across individual methods.

let fetchSeq = 0;
let mutationSeq = 0;

// ── Diff cache invalidation ──────────────────────────────────────────
//
// When files move between staged/unstaged, the old area's cache entry is
// stale (its diff no longer reflects reality). Evicting it ensures the next
// selectFile does a fresh fetch instead of showing outdated data.

function invalidateDiffCache(paths: string[], area: "Unstaged" | "Staged") {
  const cache = getDiffCache();
  for (const p of paths) {
    cache.delete(`${area}:${p}`);
  }
}

// ── Store interface ──────────────────────────────────────────────────

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
  switchRepo: (from: string | null, to: string) => void;
}

// ── Store implementation ─────────────────────────────────────────────

interface MutateOptions {
  /** Apply the optimistic state change. Returns a rollback function. */
  optimistic: () => () => void;
  /** Diff-cache entries to invalidate (by area). */
  invalidate?: { paths: string[]; area: "Unstaged" | "Staged" };
  /** The backend IPC call. */
  execute: () => Promise<{ status: string; error?: unknown }>;
  /** Human-readable error message for the alert banner. */
  errorMessage: string;
  /** Repo path for the reconciliation fetch. */
  repoPath: string;
}

// ─── The universal mutation helper ───────────────────────────────
//
// ALL file-level mutations follow a single flow:
//
//  1. Bump mutationSeq (invalidates any in-flight poll fetch)
//  2. Apply optimistic state update (immediate, synchronous)
//  3. Invalidate stale diff-cache entries
//  4. Await the backend IPC command
//  5. On success → reconcile via fetchStatus
//  6. On failure → rollback optimistic state + show alert
//
// By routing every mutation through this helper, we guarantee:
// • Consistent stale-data protection
// • Automatic cache invalidation
// • Uniform error handling and rollback

async function mutate(opts: MutateOptions) {
  // 1. Mark mutation — any in-flight fetchStatus results become stale.
  mutationSeq++;

  // 2. Optimistic update (returns rollback closure).
  const rollback = opts.optimistic();

  // 3. Invalidate stale cache entries.
  if (opts.invalidate) {
    invalidateDiffCache(opts.invalidate.paths, opts.invalidate.area);
  }

  // 4. Backend call.
  const result = await opts.execute();

  if (result.status === "ok") {
    // 5. Reconcile with real git state.
    useStagingStore.getState().fetchStatus(opts.repoPath);
  } else {
    // 6. Rollback + alert.
    rollback();
    useAlertStore.getState().addAlert(
      extractErrorMessage(result.error as any, opts.errorMessage),
    );
  }
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

    // ─── fetchStatus ─────────────────────────────────────────────
    //
    // Single source of truth reconciliation. Called by:
    // • Polling (every 3s)
    // • After every successful mutation (via the mutate helper)
    //
    // Guards:
    // • fetchSeq — only the latest call applies its result
    // • mutationSeq — discards if a mutation happened mid-flight
    // • Fingerprint — no-op when data is semantically unchanged
    // • Error resilience — transient errors don't wipe valid state

    fetchStatus: async (repoPath: string) => {
      if (!get().initialized) {
        set({ loading: true });
      }

      const myFetchSeq = ++fetchSeq;
      const mutSeqAtStart = mutationSeq;

      const result = await commands.getStatus(repoPath);

      // Stale check 1: a newer fetchStatus was started while we awaited.
      if (myFetchSeq !== fetchSeq) return;

      // Stale check 2: a mutation happened while we awaited — our data
      // predates the mutation and would revert the optimistic update.
      if (mutSeqAtStart !== mutationSeq) {
        if (!get().initialized) set({ loading: false, initialized: true });
        return;
      }

      if (result.status === "ok") {
        const data = result.data;
        const prev = get();
        const statusChanged =
          statusFingerprint(prev.staged) !== statusFingerprint(data.staged) ||
          statusFingerprint(prev.unstaged) !== statusFingerprint(data.unstaged);

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
        } else if (prev.loading) {
          set({ loading: false });
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
        // Transient error (e.g., git index.lock contention).
        // If we already have data, silently ignore — next poll will retry.
        // Only clear state on the very first fetch (nothing to preserve).
        const prev = get();
        if (!prev.initialized) {
          set({ staged: [], unstaged: [], loading: false, initialized: true, defaultSummary: "", stagedStats: new Map(), unstagedStats: new Map() });
        } else {
          set({ loading: false });
        }
      }
    },

    setSummary: (summary: string) => set({ summary }),
    setDescription: (description: string) => set({ description }),

    // ─── commit ──────────────────────────────────────────────────

    commit: async (repoPath: string): Promise<boolean> => {
      const { summary, defaultSummary, description, emptyCommitMode, staged } = get();
      const msg = summary || defaultSummary;
      if (!msg) return false;
      if (!emptyCommitMode && staged.length === 0) return false;

      set({ committing: true });
      mutationSeq++;
      const result = await commands.commit(repoPath, msg, description, emptyCommitMode);
      set({ committing: false });

      if (result.status === "error") {
        useAlertStore.getState().addAlert(
          extractErrorMessage(result.error, "Failed to commit"),
        );
        return false;
      }

      set({ summary: "", description: "", defaultSummary: "", emptyCommitMode: false });
      get().fetchStatus(repoPath);
      return true;
    },

    // ─── stageFile ───────────────────────────────────────────────

    stageFile: async (repoPath: string, path: string) => {
      if (get().emptyCommitMode) set({ emptyCommitMode: false });

      const { staged, unstaged, defaultSummary: prevDefault, stagedStats, unstagedStats } = get();
      const entry = unstaged.find((e) => e.path === path);

      await mutate({
        optimistic: () => {
          if (entry) {
            const newStaged = [...staged, entry];
            const newStagedStats = new Map(stagedStats);
            const newUnstagedStats = new Map(unstagedStats);
            const stat = newUnstagedStats.get(path);
            if (stat) { newStagedStats.set(path, stat); newUnstagedStats.delete(path); }
            set({
              staged: newStaged,
              unstaged: unstaged.filter((e) => e.path !== path),
              defaultSummary: computeDefaultSummary(newStaged),
              stagedStats: newStagedStats,
              unstagedStats: newUnstagedStats,
            });
          }
          return () => set({ staged, unstaged, defaultSummary: prevDefault, stagedStats, unstagedStats });
        },
        invalidate: { paths: [path], area: "Unstaged" },
        execute: () => commands.stageFiles(repoPath, [path]),
        errorMessage: "Failed to stage file",
        repoPath,
      });
    },

    // ─── unstageFile ─────────────────────────────────────────────

    unstageFile: async (repoPath: string, path: string) => {
      if (get().emptyCommitMode) set({ emptyCommitMode: false });

      const { staged, unstaged, defaultSummary: prevDefault, stagedStats, unstagedStats } = get();
      const entry = staged.find((e) => e.path === path);

      await mutate({
        optimistic: () => {
          if (entry) {
            const newStaged = staged.filter((e) => e.path !== path);
            const newStagedStats = new Map(stagedStats);
            const newUnstagedStats = new Map(unstagedStats);
            const stat = newStagedStats.get(path);
            if (stat) { newUnstagedStats.set(path, stat); newStagedStats.delete(path); }
            set({
              staged: newStaged,
              unstaged: [...unstaged, entry],
              defaultSummary: computeDefaultSummary(newStaged),
              stagedStats: newStagedStats,
              unstagedStats: newUnstagedStats,
            });
          }
          return () => set({ staged, unstaged, defaultSummary: prevDefault, stagedStats, unstagedStats });
        },
        invalidate: { paths: [path], area: "Staged" },
        execute: () => commands.unstageFiles(repoPath, [path]),
        errorMessage: "Failed to unstage file",
        repoPath,
      });
    },

    // ─── stageAll ────────────────────────────────────────────────

    stageAll: async (repoPath: string) => {
      if (get().emptyCommitMode) set({ emptyCommitMode: false });
      const { staged, unstaged, defaultSummary: prevDefault, stagedStats, unstagedStats } = get();
      if (unstaged.length === 0) return;

      const paths = unstaged.map((e) => e.path);

      await mutate({
        optimistic: () => {
          const newStaged = [...staged, ...unstaged];
          const newStagedStats = new Map(stagedStats);
          for (const [k, v] of unstagedStats) newStagedStats.set(k, v);
          set({
            staged: newStaged,
            unstaged: [],
            defaultSummary: computeDefaultSummary(newStaged),
            stagedStats: newStagedStats,
            unstagedStats: new Map(),
          });
          return () => set({ staged, unstaged, defaultSummary: prevDefault, stagedStats, unstagedStats });
        },
        invalidate: { paths, area: "Unstaged" },
        execute: () => commands.stageFiles(repoPath, paths),
        errorMessage: "Failed to stage files",
        repoPath,
      });
    },

    // ─── unstageAll ──────────────────────────────────────────────

    unstageAll: async (repoPath: string) => {
      if (get().emptyCommitMode) set({ emptyCommitMode: false });
      const { staged, unstaged, defaultSummary: prevDefault, stagedStats, unstagedStats } = get();
      if (staged.length === 0) return;

      const paths = staged.map((e) => e.path);

      await mutate({
        optimistic: () => {
          const newUnstagedStats = new Map(unstagedStats);
          for (const [k, v] of stagedStats) newUnstagedStats.set(k, v);
          set({
            staged: [],
            unstaged: [...unstaged, ...staged],
            defaultSummary: "",
            stagedStats: new Map(),
            unstagedStats: newUnstagedStats,
          });
          return () => set({ staged, unstaged, defaultSummary: prevDefault, stagedStats, unstagedStats });
        },
        invalidate: { paths, area: "Staged" },
        execute: () => commands.unstageFiles(repoPath, paths),
        errorMessage: "Failed to unstage files",
        repoPath,
      });
    },

    // ─── discardFile ─────────────────────────────────────────────

    discardFile: async (repoPath: string, path: string, area: "Unstaged" | "Staged") => {
      mutationSeq++;
      invalidateDiffCache([path], area);

      const result = area === "Unstaged"
        ? await commands.discardUnstagedFiles(repoPath, [path])
        : await commands.discardStagedFiles(repoPath, [path]);

      if (result.status === "ok") {
        get().fetchStatus(repoPath);
      } else {
        useAlertStore.getState().addAlert(extractErrorMessage(result.error, "Failed to discard changes"));
      }
    },

    // ─── discardAll ──────────────────────────────────────────────

    discardAll: async (repoPath: string, area: "Unstaged" | "Staged") => {
      const { staged, unstaged } = get();
      const paths = (area === "Unstaged" ? unstaged : staged).map((e) => e.path);
      if (paths.length === 0) return;

      mutationSeq++;
      invalidateDiffCache(paths, area);

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
