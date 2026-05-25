import { useEffect } from "react";
import { listen } from "@tauri-apps/api/event";
import { commands } from "../../ipc/bindings";

interface RepoChangedPayload {
  repo_path: string;
}

/**
 * Hook that watches a repository for file changes and calls the callback when changes occur.
 * Starts/stops the watcher automatically based on the provided repoPath.
 */
export function useRepoWatcher(repoPath: string | null, onChange: () => void) {
  useEffect(() => {
    if (!repoPath) return;

    // Start watching
    commands.watchRepo(repoPath);

    const unlisten = listen<RepoChangedPayload>("repo-files-changed", (event) => {
      if (event.payload.repo_path === repoPath) {
        onChange();
      }
    });

    return () => {
      commands.unwatchRepo(repoPath);
      unlisten.then((fn) => fn());
    };
  }, [repoPath, onChange]);
}
