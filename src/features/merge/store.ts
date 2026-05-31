import { create } from "zustand";
import { commands, type ConflictResolution, type FileConflictInfo } from "../../ipc/bindings";
import { useAlertStore } from "../../shared/stores/alerts";
import { useStagingStore } from "../staging";
import { extractErrorMessage } from "../../shared/utils/errors";

/** Resolution choice for a file — null means no choice yet. */
export type ResolutionChoice = ConflictResolution | null;

interface MergeState {
  /** Whether a merge/revert conflict is currently in progress. */
  merging: boolean;
  /** Whether the current conflict is from a revert (vs a merge). */
  isRevert: boolean;
  /** The branch being merged in, or the commit hash being reverted. */
  incomingBranch: string;
  /** Per-file conflict info (path + conflict count). */
  conflictFiles: FileConflictInfo[];
  /** User's chosen resolution per file path (null = no choice yet). */
  resolutions: Record<string, ResolutionChoice>;
  /** Files that have been resolved externally (no more conflict markers). */
  resolvedExternally: Set<string>;
  /** Whether an operation is in flight. */
  loading: boolean;

  /** Start a merge of the given branch into the current branch. */
  mergeBranch: (repoPath: string, branchName: string) => Promise<boolean>;
  /** Revert a commit, entering conflict resolution if needed. */
  revertCommit: (repoPath: string, hash: string) => Promise<boolean>;
  /** Enter conflict resolution mode for an already in-progress merge/revert. */
  enterConflictResolution: (repoPath: string, isRevert: boolean, incomingBranch: string) => Promise<void>;
  /** Abort the in-progress merge or revert. */
  abortMerge: (repoPath: string) => Promise<void>;
  /** Refresh the conflict list and counts. */
  refreshConflicts: (repoPath: string) => Promise<void>;
  /** Set a resolution choice for a file (does NOT apply it yet). */
  setResolution: (filePath: string, resolution: ResolutionChoice) => void;
  /** Apply all chosen resolutions and finalize the merge. */
  applyAndFinalize: (repoPath: string, message: string) => Promise<boolean>;
  /** Open a file in the external merge tool. */
  openInMergeTool: (repoPath: string, filePath: string, toolId: string) => Promise<void>;
  /** Clear the merge state. */
  clear: () => void;
}

