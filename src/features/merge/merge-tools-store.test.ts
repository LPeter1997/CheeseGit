import { describe, it, expect, vi, beforeEach } from "vitest";
import { useMergeToolsStore } from "./merge-tools-store";

vi.mock("../../ipc/bindings", () => ({
  commands: {
    getAvailableMergeTools: vi.fn(),
    rescanMergeTools: vi.fn(),
  },
}));

vi.mock("../../shared/utils/app-state", () => ({
  getAppStateSafe: vi.fn(),
  saveAppStateSafe: vi.fn(),
}));

import { commands } from "../../ipc/bindings";
import { getAppStateSafe, saveAppStateSafe } from "../../shared/utils/app-state";

const mockGetAvailableMergeTools = vi.mocked(commands.getAvailableMergeTools);
const mockRescanMergeTools = vi.mocked(commands.rescanMergeTools);
const mockGetAppStateSafe = vi.mocked(getAppStateSafe);
const mockSaveAppStateSafe = vi.mocked(saveAppStateSafe);

beforeEach(() => {
  vi.clearAllMocks();
  useMergeToolsStore.setState({
    availableTools: [],
    selectedToolId: null,
    initialized: false,
  });
});

describe("useMergeToolsStore", () => {
  describe("initialize", () => {
    it("loads available tools and selects first when no preference", async () => {
      mockGetAvailableMergeTools.mockResolvedValue({
        status: "ok",
        data: [
          { id: "vscode", display_name: "Visual Studio Code", icon: null },
          { id: "neovim", display_name: "NeoVim", icon: null },
        ],
      });
      mockGetAppStateSafe.mockResolvedValue({});

      await useMergeToolsStore.getState().initialize();

      const state = useMergeToolsStore.getState();
      expect(state.initialized).toBe(true);
      expect(state.availableTools).toHaveLength(2);
      expect(state.selectedToolId).toBe("vscode");
    });

    it("restores preferred tool from app state", async () => {
      mockGetAvailableMergeTools.mockResolvedValue({
        status: "ok",
        data: [
          { id: "vscode", display_name: "Visual Studio Code", icon: null },
          { id: "neovim", display_name: "NeoVim", icon: null },
        ],
      });
      mockGetAppStateSafe.mockResolvedValue({ preferred_merge_tool: "neovim" });

      await useMergeToolsStore.getState().initialize();

      expect(useMergeToolsStore.getState().selectedToolId).toBe("neovim");
    });

    it("falls back to first tool when preferred is no longer available", async () => {
      mockGetAvailableMergeTools.mockResolvedValue({
        status: "ok",
        data: [
          { id: "vscode", display_name: "Visual Studio Code", icon: null },
        ],
      });
      mockGetAppStateSafe.mockResolvedValue({ preferred_merge_tool: "removed_tool" });

      await useMergeToolsStore.getState().initialize();

      expect(useMergeToolsStore.getState().selectedToolId).toBe("vscode");
    });

    it("sets null selectedToolId when no tools are available", async () => {
      mockGetAvailableMergeTools.mockResolvedValue({
        status: "ok",
        data: [],
      });
      mockGetAppStateSafe.mockResolvedValue({});

      await useMergeToolsStore.getState().initialize();

      const state = useMergeToolsStore.getState();
      expect(state.initialized).toBe(true);
      expect(state.selectedToolId).toBeNull();
    });

    it("does not crash on IPC error", async () => {
      mockGetAvailableMergeTools.mockResolvedValue({
        status: "error",
        error: { Other: "fail" },
      });

      await useMergeToolsStore.getState().initialize();

      // Should remain uninitialized
      expect(useMergeToolsStore.getState().initialized).toBe(false);
    });
  });

  describe("selectTool", () => {
    it("updates selectedToolId and persists preference", async () => {
      useMergeToolsStore.setState({
        availableTools: [
          { id: "vscode", display_name: "Visual Studio Code", icon: null },
          { id: "neovim", display_name: "NeoVim", icon: null },
        ],
        selectedToolId: "vscode",
        initialized: true,
      });
      mockGetAppStateSafe.mockResolvedValue({ theme: "dark" });
      mockSaveAppStateSafe.mockResolvedValue(true);

      await useMergeToolsStore.getState().selectTool("neovim");

      expect(useMergeToolsStore.getState().selectedToolId).toBe("neovim");
      expect(mockSaveAppStateSafe).toHaveBeenCalledWith({
        theme: "dark",
        preferred_merge_tool: "neovim",
      });
    });
  });

  describe("rescan", () => {
    it("updates available tools and keeps current selection", async () => {
      useMergeToolsStore.setState({
        availableTools: [{ id: "vscode", display_name: "Visual Studio Code", icon: null }],
        selectedToolId: "vscode",
        initialized: true,
      });
      mockRescanMergeTools.mockResolvedValue({
        status: "ok",
        data: [
          { id: "vscode", display_name: "Visual Studio Code", icon: null },
          { id: "meld", display_name: "Meld", icon: null },
        ],
      });

      await useMergeToolsStore.getState().rescan();

      const state = useMergeToolsStore.getState();
      expect(state.availableTools).toHaveLength(2);
      expect(state.selectedToolId).toBe("vscode");
    });

    it("falls back when current selection is no longer available", async () => {
      useMergeToolsStore.setState({
        availableTools: [
          { id: "vscode", display_name: "Visual Studio Code", icon: null },
          { id: "neovim", display_name: "NeoVim", icon: null },
        ],
        selectedToolId: "neovim",
        initialized: true,
      });
      mockRescanMergeTools.mockResolvedValue({
        status: "ok",
        data: [
          { id: "vscode", display_name: "Visual Studio Code", icon: null },
        ],
      });

      await useMergeToolsStore.getState().rescan();

      expect(useMergeToolsStore.getState().selectedToolId).toBe("vscode");
    });
  });
});
