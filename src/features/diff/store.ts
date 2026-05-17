import { create } from "zustand";
import { commands, type DiffArea, type FileDiff } from "../../ipc/bindings";
import { LruCache } from "../../shared/utils/lru-cache";

export type DiffViewMode = "unified" | "split";

interface DiffCacheEntry {
  fileContent: string | null;
  fileDiff: FileDiff | null;
}

const diffCache = new LruCache<string, DiffCacheEntry>(30);

/** Expose the cache so the prefetch hook can populate it. */
export function getDiffCache() {
  return diffCache;
}

interface SavedDiffSelection {
  selectedFile: string | null;
  selectedArea: DiffArea | null;
  fileContent: string | null;
  fileDiff: FileDiff | null;
}

/** Per-repo selection state cache. */
const repoSelections = new Map<string, SavedDiffSelection>();

interface DiffState {
  selectedFile: string | null;
  selectedArea: DiffArea | null;
  fileContent: string | null;
  fileDiff: FileDiff | null;
  viewMode: DiffViewMode;
  loading: boolean;
  selectFile: (
    repoPath: string,
    relativePath: string,
    area: DiffArea,
  ) => Promise<void>;
  /** Re-fetches diff for the currently selected file without clearing existing data. */
  refreshFile: (repoPath: string, relativePath: string, area: DiffArea) => Promise<void>;
  setViewMode: (mode: DiffViewMode) => void;
  clearSelection: () => void;
  /** Save current selection for `from` repo and restore selection for `to` repo. */
  switchRepo: (from: string | null, to: string) => void;
}

export const useDiffStore = create<DiffState>((set, get) => ({
  selectedFile: null,
  selectedArea: null,
  fileContent: null,
  fileDiff: null,
  viewMode: "unified",
  loading: false,

  selectFile: async (
    repoPath: string,
    relativePath: string,
    area: DiffArea,
  ) => {
    // Show cached data immediately if available.
    const cached = diffCache.get(`${area}:${relativePath}`);
    set({
      selectedFile: relativePath,
      selectedArea: area,
      fileContent: cached?.fileContent ?? null,
      fileDiff: cached?.fileDiff ?? null,
      loading: !cached,
    });

    const [contentResult, diffResult] = await Promise.all([
      commands.readFileContents(repoPath, relativePath),
      commands.getFileDiff(repoPath, relativePath, area),
    ]);

    const fileContent = contentResult.status === "ok" ? contentResult.data : null;
    const fileDiff = diffResult.status === "ok" ? diffResult.data : null;

    // Update cache.
    diffCache.set(`${area}:${relativePath}`, { fileContent, fileDiff });

    // Only apply if still viewing this file.
    const current = useDiffStore.getState();
    if (current.selectedFile === relativePath && current.selectedArea === area) {
      set({ fileContent, fileDiff, loading: false });
    }
  },

  refreshFile: async (
    repoPath: string,
    relativePath: string,
    area: DiffArea,
  ) => {
    // Keep existing fileContent/fileDiff visible while fetching.
    set({ selectedFile: relativePath, selectedArea: area });

    const [contentResult, diffResult] = await Promise.all([
      commands.readFileContents(repoPath, relativePath),
      commands.getFileDiff(repoPath, relativePath, area),
    ]);

    const fileContent = contentResult.status === "ok" ? contentResult.data : null;
    const fileDiff = diffResult.status === "ok" ? diffResult.data : null;

    // Update cache.
    diffCache.set(`${area}:${relativePath}`, { fileContent, fileDiff });

    // Only update if we're still looking at the same file.
    const current = useDiffStore.getState();
    if (current.selectedFile === relativePath && current.selectedArea === area) {
      set({ fileContent, fileDiff, loading: false });
    }
  },

  setViewMode: (mode: DiffViewMode) => set({ viewMode: mode }),

  clearSelection: () =>
    set({
      selectedFile: null,
      selectedArea: null,
      fileContent: null,
      fileDiff: null,
      loading: false,
    }),

  switchRepo: (from: string | null, to: string) => {
    const current = get();
    // Save current selection for the old repo.
    if (from) {
      repoSelections.set(from, {
        selectedFile: current.selectedFile,
        selectedArea: current.selectedArea,
        fileContent: current.fileContent,
        fileDiff: current.fileDiff,
      });
    }
    // Restore selection for the new repo.
    const saved = repoSelections.get(to);
    if (saved) {
      set({
        selectedFile: saved.selectedFile,
        selectedArea: saved.selectedArea,
        fileContent: saved.fileContent,
        fileDiff: saved.fileDiff,
        loading: false,
      });
    } else {
      set({
        selectedFile: null,
        selectedArea: null,
        fileContent: null,
        fileDiff: null,
        loading: false,
      });
    }
  },
}));
