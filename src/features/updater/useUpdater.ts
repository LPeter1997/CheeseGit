import { useEffect, useRef } from "react";
import { check, type Update } from "@tauri-apps/plugin-updater";
import { relaunch } from "@tauri-apps/plugin-process";
import { useAlertStore } from "../../shared/stores/alerts";
import { useUpdaterStore } from "./store";
import { commands } from "../../ipc/bindings";
import { getCurrentWindow } from "@tauri-apps/api/window";

/** Holds the pending update object for "on exit" flow. */
let pendingUpdate: Update | null = null;

/**
 * Hook that checks for updates on startup (production only) and provides
 * action handlers for the update banner buttons.
 */
export function useUpdater() {
  const hasChecked = useRef(false);
  const addUpdateAlert = useAlertStore((s) => s.addUpdateAlert);
  const setStatus = useUpdaterStore((s) => s.setStatus);
  const setAvailableVersion = useUpdaterStore((s) => s.setAvailableVersion);

  useEffect(() => {
    if (import.meta.env.DEV) return;
    if (hasChecked.current) return;
    hasChecked.current = true;

    checkForUpdate();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  async function checkForUpdate() {
    setStatus("checking");
    try {
      const update = await check();
      if (!update) {
        setStatus("idle");
        return;
      }

      const appState = await commands.getAppState();
      if (appState.skipped_version === update.version) {
        setStatus("idle");
        return;
      }

      setAvailableVersion(update.version);
      addUpdateAlert(update.version);
      setStatus("idle");
      pendingUpdate = update;
    } catch {
      setStatus("idle");
    }
  }
}

/** Download and install the update immediately, then relaunch. */
export async function updateNow() {
  const { setStatus, setDownloadProgress } = useUpdaterStore.getState();

  if (!pendingUpdate) {
    // Try to check again
    try {
      const update = await check();
      if (!update) return;
      pendingUpdate = update;
    } catch {
      return;
    }
  }

  setStatus("downloading");
  setDownloadProgress(0);

  try {
    let downloaded = 0;
    let contentLength = 0;

    await pendingUpdate.downloadAndInstall((event) => {
      switch (event.event) {
        case "Started":
          contentLength = event.data.contentLength ?? 0;
          break;
        case "Progress":
          downloaded += event.data.chunkLength;
          if (contentLength > 0) {
            setDownloadProgress(Math.round((downloaded / contentLength) * 100));
          }
          break;
        case "Finished":
          setDownloadProgress(100);
          break;
      }
    });

    // Persist release notes so we can show "What's new" on next startup.
    await persistChangelog(pendingUpdate.version, pendingUpdate.body);

    // On Windows the app exits automatically during install.
    // On macOS/Linux we need to relaunch.
    await relaunch();
  } catch {
    setStatus("idle");
    setDownloadProgress(null);
  }
}

/** Download the update in background; install when the app closes. */
export async function updateOnExit() {
  const { setStatus, setDownloadProgress } = useUpdaterStore.getState();

  if (!pendingUpdate) {
    try {
      const update = await check();
      if (!update) return;
      pendingUpdate = update;
    } catch {
      return;
    }
  }

  // Remove the banner
  const alerts = useAlertStore.getState().alerts;
  const updateAlert = alerts.find((a) => a.type === "update");
  if (updateAlert) {
    useAlertStore.getState().removeAlert(updateAlert.id);
  }

  setStatus("downloading");
  setDownloadProgress(0);

  try {
    let downloaded = 0;
    let contentLength = 0;

    await pendingUpdate.download((event) => {
      switch (event.event) {
        case "Started":
          contentLength = event.data.contentLength ?? 0;
          break;
        case "Progress":
          downloaded += event.data.chunkLength;
          if (contentLength > 0) {
            setDownloadProgress(Math.round((downloaded / contentLength) * 100));
          }
          break;
        case "Finished":
          setDownloadProgress(100);
          break;
      }
    });

    setStatus("ready-on-exit");

    // Persist release notes so we can show "What's new" on next startup.
    await persistChangelog(pendingUpdate.version, pendingUpdate.body);

    // Listen for window close and install before exit.
    const currentWindow = getCurrentWindow();
    const unlisten = await currentWindow.onCloseRequested(async (event) => {
      event.preventDefault();
      try {
        await pendingUpdate!.install();
        // On Windows, app exits during install. On others, we relaunch.
        await relaunch();
      } catch {
        // If install fails, just close normally.
        unlisten();
        await currentWindow.close();
      }
    });
  } catch {
    setStatus("idle");
    setDownloadProgress(null);
  }
}

/** Skip this version — persist so we don't prompt again. */
export async function skipUpdate() {
  const version = useUpdaterStore.getState().availableVersion;
  if (version) {
    const appState = await commands.getAppState();
    await commands.saveAppState({ ...appState, skipped_version: version });
  }
  pendingUpdate = null;

  // Remove the banner
  const alerts = useAlertStore.getState().alerts;
  const updateAlert = alerts.find((a) => a.type === "update");
  if (updateAlert) {
    useAlertStore.getState().removeAlert(updateAlert.id);
  }

  useUpdaterStore.getState().setStatus("idle");
  useUpdaterStore.getState().setAvailableVersion(null);
}

/** Save release notes to AppState so "What's new" dialog shows on next startup. */
async function persistChangelog(version: string, body?: string) {
  if (!body) return;
  const appState = await commands.getAppState();
  await commands.saveAppState({
    ...appState,
    pending_changelog: body,
    pending_changelog_version: version,
  });
}
