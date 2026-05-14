import { useEffect, useCallback, useState, useRef } from "react";
import { commands, type BranchTrackingStatus } from "../../../ipc/bindings";
import { useStagingStore } from "../../staging";
import { useHistoryStore } from "../../history";

const POLL_INTERVAL_MS = 3_000;

/**
 * Centralized polling hook that keeps all repo-related stores in sync.
 * Polls status, commit history, current branch, and tracking status on a timer.
 * Pauses automatically when the document is hidden.
 *
 * Returns the current branch name, tracking status, and a manual `refresh`
 * function that can be called after mutations for an immediate update.
 */
export function useRepoPolling(repoPath: string) {
  const fetchStatus = useStagingStore((s) => s.fetchStatus);
  const fetchLog = useHistoryStore((s) => s.fetchLog);
  const [currentBranch, setCurrentBranch] = useState<string | null>(null);
  const [tracking, setTracking] = useState<BranchTrackingStatus | null>(null);
  const intervalRef = useRef<ReturnType<typeof setInterval> | null>(null);

  const refresh = useCallback(async () => {
    fetchStatus(repoPath);
    fetchLog(repoPath);
    const [branchResult, trackingResult] = await Promise.all([
      commands.getCurrentBranch(repoPath),
      commands.getTrackingStatus(repoPath),
    ]);
    if (branchResult.status === "ok") {
      setCurrentBranch(branchResult.data);
    }
    if (trackingResult.status === "ok") {
      setTracking(trackingResult.data);
    }
  }, [repoPath, fetchStatus, fetchLog]);

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
