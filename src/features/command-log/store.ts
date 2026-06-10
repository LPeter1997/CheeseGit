import { create } from "zustand";
import { listen } from "@tauri-apps/api/event";
import { save } from "@tauri-apps/plugin-dialog";
import { commands, type CommandEntry } from "../../ipc/bindings";

interface CommandLogState {
  entries: CommandEntry[];
  isOpen: boolean;
  showBackground: boolean;
  search: string;
  toggle: () => void;
  toggleShowBackground: () => void;
  setSearch: (value: string) => void;
  refresh: () => Promise<void>;
  exportLog: () => Promise<void>;
}

export const useCommandLogStore = create<CommandLogState>((set, get) => ({
  entries: [],
  isOpen: false,
  showBackground: false,
  search: "",

  toggle: () => {
    const opening = !get().isOpen;
    set({ isOpen: opening });
    // Fetch fresh data when the panel opens.
    if (opening) {
      get().refresh();
    }
  },
  toggleShowBackground: () => set((s) => ({ showBackground: !s.showBackground })),
  setSearch: (value: string) => set({ search: value }),

  refresh: async () => {
    const result = await commands.getCommandLog();
    if (result.status === "error") return;

    const entries = result.data;
    // Skip state update if the log hasn't changed (avoids unnecessary re-renders).
    const prev = get().entries;
    if (
      prev.length === entries.length &&
      prev.length > 0 &&
      prev[prev.length - 1].timestamp === entries[entries.length - 1].timestamp
    ) {
      return;
    }
    set({ entries });
  },

  exportLog: async () => {
    const path = await save({
      title: "Export Command Log",
      defaultPath: "cheesegit-command-log.txt",
      filters: [{ name: "Text", extensions: ["txt", "log"] }],
    });
    if (!path) return;
    await commands.exportCommandLog(path);
  },
}));

// Subscribe to backend events — debounced refresh to coalesce rapid bursts.
// Only refreshes when the panel is open; otherwise deferred until toggle.
let debounceTimer: ReturnType<typeof setTimeout> | null = null;
listen("command-log-updated", () => {
  if (!useCommandLogStore.getState().isOpen) return;
  if (debounceTimer) clearTimeout(debounceTimer);
  debounceTimer = setTimeout(() => {
    debounceTimer = null;
    useCommandLogStore.getState().refresh();
  }, 300);
});
