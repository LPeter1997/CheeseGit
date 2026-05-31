import { create } from "zustand";
import { commands, type MergeToolInfo } from "../../ipc/bindings";
import { getAppStateSafe, saveAppStateSafe } from "../../shared/utils/app-state";

interface MergeToolsState {
  /** Available merge tools detected on the system. */
  availableTools: MergeToolInfo[];
  /** Currently selected merge tool id, or null if none available. */
  selectedToolId: string | null;
  /** Whether tool detection has completed at least once. */
  initialized: boolean;

  /** Load available tools and restore the preferred selection. */
  initialize: () => Promise<void>;
  /** Select a different merge tool and persist the preference. */
  selectTool: (toolId: string) => Promise<void>;
  /** Re-scan the system for available tools. */
  rescan: () => Promise<void>;
}

export const useMergeToolsStore = create<MergeToolsState>((set, get) => ({
  availableTools: [],
  selectedToolId: null,
  initialized: false,

  initialize: async () => {
    const result = await commands.getAvailableMergeTools();
    if (result.status !== "ok") return;

    const tools = result.data;
    const appState = await getAppStateSafe();
    const preferred = appState.preferred_merge_tool ?? null;

    // Use the preferred tool if still available, otherwise first available
    let selectedId: string | null = null;
    if (preferred && tools.some((t) => t.id === preferred)) {
      selectedId = preferred;
    } else if (tools.length > 0) {
      selectedId = tools[0].id;
    }

    set({ availableTools: tools, selectedToolId: selectedId, initialized: true });
  },

  selectTool: async (toolId: string) => {
    set({ selectedToolId: toolId });
    // Persist the preference
    const appState = await getAppStateSafe();
    await saveAppStateSafe({ ...appState, preferred_merge_tool: toolId });
  },

  rescan: async () => {
    const result = await commands.rescanMergeTools();
    if (result.status !== "ok") return;

    const tools = result.data;
    const currentId = get().selectedToolId;

    // Keep current selection if still available, otherwise fall back
    let selectedId: string | null = null;
    if (currentId && tools.some((t) => t.id === currentId)) {
      selectedId = currentId;
    } else if (tools.length > 0) {
      selectedId = tools[0].id;
    }

    set({ availableTools: tools, selectedToolId: selectedId });
  },
}));
