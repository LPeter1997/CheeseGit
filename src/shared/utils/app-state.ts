import { commands, type AppState } from "../../ipc/bindings";

/**
 * Returns persisted app state; falls back to empty object on read failure.
 */
export async function getAppStateSafe(): Promise<AppState> {
  try {
    const result = await commands.getAppState();
    if (result.status === "ok") {
      return result.data;
    }
  } catch {
    // Ignore and fall back to defaults
  }
  return {};
}

/**
 * Best-effort app-state persistence.
 */
export async function saveAppStateSafe(state: AppState): Promise<boolean> {
  try {
    const result = await commands.saveAppState(state);
    return result.status === "ok";
  } catch {
    return false;
  }
}
