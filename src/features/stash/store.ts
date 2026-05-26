import { create } from "zustand";
import { commands, type StashEntry, type StatusEntry, type FileDiff, type FileStats } from "../../ipc/bindings";
import { useAlertStore } from "../../shared/stores/alerts";
import { extractErrorMessage } from "../../shared/utils/errors";
import { LruCache } from "../../shared/utils/lru-cache";

// ── Per-repo state cache ──────────────────────────────────────────────

interface SavedStashState {
  stashes: StashEntry[];
  initialized: boolean;
  selectedIndex: number | null;
  stashFiles: StatusEntry[];
  selectedFilePath: string | null;
}

const repoStash = new Map<string, SavedStashState>();

// ── Diff cache ────────────────────────────────────────────────────────

interface StashDiffCacheEntry {
  fileDiff: FileDiff | null;
  fileContent: string | null;
}

const stashDiffCache = new LruCache<string, StashDiffCacheEntry>(10);

// ── Store interface ──────────────────────────────────────────────────

interface StashState {
  stashes: StashEntry[];
  loading: boolean;
  initialized: boolean;
  selectedIndex: number | null;
  stashFiles: StatusEntry[];
  stashFilesLoading: boolean;
  stashFileStats: Map<string, FileStats>;
  selectedFilePath: string | null;
  selectedFileDiff: FileDiff | null;
  selectedFileContent: string | null;
  selectedFileDiffLoading: boolean;
  fetchStashes: (repoPath: string) => Promise<void>;
  selectStash: (index: number, repoPath: string) => Promise<void>;
  selectStashFile: (filePath: string, repoPath: string) => Promise<void>;
  applyStash: (repoPath: string, index: number) => Promise<boolean>;
  popStash: (repoPath: string, index: number) => Promise<boolean>;
  dropStash: (repoPath: string, index: number) => Promise<boolean>;
  clearSelection: () => void;
  switchRepo: (from: string | null, to: string) => void;
}

// ── Fetch coordination ───────────────────────────────────────────────

let fetchSeq = 0;

// ── Store implementation ─────────────────────────────────────────────

