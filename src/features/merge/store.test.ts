import { describe, it, expect, vi, beforeEach } from "vitest";
import { useMergeStore } from "./store";

vi.mock("../../ipc/bindings", () => ({
  commands: {
    mergeBranch: vi.fn(),
    mergeAbort: vi.fn(),
    getConflictCounts: vi.fn(),
    resolveConflict: vi.fn(),
    openInMergeTool: vi.fn(),
    mergeContinue: vi.fn(),
    revertCommit: vi.fn(),
    revertAbort: vi.fn(),
    revertContinue: vi.fn(),
  },
}));

vi.mock("../../shared/stores/alerts", () => ({
  useAlertStore: {
    getState: () => ({ addAlert: vi.fn() }),
  },
}));

import { commands } from "../../ipc/bindings";
const mockMergeBranch = vi.mocked(commands.mergeBranch);
const mockMergeAbort = vi.mocked(commands.mergeAbort);
const mockGetConflictCounts = vi.mocked(commands.getConflictCounts);
const mockResolveConflict = vi.mocked(commands.resolveConflict);
const mockOpenInMergeTool = vi.mocked(commands.openInMergeTool);
const mockMergeContinue = vi.mocked(commands.mergeContinue);
const mockRevertCommit = vi.mocked(commands.revertCommit);
const mockRevertAbort = vi.mocked(commands.revertAbort);
const mockRevertContinue = vi.mocked(commands.revertContinue);

function resetStore() {
  useMergeStore.setState({
    merging: false,
    isRevert: false,
    incomingBranch: "",
    conflictFiles: [],
    resolutions: {},
    resolvedExternally: new Set(),
    loading: false,
  });
}

