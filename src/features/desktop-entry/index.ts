import { useEffect, useRef } from "react";
import { commands, type DesktopEntryStatus } from "../../ipc/bindings";
import { useAlertStore } from "../../shared/stores/alerts";
import { getAppStateSafe, saveAppStateSafe } from "../../shared/utils/app-state";

/**
 * Hook that checks the Linux desktop entry status on startup
 * and prompts the user to register/update if needed.
 */
export function useDesktopEntry() {
  const hasChecked = useRef(false);
  const addDesktopEntryAlert = useAlertStore((s) => s.addDesktopEntryAlert);

  useEffect(() => {
    if (import.meta.env.DEV) return;
    if (hasChecked.current) return;
    hasChecked.current = true;

    checkDesktopEntry();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  async function checkDesktopEntry() {
    try {
      const appState = await getAppStateSafe();
      if (appState.dismiss_desktop_entry) return;

      const result = await commands.checkDesktopEntryStatus();
      if (result.status === "error") return;

      const status = result.data;
      if (status === "Missing") {
        addDesktopEntryAlert("missing");
      } else if (status === "Stale") {
        addDesktopEntryAlert("stale");
      }
    } catch {
      // Silently ignore — not critical
    }
  }
}

/** Best-effort check for desktop entry status. */
export async function getDesktopEntryStatusSafe(): Promise<DesktopEntryStatus | null> {
  try {
    const result = await commands.checkDesktopEntryStatus();
    if (result.status === "ok") {
      return result.data;
    }
  } catch {
    // Ignore and return null
  }
  return null;
}

/** Register (or update) the desktop entry. */
export async function registerDesktopEntry(options?: { showSuccessAlert?: boolean }) {
  try {
    const result = await commands.registerDesktopEntry();
    if (result.status === "error") {
      useAlertStore.getState().addAlert(`Failed to register desktop entry: ${JSON.stringify(result.error)}`);
      return false;
    }
    if (options?.showSuccessAlert) {
      useAlertStore.getState().addAlert("Desktop entry registered.", "info");
    }
    return true;
  } catch {
    useAlertStore.getState().addAlert("Failed to register desktop entry");
    return false;
  }
}

/** Dismiss the desktop entry prompt permanently. */
export async function dismissDesktopEntry() {
  try {
    const appState = await getAppStateSafe();
    await saveAppStateSafe({ ...appState, dismiss_desktop_entry: true });
  } catch {
    // Silently ignore
  }
}