export const useStashStore = create<StashState>((set, get) => ({
  stashes: [],
  loading: false,
  initialized: false,
  selectedIndex: null,
  stashFiles: [],
  stashFilesLoading: false,
  stashFileStats: new Map(),
  selectedFilePath: null,
  selectedFileDiff: null,
  selectedFileContent: null,
  selectedFileDiffLoading: false,

  fetchStashes: async (repoPath: string) => {
    if (!get().initialized) {
      set({ loading: true });
    }

    const mySeq = ++fetchSeq;
    const result = await commands.listStashes(repoPath);

    if (mySeq !== fetchSeq) return;

    if (result.status === "ok") {
      const prev = get();
      const changed =
        !prev.initialized ||
        prev.stashes.length !== result.data.length ||
        prev.stashes.some((s, i) => s.hash !== result.data[i]?.hash);

      if (changed) {
        // If the selected stash no longer exists, clear selection.
        const selectedIndex = prev.selectedIndex;
        const stillExists = selectedIndex !== null && result.data.some((s) => s.index === selectedIndex);
        set({
          stashes: result.data,
          loading: false,
          initialized: true,
          ...(stillExists ? {} : { selectedIndex: null, stashFiles: [], selectedFilePath: null, selectedFileDiff: null, selectedFileContent: null, stashFileStats: new Map() }),
        });
      } else if (prev.loading) {
        set({ loading: false });
      }
    } else {
      const prev = get();
      if (!prev.initialized) {
        set({ stashes: [], loading: false, initialized: true });
      } else {
        set({ loading: false });
      }
    }
  },

  selectStash: async (index: number, repoPath: string) => {
    set({
      selectedIndex: index,
      stashFiles: [],
      stashFilesLoading: true,
      selectedFilePath: null,
      selectedFileDiff: null,
      selectedFileContent: null,
      stashFileStats: new Map(),
    });
    stashDiffCache.clear();

    const [filesResult, statsResult] = await Promise.all([
      commands.listStashFiles(repoPath, index),
      commands.stashFileStats(repoPath, index),
    ]);

    if (get().selectedIndex !== index) return;

    const files = filesResult.status === "ok" ? filesResult.data : [];
    const statsMap = new Map<string, FileStats>();
    if (statsResult.status === "ok") {
      for (const s of statsResult.data) statsMap.set(s.path, s);
    }

    set({
      stashFiles: files,
      stashFilesLoading: false,
      stashFileStats: statsMap,
    });
  },

  selectStashFile: async (filePath: string, repoPath: string) => {
    const { selectedIndex } = get();
    if (selectedIndex === null) return;

    const cacheKey = `${selectedIndex}:${filePath}`;
    const cached = stashDiffCache.get(cacheKey);
    if (cached) {
      set({
        selectedFilePath: filePath,
        selectedFileDiff: cached.fileDiff,
        selectedFileContent: cached.fileContent,
        selectedFileDiffLoading: false,
      });
      return;
    }

    set({
      selectedFilePath: filePath,
      selectedFileDiff: null,
      selectedFileContent: null,
      selectedFileDiffLoading: true,
    });

    const [diffResult, contentResult] = await Promise.all([
      commands.diffStashFile(repoPath, selectedIndex, filePath),
      commands.showFileAtStash(repoPath, selectedIndex, filePath),
    ]);

    if (get().selectedFilePath !== filePath || get().selectedIndex !== selectedIndex) return;

    const fileDiff = diffResult.status === "ok" ? diffResult.data : null;
    const fileContent = contentResult.status === "ok" ? contentResult.data : null;

    stashDiffCache.set(cacheKey, { fileDiff, fileContent });

    set({
      selectedFileDiff: fileDiff,
      selectedFileContent: fileContent,
      selectedFileDiffLoading: false,
    });
  },

  applyStash: async (repoPath: string, index: number) => {
    const result = await commands.stashApply(repoPath, index);
    if (result.status === "error") {
      useAlertStore.getState().addAlert(
        extractErrorMessage(result.error, "Failed to apply stash"),
      );
      return false;
    }
    return true;
  },

  popStash: async (repoPath: string, index: number) => {
    const result = await commands.stashPop(repoPath, index);
    if (result.status === "error") {
      useAlertStore.getState().addAlert(
        extractErrorMessage(result.error, "Failed to pop stash"),
      );
      return false;
    }
    get().fetchStashes(repoPath);
    return true;
  },

  dropStash: async (repoPath: string, index: number) => {
    const result = await commands.stashDrop(repoPath, index);
    if (result.status === "error") {
      useAlertStore.getState().addAlert(
        extractErrorMessage(result.error, "Failed to drop stash"),
      );
      return false;
    }
    get().fetchStashes(repoPath);
    return true;
  },

  clearSelection: () =>
    set({
      selectedIndex: null,
      stashFiles: [],
      selectedFilePath: null,
      selectedFileDiff: null,
      selectedFileContent: null,
      stashFileStats: new Map(),
    }),

  switchRepo: (from: string | null, to: string) => {
    const current = get();
    if (from) {
      repoStash.set(from, {
        stashes: current.stashes,
        initialized: current.initialized,
        selectedIndex: current.selectedIndex,
        stashFiles: current.stashFiles,
        selectedFilePath: current.selectedFilePath,
      });
    }
    const saved = repoStash.get(to);
    if (saved) {
      set({
        stashes: saved.stashes,
        initialized: saved.initialized,
        selectedIndex: saved.selectedIndex,
        stashFiles: saved.stashFiles,
        selectedFilePath: saved.selectedFilePath,
        selectedFileDiff: null,
        selectedFileContent: null,
        loading: false,
        stashFilesLoading: false,
        selectedFileDiffLoading: false,
        stashFileStats: new Map(),
      });
    } else {
      set({
        stashes: [],
        initialized: false,
        selectedIndex: null,
        stashFiles: [],
        selectedFilePath: null,
        selectedFileDiff: null,
        selectedFileContent: null,
        loading: false,
        stashFilesLoading: false,
        selectedFileDiffLoading: false,
        stashFileStats: new Map(),
      });
    }
    stashDiffCache.clear();
  },
}));