describe("useMergeStore", () => {
  beforeEach(() => {
    resetStore();
    vi.clearAllMocks();
  });

  it("starts with empty state", () => {
    const state = useMergeStore.getState();
    expect(state.merging).toBe(false);
    expect(state.incomingBranch).toBe("");
    expect(state.conflictFiles).toEqual([]);
    expect(state.loading).toBe(false);
  });

  describe("mergeBranch", () => {
    it("returns true and stays non-merging on successful merge", async () => {
      mockMergeBranch.mockResolvedValue({ status: "ok", data: "Success" });

      const result = await useMergeStore.getState().mergeBranch("/repo", "feature");

      expect(result).toBe(true);
      expect(useMergeStore.getState().merging).toBe(false);
    });

    it("enters merging state with conflicts on conflict result", async () => {
      mockMergeBranch.mockResolvedValue({
        status: "ok",
        data: {
          Conflict: {
            incoming_branch: "feature",
            conflicted_files: ["file1.txt", "file2.txt"],
          },
        },
      });
      mockGetConflictCounts.mockResolvedValue({
        status: "ok",
        data: [
          { path: "file1.txt", conflict_count: 2 },
          { path: "file2.txt", conflict_count: 1 },
        ],
      });

      const result = await useMergeStore.getState().mergeBranch("/repo", "feature");

      expect(result).toBe(false);
      const state = useMergeStore.getState();
      expect(state.merging).toBe(true);
      expect(state.incomingBranch).toBe("feature");
      expect(state.conflictFiles).toEqual([
        { path: "file1.txt", conflict_count: 2 },
        { path: "file2.txt", conflict_count: 1 },
      ]);
      expect(state.resolutions).toEqual({ "file1.txt": null, "file2.txt": null });
    });

    it("returns false and shows alert on error", async () => {
      mockMergeBranch.mockResolvedValue({
        status: "error",
        error: { Git: "Not a valid branch" },
      });

      const result = await useMergeStore.getState().mergeBranch("/repo", "nonexistent");

      expect(result).toBe(false);
      expect(useMergeStore.getState().merging).toBe(false);
    });
  });

  describe("abortMerge", () => {
    it("clears merge state on success", async () => {
      useMergeStore.setState({
        merging: true,
        incomingBranch: "feature",
        conflictFiles: [{ path: "file.txt", conflict_count: 1 }],
        resolutions: { "file.txt": null },
      });
      mockMergeAbort.mockResolvedValue({ status: "ok", data: null });

      await useMergeStore.getState().abortMerge("/repo");

      const state = useMergeStore.getState();
      expect(state.merging).toBe(false);
      expect(state.conflictFiles).toEqual([]);
    });

    it("keeps state on error", async () => {
      useMergeStore.setState({
        merging: true,
        incomingBranch: "feature",
        conflictFiles: [{ path: "file.txt", conflict_count: 1 }],
        resolutions: { "file.txt": null },
      });
      mockMergeAbort.mockResolvedValue({
        status: "error",
        error: { Git: "no merge in progress" },
      });

      await useMergeStore.getState().abortMerge("/repo");

      expect(useMergeStore.getState().merging).toBe(true);
    });
  });

  describe("refreshConflicts", () => {
    it("updates conflict files and detects external resolution", async () => {
      useMergeStore.setState({
        merging: true,
        incomingBranch: "feature",
        conflictFiles: [
          { path: "a.txt", conflict_count: 2 },
          { path: "b.txt", conflict_count: 1 },
        ],
        resolutions: { "a.txt": null, "b.txt": null },
        resolvedExternally: new Set(),
      });
      // a.txt no longer in conflicts (resolved externally)
      mockGetConflictCounts.mockResolvedValue({
        status: "ok",
        data: [{ path: "b.txt", conflict_count: 1 }],
      });

      await useMergeStore.getState().refreshConflicts("/repo");

      const state = useMergeStore.getState();
      expect(state.conflictFiles).toEqual([{ path: "b.txt", conflict_count: 1 }]);
      expect(state.resolvedExternally.has("a.txt")).toBe(true);
    });
  });

  describe("setResolution", () => {
    it("sets resolution for a file without applying", () => {
      useMergeStore.setState({
        merging: true,
        conflictFiles: [{ path: "file.txt", conflict_count: 1 }],
        resolutions: { "file.txt": null },
      });

      useMergeStore.getState().setResolution("file.txt", "AcceptCurrent");

      expect(useMergeStore.getState().resolutions["file.txt"]).toBe("AcceptCurrent");
    });

    it("can toggle resolution off", () => {
      useMergeStore.setState({
        merging: true,
        conflictFiles: [{ path: "file.txt", conflict_count: 1 }],
        resolutions: { "file.txt": "AcceptCurrent" },
      });

      useMergeStore.getState().setResolution("file.txt", null);

      expect(useMergeStore.getState().resolutions["file.txt"]).toBeNull();
    });
  });

  describe("applyAndFinalize", () => {
    it("applies resolutions and completes merge", async () => {
      useMergeStore.setState({
        merging: true,
        incomingBranch: "feature",
        conflictFiles: [{ path: "a.txt", conflict_count: 1 }],
        resolutions: { "a.txt": "AcceptIncoming" },
        resolvedExternally: new Set(),
      });
      mockResolveConflict.mockResolvedValue({ status: "ok", data: null });
      mockMergeContinue.mockResolvedValue({ status: "ok", data: null });

      const result = await useMergeStore.getState().applyAndFinalize("/repo", "Merge branch 'feature'");

      expect(result).toBe(true);
      expect(mockResolveConflict).toHaveBeenCalledWith("/repo", "a.txt", "AcceptIncoming");
      expect(mockMergeContinue).toHaveBeenCalledWith("/repo", "Merge branch 'feature'");
      expect(useMergeStore.getState().merging).toBe(false);
    });

    it("stages externally resolved files before commit", async () => {
      useMergeStore.setState({
        merging: true,
        incomingBranch: "feature",
        conflictFiles: [{ path: "a.txt", conflict_count: 1 }],
        resolutions: { "a.txt": "AcceptCurrent" },
        resolvedExternally: new Set(["a.txt"]),
      });
      mockResolveConflict.mockResolvedValue({ status: "ok", data: null });
      mockMergeContinue.mockResolvedValue({ status: "ok", data: null });

      await useMergeStore.getState().applyAndFinalize("/repo", "msg");

      // Externally resolved files are staged with AcceptBoth (git add)
      expect(mockResolveConflict).toHaveBeenCalledWith("/repo", "a.txt", "AcceptBoth");
      expect(mockMergeContinue).toHaveBeenCalled();
    });

    it("returns false on resolve error", async () => {
      useMergeStore.setState({
        merging: true,
        incomingBranch: "feature",
        conflictFiles: [{ path: "a.txt", conflict_count: 1 }],
        resolutions: { "a.txt": "AcceptCurrent" },
        resolvedExternally: new Set(),
      });
      mockResolveConflict.mockResolvedValue({
        status: "error",
        error: { Git: "failed" },
      });

      const result = await useMergeStore.getState().applyAndFinalize("/repo", "msg");

      expect(result).toBe(false);
      expect(useMergeStore.getState().merging).toBe(true);
    });
  });

  describe("openInMergeTool", () => {
    it("calls the command with correct arguments", async () => {
      mockOpenInMergeTool.mockResolvedValue({ status: "ok", data: null });

      await useMergeStore.getState().openInMergeTool("/repo", "file.txt");

      expect(mockOpenInMergeTool).toHaveBeenCalledWith("/repo", "file.txt");
    });
  });

  describe("revertCommit", () => {
    it("returns true on successful revert", async () => {
      mockRevertCommit.mockResolvedValue({ status: "ok", data: "Success" });

      const result = await useMergeStore.getState().revertCommit("/repo", "abc123");

      expect(result).toBe(true);
      expect(useMergeStore.getState().merging).toBe(false);
      expect(useMergeStore.getState().isRevert).toBe(false);
    });

    it("enters merging state with isRevert on conflict", async () => {
      mockRevertCommit.mockResolvedValue({
        status: "ok",
        data: {
          Conflict: {
            incoming_branch: "abc123",
            conflicted_files: ["file.txt"],
          },
        },
      });
      mockGetConflictCounts.mockResolvedValue({
        status: "ok",
        data: [{ path: "file.txt", conflict_count: 1 }],
      });

      const result = await useMergeStore.getState().revertCommit("/repo", "abc123");

      expect(result).toBe(false);
      const state = useMergeStore.getState();
      expect(state.merging).toBe(true);
      expect(state.isRevert).toBe(true);
      expect(state.incomingBranch).toBe("abc123");
      expect(state.conflictFiles).toEqual([{ path: "file.txt", conflict_count: 1 }]);
    });

    it("returns false and shows alert on error", async () => {
      mockRevertCommit.mockResolvedValue({
        status: "error",
        error: { Git: "Cannot revert" },
      });

      const result = await useMergeStore.getState().revertCommit("/repo", "abc123");

      expect(result).toBe(false);
      expect(useMergeStore.getState().merging).toBe(false);
    });
  });

  describe("abortMerge (revert mode)", () => {
    it("calls revertAbort when isRevert is true", async () => {
      useMergeStore.setState({
        merging: true,
        isRevert: true,
        incomingBranch: "abc123",
        conflictFiles: [{ path: "file.txt", conflict_count: 1 }],
        resolutions: { "file.txt": null },
      });
      mockRevertAbort.mockResolvedValue({ status: "ok", data: null });

      await useMergeStore.getState().abortMerge("/repo");

      expect(mockRevertAbort).toHaveBeenCalledWith("/repo");
      expect(mockMergeAbort).not.toHaveBeenCalled();
      expect(useMergeStore.getState().merging).toBe(false);
    });
  });

  describe("applyAndFinalize (revert mode)", () => {
    it("calls revertContinue instead of mergeContinue", async () => {
      useMergeStore.setState({
        merging: true,
        isRevert: true,
        incomingBranch: "abc123",
        conflictFiles: [{ path: "a.txt", conflict_count: 1 }],
        resolutions: { "a.txt": "AcceptIncoming" },
        resolvedExternally: new Set(),
      });
      mockResolveConflict.mockResolvedValue({ status: "ok", data: null });
      mockRevertContinue.mockResolvedValue({ status: "ok", data: null });

      const result = await useMergeStore.getState().applyAndFinalize("/repo", "Revert");

      expect(result).toBe(true);
      expect(mockRevertContinue).toHaveBeenCalledWith("/repo");
      expect(mockMergeContinue).not.toHaveBeenCalled();
      expect(useMergeStore.getState().merging).toBe(false);
    });
  });
});
