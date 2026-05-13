import { describe, it, expect, vi, beforeEach } from "vitest";
import { useStagingStore } from "./store";

vi.mock("../../ipc/bindings", () => ({
  commands: {
    getStatus: vi.fn(),
    commit: vi.fn(),
    stageFiles: vi.fn(),
    unstageFiles: vi.fn(),
  },
}));

vi.mock("../../shared/stores/toast", () => ({
  useToastStore: {
    getState: () => ({ addToast: vi.fn() }),
  },
}));

import { commands } from "../../ipc/bindings";
const mockGetStatus = vi.mocked(commands.getStatus);
const mockCommit = vi.mocked(commands.commit);
const mockStageFiles = vi.mocked(commands.stageFiles);
const mockUnstageFiles = vi.mocked(commands.unstageFiles);

function resetStore() {
  useStagingStore.setState({
    staged: [],
    unstaged: [],
    summary: "",
    description: "",
    loading: false,
    committing: false,
    defaultSummary: "",
    initialized: false,
  });
}

describe("useStagingStore", () => {
  beforeEach(() => {
    resetStore();
    vi.clearAllMocks();
  });

  it("starts with empty state", () => {
    const state = useStagingStore.getState();
    expect(state.staged).toEqual([]);
    expect(state.unstaged).toEqual([]);
    expect(state.summary).toBe("");
    expect(state.description).toBe("");
  });

  it("fetchStatus populates staged and unstaged", async () => {
    mockGetStatus.mockResolvedValue({
      status: "ok",
      data: {
        staged: [{ path: "a.txt", status: "Modified" }],
        unstaged: [{ path: "b.txt", status: "Untracked" }],
      },
    });

    await useStagingStore.getState().fetchStatus("/repo");

    const state = useStagingStore.getState();
    expect(state.staged).toHaveLength(1);
    expect(state.unstaged).toHaveLength(1);
    expect(state.loading).toBe(false);
  });

  it("sets defaultSummary for single staged file", async () => {
    mockGetStatus.mockResolvedValue({
      status: "ok",
      data: {
        staged: [{ path: "src/utils.ts", status: "Modified" }],
        unstaged: [],
      },
    });

    await useStagingStore.getState().fetchStatus("/repo");

    expect(useStagingStore.getState().defaultSummary).toBe("Update utils.ts");
  });

  it("no defaultSummary for multiple staged files", async () => {
    mockGetStatus.mockResolvedValue({
      status: "ok",
      data: {
        staged: [
          { path: "a.txt", status: "Modified" },
          { path: "b.txt", status: "Added" },
        ],
        unstaged: [],
      },
    });

    await useStagingStore.getState().fetchStatus("/repo");

    expect(useStagingStore.getState().defaultSummary).toBe("");
  });

  it("commit succeeds and resets form", async () => {
    useStagingStore.setState({
      staged: [{ path: "a.txt", status: "Modified" }],
      summary: "my commit",
    });

    mockCommit.mockResolvedValue({ status: "ok", data: null });
    mockGetStatus.mockResolvedValue({
      status: "ok",
      data: { staged: [], unstaged: [] },
    });

    const ok = await useStagingStore.getState().commit("/repo");

    expect(ok).toBe(true);
    expect(mockCommit).toHaveBeenCalledWith("/repo", "my commit", "");
    expect(useStagingStore.getState().summary).toBe("");
    expect(useStagingStore.getState().description).toBe("");
  });

  it("commit uses defaultSummary when summary is empty", async () => {
    useStagingStore.setState({
      staged: [{ path: "file.txt", status: "Modified" }],
      summary: "",
      defaultSummary: "Update file.txt",
    });

    mockCommit.mockResolvedValue({ status: "ok", data: null });
    mockGetStatus.mockResolvedValue({
      status: "ok",
      data: { staged: [], unstaged: [] },
    });

    await useStagingStore.getState().commit("/repo");

    expect(mockCommit).toHaveBeenCalledWith("/repo", "Update file.txt", "");
  });

  it("commit returns false when no summary available", async () => {
    useStagingStore.setState({
      staged: [{ path: "a.txt", status: "Modified" }],
      summary: "",
      defaultSummary: "",
    });

    const ok = await useStagingStore.getState().commit("/repo");
    expect(ok).toBe(false);
    expect(mockCommit).not.toHaveBeenCalled();
  });

  it("clear resets all state", () => {
    useStagingStore.setState({
      staged: [{ path: "a.txt", status: "Modified" }],
      unstaged: [{ path: "b.txt", status: "Untracked" }],
      summary: "hello",
      description: "world",
      loading: true,
      committing: true,
      defaultSummary: "Update a.txt",
    });

    useStagingStore.getState().clear();
    const state = useStagingStore.getState();
    expect(state.staged).toEqual([]);
    expect(state.unstaged).toEqual([]);
    expect(state.summary).toBe("");
    expect(state.description).toBe("");
    expect(state.loading).toBe(false);
    expect(state.committing).toBe(false);
    expect(state.defaultSummary).toBe("");
  });

  it("stageFile calls stageFiles IPC and refreshes", async () => {
    mockStageFiles.mockResolvedValue({ status: "ok", data: null });
    mockGetStatus.mockResolvedValue({
      status: "ok",
      data: { staged: [{ path: "a.txt", status: "Added" }], unstaged: [] },
    });

    await useStagingStore.getState().stageFile("/repo", "a.txt");

    expect(mockStageFiles).toHaveBeenCalledWith("/repo", ["a.txt"]);
    expect(mockGetStatus).toHaveBeenCalled();
  });

  it("unstageFile calls unstageFiles IPC and refreshes", async () => {
    mockUnstageFiles.mockResolvedValue({ status: "ok", data: null });
    mockGetStatus.mockResolvedValue({
      status: "ok",
      data: { staged: [], unstaged: [{ path: "a.txt", status: "Modified" }] },
    });

    await useStagingStore.getState().unstageFile("/repo", "a.txt");

    expect(mockUnstageFiles).toHaveBeenCalledWith("/repo", ["a.txt"]);
    expect(mockGetStatus).toHaveBeenCalled();
  });

  it("stageAll stages all unstaged files", async () => {
    useStagingStore.setState({
      unstaged: [
        { path: "a.txt", status: "Modified" },
        { path: "b.txt", status: "Untracked" },
      ],
    });

    mockStageFiles.mockResolvedValue({ status: "ok", data: null });
    mockGetStatus.mockResolvedValue({
      status: "ok",
      data: { staged: [{ path: "a.txt", status: "Modified" }, { path: "b.txt", status: "Added" }], unstaged: [] },
    });

    await useStagingStore.getState().stageAll("/repo");

    expect(mockStageFiles).toHaveBeenCalledWith("/repo", ["a.txt", "b.txt"]);
  });

  it("unstageAll unstages all staged files", async () => {
    useStagingStore.setState({
      staged: [
        { path: "a.txt", status: "Modified" },
        { path: "b.txt", status: "Added" },
      ],
    });

    mockUnstageFiles.mockResolvedValue({ status: "ok", data: null });
    mockGetStatus.mockResolvedValue({
      status: "ok",
      data: { staged: [], unstaged: [{ path: "a.txt", status: "Modified" }, { path: "b.txt", status: "Untracked" }] },
    });

    await useStagingStore.getState().unstageAll("/repo");

    expect(mockUnstageFiles).toHaveBeenCalledWith("/repo", ["a.txt", "b.txt"]);
  });
});
