import { type BranchInfo, type RemoteBranchInfo } from "../../ipc/bindings";

/**
 * Snapshot of the last successfully fetched branch list for a repository.
 */
export interface BranchListSnapshot {
  branches: BranchInfo[];
  remoteBranches: RemoteBranchInfo[];
}

/**
 * Process-wide cache of branch lists keyed by repository path.
 *
 * Opening the branch dropdown for a repo with many branches triggers a
 * `git for-each-ref` that can take a noticeable amount of time. Caching the
 * previous result lets us render it instantly while a fresh fetch runs in the
 * background, so the dropdown never shows an empty "Loading…" state once it has
 * been opened at least once.
 */
const cache = new Map<string, BranchListSnapshot>();

export function getCachedBranchList(repoPath: string): BranchListSnapshot | undefined {
  return cache.get(repoPath);
}

export function setCachedBranchList(repoPath: string, snapshot: BranchListSnapshot): void {
  cache.set(repoPath, snapshot);
}

/** Drop a repo's cached branch list (e.g. when the repo is closed). */
export function clearCachedBranchList(repoPath: string): void {
  cache.delete(repoPath);
}
