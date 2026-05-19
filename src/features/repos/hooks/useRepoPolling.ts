import { useEffect, useCallback, useState, useRef } from "react";
import { commands, type BranchTrackingStatus } from "../../../ipc/bindings";
import { useStagingStore } from "../../staging";
import { useHistoryStore } from "../../history";
import { useReposStore } from "../store";

const POLL_INTERVAL_MS = 3_000;

/**
 * Centralized polling hook that keeps all repo-related stores in sync.
 * Polls status, commit history, current branch, and tracking status on a timer.
 * Pauses automatically when the document is hidden.
 * Prevents overlapping polls (skips tick if previous one still in-flight).
 *
 * Returns the current branch name, tracking status, and a manual `refresh`
 * function that can be called after mutations for an immediate update.
 */
export function useRepoPolling(repoPath: string) {
  const fetchStatus = useStagingStore((s) => s.fetchStatus);
  const fetchLog = useHistoryStore((s) => s.fetchLog);
  const fetchGraph = useHistoryStore((s) => s.fetchGraph);
  const [currentBranch, setCurrentBranch] = useState<string | null>(null);
  const [tracking, setTracking] = useState<BranchTrackingStatus | null>(null);
  const intervalRef = useRef<ReturnType<typeof setInterval> | null>(null);
  const inflightRef = useRef(false);

  const refresh = useCallback(async () => {
    // Only update the active tab to save processing power.
    const { repos, activeIndex } = useReposStore.getState();
    if (repos[activeIndex]?.path !== repoPath) return;

    // Prevent overlapping polls — skip if previous cycle hasn't completed.
    if (inflightRef.current) return;
    inflightRef.current = true;

    try {
      // Fire status + lightweight metadata in parallel.
      const [branchResult, trackingResult, remotesResult] = await Promise.all([
        commands.getCurrentBranch(repoPath),
        commands.getTrackingStatus(repoPath),
        commands.listRemotes(repoPath),
        fetchStatus(repoPath),
      ]);

      const branch = branchResult.status === "ok" ? branchResult.data : null;
      if (branch) {
        setCurrentBranch(branch);
      }
      if (trackingResult.status === "ok") {
        setTracking(trackingResult.data);
      }

      // Fetch graph data for the current branch (+ all visible branches).
      // Use the first remote for local-only detection.
      const remote = remotesResult.status === "ok" && remotesResult.data.length > 0
        ? remotesResult.data[0].name
        : null;

      // Always fetch log + graph — they have internal change detection
      // (graphDataEqual) to skip expensive layout recomputation.
      const graphPromise = branch ? fetchGraph(repoPath, [], remote, branch) : Promise.resolve();
      await Promise.all([fetchLog(repoPath), graphPromise]);
    } finally {
      inflightRef.current = false;
    }
  }, [repoPath, fetchStatus, fetchLog, fetchGraph]);

  useEffect(() => {
    function start() {
      if (intervalRef.current === null) {
        intervalRef.current = setInterval(refresh, POLL_INTERVAL_MS);
      }
    }

    function stop() {
      if (intervalRef.current !== null) {
        clearInterval(intervalRef.current);
        intervalRef.current = null;
      }
    }

    function onVisibilityChange() {
      if (document.hidden) {
        stop();
      } else {
        refresh();
        start();
      }
    }

    // Immediate fetch on mount, then start polling
    refresh();
    start();

    document.addEventListener("visibilitychange", onVisibilityChange);

    return () => {
      stop();
      document.removeEventListener("visibilitychange", onVisibilityChange);
    };
  }, [refresh]);

  return { currentBranch, tracking, refresh };
}
