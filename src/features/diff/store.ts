import { create } from "zustand";
import { commands, type DiffArea, type FileDiff } from "../../ipc/bindings";
import { LruCache } from "../../shared/utils/lru-cache";

export type DiffViewMode = "unified" | "split";

/**
 * Lightweight fingerprint for a FileDiff to detect whether content has
 * actually changed. Used by refreshFile to skip no-op state updates
 * that would reset the scroll position.
 */
function diffFingerprint(diff: FileDiff | null): string {
  if (!diff) return "";
  let fp = "";
  for (const hunk of diff.hunks) {
    fp += hunk.header;
    fp += "\x00";
    for (const line of hunk.lines) {
      fp += line.kind;
      fp += line.content;
      fp += "\x00";
    }
  }
  return fp;
}

// ── Diff cache ───────────────────────────────────────────────────────
//
// Caches file-content + diff pairs keyed by "Area:path".
// Populated by:
//   • selectFile (on-demand, when user clicks a file)
//   • useDiffPrefetch (background, after file lists change)
//
// Invalidated by:
//   • Staging store mutations (files change area → old entry is stale)
//
// Rules:
//   • Never cache entries where BOTH fileContent and fileDiff are null
//     (these represent transient failures, not real data).
//   • A cache "hit" only counts if it has useful content.

interface DiffCacheEntry {
  fileContent: string | null;
  fileDiff: FileDiff | null;
}

const diffCache = new LruCache<string, DiffCacheEntry>(30);

/** Expose the cache for the prefetch hook and staging store's invalidation. */
export function getDiffCache() {
  return diffCache;
}

// ── Per-repo selection state ─────────────────────────────────────────

interface SavedDiffSelection {
  selectedFile: string | null;
  selectedArea: DiffArea | null;
  fileContent: string | null;
  fileDiff: FileDiff | null;
}

const repoSelections = new Map<string, SavedDiffSelection>();

// ── Request sequencing ───────────────────────────────────────────────
//
// selectFile is async. If the user clicks rapidly between files, we must
// ensure only the LATEST request applies its result. A simple incrementing
// counter (selectSeq) tracks this: each call captures the current seq,
// and only applies its fetch result if still the latest.

let selectSeq = 0;

// ── Store ────────────────────────────────────────────────────────────

interface DiffState {
  selectedFile: string | null;
  selectedArea: DiffArea | null;
  fileContent: string | null;
  fileDiff: FileDiff | null;
  viewMode: DiffViewMode;
  loading: boolean;
  selectFile: (repoPath: string, relativePath: string, area: DiffArea) => Promise<void>;
  refreshFile: (repoPath: string, relativePath: string, area: DiffArea) => Promise<void>;
  setViewMode: (mode: DiffViewMode) => void;
  clearSelection: () => void;
  switchRepo: (from: string | null, to: string) => void;
}

export const useDiffStore = create<DiffState>((set, get) => ({
  selectedFile: null,
  selectedArea: null,
  fileContent: null,
  fileDiff: null,
  viewMode: "unified",
  loading: false,

  // ─── selectFile ──────────────────────────────────────────────────
  //
  // Called when the user clicks a file in the staging panel.
  //
  // Flow:
  //  1. If already viewing this exact file+area, no-op.
  //  2. Check cache — show cached data instantly (avoids flicker).
  //  3. Fetch fresh data from backend.
  //  4. Apply only if this is still the latest request (selectSeq guard).
  //  5. Cache the result (only if it contains useful data).

  selectFile: async (repoPath: string, relativePath: string, area: DiffArea) => {
    const current = get();
    // Only skip if we already have this file displayed with real content.
    // If content is null (failed previous fetch), allow re-clicking to retry.
    if (
      current.selectedFile === relativePath &&
      current.selectedArea === area &&
      !current.loading &&
      (current.fileContent !== null || current.fileDiff !== null)
    ) {
      return;
    }

    const mySeq = ++selectSeq;

    // Check cache — only treat as a hit if it has real content.
    const cached = diffCache.get(`${area}:${relativePath}`);
    const cacheHit = cached && (cached.fileContent !== null || cached.fileDiff !== null);

    set({
      selectedFile: relativePath,
      selectedArea: area,
      fileContent: cacheHit ? cached.fileContent : null,
      fileDiff: cacheHit ? cached.fileDiff : null,
      loading: !cacheHit,
    });

    // Fetch fresh data regardless of cache (cache may be stale).
    let contentResult, diffResult;
    try {
      [contentResult, diffResult] = await Promise.all([
        commands.readFileContents(repoPath, relativePath),
        commands.getFileDiff(repoPath, relativePath, area),
      ]);
    } catch {
      // Unexpected IPC-level error (e.g., serialization failure).
      // Don't leave the UI stuck in a loading state.
      if (mySeq === selectSeq) {
        set({ fileContent: null, fileDiff: null, loading: false });
      }
      return;
    }

    // Stale check: another selectFile was called while we awaited.
    if (mySeq !== selectSeq) return;

    const fileContent = contentResult.status === "ok" ? contentResult.data : null;
    const fileDiff = diffResult.status === "ok" ? diffResult.data : null;

    // Only cache successful fetches (don't poison cache with failures).
    if (fileContent !== null || fileDiff !== null) {
      diffCache.set(`${area}:${relativePath}`, { fileContent, fileDiff });
    }

    set({ fileContent, fileDiff, loading: false });
  },

  // ─── refreshFile ─────────────────────────────────────────────────
  //
  // Re-fetches the currently-viewed file's data without clearing the
  // existing display (no loading state). Used after line-level
  // stage/unstage/discard operations.

  refreshFile: async (repoPath: string, relativePath: string, area: DiffArea) => {
    const mySeq = ++selectSeq;

    // Keep existing content visible (no loading flicker).
    set({ selectedFile: relativePath, selectedArea: area });

    let contentResult, diffResult;
    try {
      [contentResult, diffResult] = await Promise.all([
        commands.readFileContents(repoPath, relativePath),
        commands.getFileDiff(repoPath, relativePath, area),
      ]);
    } catch {
      if (mySeq === selectSeq) {
        set({ loading: false });
      }
      return;
    }

    if (mySeq !== selectSeq) return;

    const fileContent = contentResult.status === "ok" ? contentResult.data : null;
    const fileDiff = diffResult.status === "ok" ? diffResult.data : null;

    if (fileContent !== null || fileDiff !== null) {
      diffCache.set(`${area}:${relativePath}`, { fileContent, fileDiff });
    }

    // Skip state update if the data hasn't actually changed.
    // This prevents unnecessary re-renders and scroll resets
    // when the file watcher fires but the diff is identical.
    const current = get();
    if (
      current.fileContent === fileContent &&
      diffFingerprint(current.fileDiff) === diffFingerprint(fileDiff)
    ) {
      return;
    }

    set({ fileContent, fileDiff, loading: false });
  },

  setViewMode: (mode: DiffViewMode) => set({ viewMode: mode }),

  clearSelection: () => {
    selectSeq++; // Cancel any in-flight fetch.
    set({
      selectedFile: null,
      selectedArea: null,
      fileContent: null,
      fileDiff: null,
      loading: false,
    });
  },

  switchRepo: (from: string | null, to: string) => {
    selectSeq++; // Cancel any in-flight fetch.
    const current = get();
    if (from) {
      repoSelections.set(from, {
        selectedFile: current.selectedFile,
        selectedArea: current.selectedArea,
        fileContent: current.fileContent,
        fileDiff: current.fileDiff,
      });
    }
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
