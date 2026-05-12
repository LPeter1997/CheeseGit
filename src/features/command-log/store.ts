import { create } from "zustand";
import { listen } from "@tauri-apps/api/event";
import { commands, type CommandEntry } from "../../ipc/bindings";

interface CommandLogState {
  entries: CommandEntry[];
  isOpen: boolean;
  toggle: () => void;
  refresh: () => Promise<void>;
}

export const useCommandLogStore = create<CommandLogState>((set) => ({
  entries: [],
  isOpen: false,

  toggle: () => set((s) => ({ isOpen: !s.isOpen })),

  refresh: async () => {
    const entries = await commands.getCommandLog();
    set({ entries });
  },
}));

// Subscribe to backend events — auto-refresh whenever a command is logged.
listen("command-log-updated", () => {
  useCommandLogStore.getState().refresh();
});
