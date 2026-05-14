import { useEffect, useRef } from "react";
import { commands, type StatusEntry } from "../../../ipc/bindings";
import { getDiffCache } from "../store";

const PREFETCH_DELAY_MS = 500;
const MAX_CONCURRENT_PREFETCHES = 3;

/**
 * Prefetches diffs for all files in the unstaged/staged lists into the LRU cache.
 * This eliminates the first-time flicker when selecting a file.
 * Runs with a short delay after the file lists change to avoid competing with user actions.
 */
export function useDiffPrefetch(
  repoPath: string,
  unstaged: StatusEntry[],
  staged: StatusEntry[],
) {
  const timerRef = useRef<ReturnType<typeof setTimeout> | null>(null);
  const abortRef = useRef(false);

  useEffect(() => {
    // Clear any pending prefetch on list change
    if (timerRef.current) {
      clearTimeout(timerRef.current);
    }
    abortRef.current = true;

    const cache = getDiffCache();

    // Collect files that aren't already cached
    const toPrefetch: { path: string; area: "Unstaged" | "Staged" }[] = [];

    for (const entry of unstaged) {
      const key = `Unstaged:${entry.path}`;
      if (cache.get(key) === undefined) {
        toPrefetch.push({ path: entry.path, area: "Unstaged" });
      }
    }
    for (const entry of staged) {
      const key = `Staged:${entry.path}`;
      if (cache.get(key) === undefined) {
        toPrefetch.push({ path: entry.path, area: "Staged" });
      }
    }

    if (toPrefetch.length === 0) return;

    abortRef.current = false;
    const abortFlag = abortRef;

    timerRef.current = setTimeout(async () => {
      // Prefetch in batches to avoid overwhelming the backend
      for (let i = 0; i < toPrefetch.length; i += MAX_CONCURRENT_PREFETCHES) {
        if (abortFlag.current) return;

        const batch = toPrefetch.slice(i, i + MAX_CONCURRENT_PREFETCHES);
        await Promise.all(
          batch.map(async ({ path, area }) => {
            if (abortFlag.current) return;
            const cacheKey = `${area}:${path}`;
            // Double-check it's still not cached
            if (cache.get(cacheKey) !== undefined) return;

            const [contentResult, diffResult] = await Promise.all([
              commands.readFileContents(repoPath, path),
              commands.getFileDiff(repoPath, path, area),
            ]);

            if (abortFlag.current) return;

            const fileContent = contentResult.status === "ok" ? contentResult.data : null;
            const fileDiff = diffResult.status === "ok" ? diffResult.data : null;
            cache.set(cacheKey, { fileContent, fileDiff });
          }),
        );
      }
    }, PREFETCH_DELAY_MS);

    return () => {
      abortRef.current = true;
      if (timerRef.current) {
        clearTimeout(timerRef.current);
      }
    };
  }, [repoPath, unstaged, staged]);
}
