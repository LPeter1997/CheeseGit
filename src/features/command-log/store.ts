import { create } from "zustand";
import { listen } from "@tauri-apps/api/event";
import { commands, type CommandEntry } from "../../ipc/bindings";

interface CommandLogState {
  entries: CommandEntry[];
  isOpen: boolean;
  showBackground: boolean;
  toggle: () => void;
  toggleShowBackground: () => void;
  refresh: () => Promise<void>;
}

export const useCommandLogStore = create<CommandLogState>((set) => ({
  entries: [],
  isOpen: false,
  showBackground: false,

  toggle: () => set((s) => ({ isOpen: !s.isOpen })),
  toggleShowBackground: () => set((s) => ({ showBackground: !s.showBackground })),

  refresh: async () => {
    const entries = await commands.getCommandLog();
    set({ entries });
  },
}));

// Subscribe to backend events — auto-refresh whenever a command is logged.
listen("command-log-updated", () => {
  useCommandLogStore.getState().refresh();
});
