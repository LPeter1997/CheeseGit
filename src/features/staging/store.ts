import { create } from "zustand";
import { commands, type StatusEntry, type FileStats } from "../../ipc/bindings";
import { useAlertStore } from "../../shared/stores/alerts";
import { extractErrorMessage } from "../../shared/utils/errors";
import { PerRepoStateCache } from "../../shared/utils/per-repo-state-cache";
import { RequestSequencer } from "../../shared/utils/request-sequencer";
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

const repoStaging = new PerRepoStateCache<SavedStagingState>();

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

const sequencer = new RequestSequencer();

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

function uniquePaths(paths: string[]): string[] {
  return Array.from(new Set(paths));
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
  undoLastCommit: (repoPath: string) => Promise<boolean>;
  stashStaged: (repoPath: string) => Promise<boolean>;
  stageFile: (repoPath: string, path: string) => Promise<void>;
  unstageFile: (repoPath: string, path: string) => Promise<void>;
  stageFiles: (repoPath: string, paths: string[]) => Promise<void>;
  unstageFiles: (repoPath: string, paths: string[]) => Promise<void>;
  stageAll: (repoPath: string) => Promise<void>;
  unstageAll: (repoPath: string) => Promise<void>;
  discardFile: (repoPath: string, path: string, area: "Unstaged" | "Staged") => Promise<void>;
  discardFiles: (repoPath: string, paths: string[], area: "Unstaged" | "Staged") => Promise<void>;
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
  sequencer.markMutation();

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

// ─── Transfer helper for stage/unstage ──────────────────────────────
//
// Consolidates the repeated pattern of moving files between staged/unstaged:
// • Extract entries from source collection
// • Transfer stats between stat maps
// • Compute new defaultSummary
// • Execute via mutate() with rollback support
//
// Usage: transferFiles(repoPath, paths, "stage", "Unstaged", "Staged", ...)

interface TransferOptions {
  repoPath: string;
  paths: string[];
  direction: "stage" | "unstage"; // "stage" = unstaged→staged, "unstage" = staged→unstaged
  execute: () => Promise<{ status: string; error?: unknown }>;
  errorMessage: string;
}

async function transferFiles(opts: TransferOptions) {
  if (opts.paths.length === 0) return;

  const state = useStagingStore.getState();
  const normalized = uniquePaths(opts.paths);
  
  const isStaging = opts.direction === "stage";
  const sourceCollection = isStaging ? state.unstaged : state.staged;
  const destCollection = isStaging ? state.staged : state.unstaged;
  const sourceStat = isStaging ? state.unstagedStats : state.stagedStats;
  const destStat = isStaging ? state.stagedStats : state.unstagedStats;
  const invalidateArea: "Unstaged" | "Staged" = isStaging ? "Unstaged" : "Staged";

  const entryMap = new Map(sourceCollection.map((e) => [e.path, e] as const));
  const selectedEntries = normalized
    .map((path) => entryMap.get(path))
    .filter((entry): entry is StatusEntry => !!entry);
  if (selectedEntries.length === 0) return;

  const selectedSet = new Set(selectedEntries.map((e) => e.path));
  const selectedPaths = Array.from(selectedSet);
  const prevDefaultSummary = state.defaultSummary;

  await mutate({
    optimistic: () => {
      const newSource = sourceCollection.filter((e) => !selectedSet.has(e.path));
      const newDest = [...destCollection, ...selectedEntries];
      const newSourceStat = new Map(sourceStat);
      const newDestStat = new Map(destStat);

      for (const path of selectedPaths) {
        const stat = newSourceStat.get(path);
        if (stat) {
          newDestStat.set(path, stat);
          newSourceStat.delete(path);
        }
      }

      const updatedStaged = isStaging ? newDest : newSource;
      const updatedUnstaged = isStaging ? newSource : newDest;

      useStagingStore.setState({
        staged: updatedStaged,
        unstaged: updatedUnstaged,
        defaultSummary: computeDefaultSummary(updatedStaged),
        stagedStats: isStaging ? newDestStat : newSourceStat,
        unstagedStats: isStaging ? newSourceStat : newDestStat,
      });

      // Rollback closure
      return () => {
        useStagingStore.setState({
          staged: state.staged,
          unstaged: state.unstaged,
          defaultSummary: prevDefaultSummary,
          stagedStats: state.stagedStats,
          unstagedStats: state.unstagedStats,
        });
      };
    },
    invalidate: { paths: selectedPaths, area: invalidateArea },
    execute: opts.execute,
    errorMessage: opts.errorMessage,
    repoPath: opts.repoPath,
  });
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

      const myFetchSeq = sequencer.nextRequest();
      const mutSeqAtStart = sequencer.snapshotMutation();

      const result = await commands.getStatus(repoPath);

      // Stale check 1: a newer fetchStatus was started while we awaited.
      if (!sequencer.isLatest(myFetchSeq)) return;

      // Stale check 2: a mutation happened while we awaited — our data
      // predates the mutation and would revert the optimistic update.
      if (!sequencer.isMutationUnchanged(mutSeqAtStart)) {
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
      sequencer.markMutation();
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

    // ─── undoLastCommit ────────────────────────────────────────

    undoLastCommit: async (repoPath: string): Promise<boolean> => {
      set({ committing: true });
      sequencer.markMutation();
      const result = await commands.undoLastCommit(repoPath);
      set({ committing: false });

      if (result.status === "error") {
        useAlertStore.getState().addAlert(
          extractErrorMessage(result.error, "Failed to undo latest commit"),
        );
        return false;
      }

      set({
        summary: result.data.summary,
        description: result.data.description,
        defaultSummary: "",
        emptyCommitMode: false,
      });
      get().fetchStatus(repoPath);
      return true;
    },

    // ─── stashStaged ─────────────────────────────────────────────

    stashStaged: async (repoPath: string): Promise<boolean> => {
      const { summary, defaultSummary, staged } = get();
      const msg = summary || defaultSummary;
      if (!msg) return false;
      if (staged.length === 0) return false;

      set({ committing: true });
      sequencer.markMutation();
      const result = await commands.stashStaged(repoPath, msg);
      set({ committing: false });

      if (result.status === "error") {
        useAlertStore.getState().addAlert(
          extractErrorMessage(result.error, "Failed to stash"),
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
      await transferFiles({
        repoPath,
        paths: [path],
        direction: "stage",
        execute: () => commands.stageFiles(repoPath, [path]),
        errorMessage: "Failed to stage file",
      });
    },

    // ─── stageFiles ──────────────────────────────────────────────

    stageFiles: async (repoPath: string, paths: string[]) => {
      if (get().emptyCommitMode) set({ emptyCommitMode: false });
      await transferFiles({
        repoPath,
        paths,
        direction: "stage",
        execute: () => commands.stageFiles(repoPath, uniquePaths(paths)),
        errorMessage: "Failed to stage files",
      });
    },

    // ─── unstageFile ─────────────────────────────────────────────

    unstageFile: async (repoPath: string, path: string) => {
      if (get().emptyCommitMode) set({ emptyCommitMode: false });
      await transferFiles({
        repoPath,
        paths: [path],
        direction: "unstage",
        execute: () => commands.unstageFiles(repoPath, [path]),
        errorMessage: "Failed to unstage file",
      });
    },

    // ─── unstageFiles ────────────────────────────────────────────

    unstageFiles: async (repoPath: string, paths: string[]) => {
      if (get().emptyCommitMode) set({ emptyCommitMode: false });
      await transferFiles({
        repoPath,
        paths,
        direction: "unstage",
        execute: () => commands.unstageFiles(repoPath, uniquePaths(paths)),
        errorMessage: "Failed to unstage files",
      });
    },

    // ─── stageAll ────────────────────────────────────────────────

    stageAll: async (repoPath: string) => {
      const { unstaged } = get();
      await get().stageFiles(repoPath, unstaged.map((entry) => entry.path));
    },

    // ─── unstageAll ──────────────────────────────────────────────

    unstageAll: async (repoPath: string) => {
      const { staged } = get();
      await get().unstageFiles(repoPath, staged.map((entry) => entry.path));
    },

    // ─── discardFile ─────────────────────────────────────────────

    discardFile: async (repoPath: string, path: string, area: "Unstaged" | "Staged") => {
      sequencer.markMutation();
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

    // ─── discardFiles ────────────────────────────────────────────

    discardFiles: async (repoPath: string, paths: string[], area: "Unstaged" | "Staged") => {
      const normalized = uniquePaths(paths);
      if (normalized.length === 0) return;

      sequencer.markMutation();
      invalidateDiffCache(normalized, area);

      const result = area === "Unstaged"
        ? await commands.discardUnstagedFiles(repoPath, normalized)
        : await commands.discardStagedFiles(repoPath, normalized);

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
      await get().discardFiles(repoPath, paths, area);
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
      repoStaging.save(from, {
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
      const saved = repoStaging.load(to);
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