export const useMergeStore = create<MergeState>((set, get) => ({
  merging: false,
  isRevert: false,
  incomingBranch: "",
  conflictFiles: [],
  resolutions: {},
  resolvedExternally: new Set(),
  loading: false,

  mergeBranch: async (repoPath: string, branchName: string) => {
    set({ loading: true });
    const result = await commands.mergeBranch(repoPath, branchName);
    set({ loading: false });

    if (result.status === "error") {
      useAlertStore
        .getState()
        .addAlert(extractErrorMessage(result.error, "Merge failed"));
      return false;
    }

    if (result.data === "AlreadyUpToDate") {
      useAlertStore.getState().addAlert("Already up to date — nothing to merge", "info");
      return true;
    }

    if ("Success" in result.data) {
      const commits_merged = result.data.Success!.commits_merged;
      useAlertStore
        .getState()
        .addAlert(
          `Merged ${commits_merged} commit${commits_merged === 1 ? "" : "s"} from ${branchName}`,
          "info",
        );
      return true;
    }

    // Conflicts detected — fetch counts
    const conflictInfo = result.data.Conflict;
    const countsResult = await commands.getConflictCounts(repoPath);
    const conflictFiles: FileConflictInfo[] =
      countsResult.status === "ok"
        ? countsResult.data
        : conflictInfo.conflicted_files.map((f) => ({ path: f, conflict_count: 0 }));

    const resolutions: Record<string, ResolutionChoice> = {};
    for (const f of conflictFiles) {
      resolutions[f.path] = null;
    }

    set({
      merging: true,
      isRevert: false,
      incomingBranch: conflictInfo.incoming_branch,
      conflictFiles,
      resolutions,
      resolvedExternally: new Set(),
    });
    return false;
  },

  revertCommit: async (repoPath: string, hash: string) => {
    set({ loading: true });
    const result = await commands.revertCommit(repoPath, hash);
    set({ loading: false });

    if (result.status === "error") {
      useAlertStore
        .getState()
        .addAlert(extractErrorMessage(result.error, "Revert failed"));
      return false;
    }

    if (result.data === "Success") {
      return true;
    }

    // Conflicts detected — fetch counts
    const conflictInfo = result.data.Conflict;
    const countsResult = await commands.getConflictCounts(repoPath);
    const conflictFiles: FileConflictInfo[] =
      countsResult.status === "ok"
        ? countsResult.data
        : conflictInfo.conflicted_files.map((f) => ({ path: f, conflict_count: 0 }));

    const resolutions: Record<string, ResolutionChoice> = {};
    for (const f of conflictFiles) {
      resolutions[f.path] = null;
    }

    set({
      merging: true,
      isRevert: true,
      incomingBranch: conflictInfo.incoming_branch,
      conflictFiles,
      resolutions,
      resolvedExternally: new Set(),
    });
    return false;
  },

  enterConflictResolution: async (repoPath: string, isRevert: boolean, incomingBranch: string) => {
    const countsResult = await commands.getConflictCounts(repoPath);
    const conflictFiles: FileConflictInfo[] =
      countsResult.status === "ok" ? countsResult.data : [];

    const resolutions: Record<string, ResolutionChoice> = {};
    for (const f of conflictFiles) {
      resolutions[f.path] = null;
    }

    set({
      merging: true,
      isRevert,
      incomingBranch,
      conflictFiles,
      resolutions,
      resolvedExternally: new Set(),
    });
  },

  abortMerge: async (repoPath: string) => {
    set({ loading: true });
    const result = get().isRevert
      ? await commands.revertAbort(repoPath)
      : await commands.mergeAbort(repoPath);
    set({ loading: false });

    if (result.status === "error") {
      useAlertStore
        .getState()
        .addAlert(extractErrorMessage(result.error, "Failed to abort merge"));
      return;
    }

    get().clear();
    useStagingStore.getState().fetchStatus(repoPath);
  },

  refreshConflicts: async (repoPath: string) => {
    if (!get().merging) return;

    const countsResult = await commands.getConflictCounts(repoPath);
    if (countsResult.status === "error") return;

    const newFiles = countsResult.data;
    const currentResolutions = get().resolutions;
    const prevFiles = get().conflictFiles;

    // Detect files that were previously conflicted but now aren't
    const newPaths = new Set(newFiles.map((f) => f.path));
    const resolvedExternally = new Set(get().resolvedExternally);
    for (const prev of prevFiles) {
      if (!newPaths.has(prev.path)) {
        resolvedExternally.add(prev.path);
      }
    }

    // Detect files where conflict count dropped to 0 (resolved externally in-place)
    for (const f of newFiles) {
      if (f.conflict_count === 0) {
        resolvedExternally.add(f.path);
      }
    }

    // Keep resolutions for existing files, add null for new ones
    const resolutions: Record<string, ResolutionChoice> = { ...currentResolutions };
    for (const f of newFiles) {
      if (!(f.path in resolutions)) {
        resolutions[f.path] = null;
      }
    }

    set({
      conflictFiles: newFiles.filter((f) => f.conflict_count > 0),
      resolutions,
      resolvedExternally,
    });
  },

  setResolution: (filePath: string, resolution: ResolutionChoice) => {
    set({
      resolutions: { ...get().resolutions, [filePath]: resolution },
    });
  },

  applyAndFinalize: async (repoPath: string, message: string) => {
    const { resolutions, resolvedExternally, conflictFiles } = get();
    set({ loading: true });

    // Apply resolutions for files that haven't been resolved externally
    for (const file of conflictFiles) {
      if (resolvedExternally.has(file.path)) continue;
      const choice = resolutions[file.path];
      if (!choice) continue;

      const result = await commands.resolveConflict(repoPath, file.path, choice);
      if (result.status === "error") {
        set({ loading: false });
        useAlertStore
          .getState()
          .addAlert(extractErrorMessage(result.error, `Failed to resolve ${file.path}`));
        return false;
      }
    }

    // Stage externally resolved files (user edited out markers but didn't git add)
    // Use AcceptBoth which will strip any remaining markers and git add the file
    for (const filePath of resolvedExternally) {
      const result = await commands.resolveConflict(repoPath, filePath, "AcceptBoth");
      if (result.status === "error") {
        // Non-fatal: file might already be staged
      }
    }

    // Finalize the merge or revert
    if (get().isRevert) {
      const result = await commands.revertContinue(repoPath);
      set({ loading: false });
      if (result.status === "error") {
        useAlertStore
          .getState()
          .addAlert(extractErrorMessage(result.error, "Failed to complete revert"));
        return false;
      }
      useAlertStore.getState().addAlert("Revert completed", "info");
    } else {
      const incomingBranch = get().incomingBranch;
      const finalMessage = message.trim() || `Merge branch '${incomingBranch}'`;
      const result = await commands.mergeContinue(repoPath, finalMessage);
      set({ loading: false });
      if (result.status === "error") {
        useAlertStore
          .getState()
          .addAlert(extractErrorMessage(result.error, "Failed to complete merge"));
        return false;
      }
      const commitsMerged = result.data;
      useAlertStore
        .getState()
        .addAlert(
          `Merged ${commitsMerged} commit${commitsMerged === 1 ? "" : "s"} from ${incomingBranch}`,
          "info",
        );
    }

    get().clear();
    useStagingStore.getState().fetchStatus(repoPath);
    return true;
  },

  openInMergeTool: async (repoPath: string, filePath: string, toolId: string) => {
    const result = await commands.openInMergeTool(repoPath, filePath, toolId);
    if (result.status === "error") {
      useAlertStore
        .getState()
        .addAlert(extractErrorMessage(result.error, "Failed to open merge tool"));
    }
  },

  clear: () => {
    set({
      merging: false,
      isRevert: false,
      incomingBranch: "",
      conflictFiles: [],
      resolutions: {},
      resolvedExternally: new Set(),
      loading: false,
    });
  },
}));
