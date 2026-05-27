import { describe, it, expect, vi, beforeEach } from "vitest";
import { useStagingStore } from "./store";

vi.mock("../../ipc/bindings", () => ({
  commands: {
    getStatus: vi.fn(),
    commit: vi.fn(),
    stageFiles: vi.fn(),
    unstageFiles: vi.fn(),
    getDiffStats: vi.fn().mockResolvedValue({ status: "ok", data: [] }),
  },
}));

vi.mock("../../shared/stores/alerts", () => ({
  useAlertStore: {
    getState: () => ({ addAlert: vi.fn() }),
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
    emptyCommitMode: false,
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
    expect(mockCommit).toHaveBeenCalledWith("/repo", "my commit", "", false);
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

    expect(mockCommit).toHaveBeenCalledWith("/repo", "Update file.txt", "", false);
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
    // Pre-populate store with unstaged file
    useStagingStore.setState({
      unstaged: [{ path: "a.txt", status: "Modified" }],
    });

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
    // Pre-populate store with staged file
    useStagingStore.setState({
      staged: [{ path: "a.txt", status: "Modified" }],
    });

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

  // ── Empty commit mode tests ─────────────────────────────────────

  it("enableEmptyCommit sets emptyCommitMode to true", () => {
    useStagingStore.getState().enableEmptyCommit();
    expect(useStagingStore.getState().emptyCommitMode).toBe(true);
  });

  it("commit in empty mode passes allowEmpty=true", async () => {
    useStagingStore.setState({
      staged: [],
      summary: "empty",
      emptyCommitMode: true,
    });

    mockCommit.mockResolvedValue({ status: "ok", data: null });
    mockGetStatus.mockResolvedValue({
      status: "ok",
      data: { staged: [], unstaged: [] },
    });

    const ok = await useStagingStore.getState().commit("/repo");
    expect(ok).toBe(true);
    expect(mockCommit).toHaveBeenCalledWith("/repo", "empty", "", true);
  });

  it("commit resets emptyCommitMode on success", async () => {
    useStagingStore.setState({
      staged: [],
      summary: "empty",
      emptyCommitMode: true,
    });

    mockCommit.mockResolvedValue({ status: "ok", data: null });
    mockGetStatus.mockResolvedValue({
      status: "ok",
      data: { staged: [], unstaged: [] },
    });

    await useStagingStore.getState().commit("/repo");
    expect(useStagingStore.getState().emptyCommitMode).toBe(false);
  });

  it("commit returns false when no staged files and not in empty commit mode", async () => {
    useStagingStore.setState({
      staged: [],
      summary: "msg",
      emptyCommitMode: false,
    });

    const ok = await useStagingStore.getState().commit("/repo");
    expect(ok).toBe(false);
    expect(mockCommit).not.toHaveBeenCalled();
  });

  it("fetchStatus cancels emptyCommitMode when changes appear", async () => {
    useStagingStore.setState({ emptyCommitMode: true });

    mockGetStatus.mockResolvedValue({
      status: "ok",
      data: {
        staged: [{ path: "a.txt", status: "Modified" }],
        unstaged: [],
      },
    });

    await useStagingStore.getState().fetchStatus("/repo");
    expect(useStagingStore.getState().emptyCommitMode).toBe(false);
  });

  it("fetchStatus keeps emptyCommitMode when still no changes", async () => {
    useStagingStore.setState({ emptyCommitMode: true });

    mockGetStatus.mockResolvedValue({
      status: "ok",
      data: { staged: [], unstaged: [] },
    });

    await useStagingStore.getState().fetchStatus("/repo");
    expect(useStagingStore.getState().emptyCommitMode).toBe(true);
  });

  it("fetchStatus keeps emptyCommitMode when same status is re-fetched", async () => {
    // Simulate periodic refresh returning identical data
    useStagingStore.setState({
      emptyCommitMode: true,
      staged: [{ path: "a.txt", status: "Modified" }],
      unstaged: [{ path: "b.txt", status: "Untracked" }],
    });

    mockGetStatus.mockResolvedValue({
      status: "ok",
      data: {
        staged: [{ path: "a.txt", status: "Modified" }],
        unstaged: [{ path: "b.txt", status: "Untracked" }],
      },
    });

    await useStagingStore.getState().fetchStatus("/repo");
    expect(useStagingStore.getState().emptyCommitMode).toBe(true);
  });

  it("stageFile cancels emptyCommitMode", async () => {
    useStagingStore.setState({ emptyCommitMode: true });

    mockStageFiles.mockResolvedValue({ status: "ok", data: null });
    mockGetStatus.mockResolvedValue({
      status: "ok",
      data: { staged: [], unstaged: [] },
    });

    await useStagingStore.getState().stageFile("/repo", "a.txt");
    expect(useStagingStore.getState().emptyCommitMode).toBe(false);
  });

  it("unstageFile cancels emptyCommitMode", async () => {
    useStagingStore.setState({ emptyCommitMode: true });

    mockUnstageFiles.mockResolvedValue({ status: "ok", data: null });
    mockGetStatus.mockResolvedValue({
      status: "ok",
      data: { staged: [], unstaged: [] },
    });

    await useStagingStore.getState().unstageFile("/repo", "a.txt");
    expect(useStagingStore.getState().emptyCommitMode).toBe(false);
  });

  it("stageAll cancels emptyCommitMode", async () => {
    useStagingStore.setState({
      emptyCommitMode: true,
      unstaged: [{ path: "a.txt", status: "Modified" }],
    });

    mockStageFiles.mockResolvedValue({ status: "ok", data: null });
    mockGetStatus.mockResolvedValue({
      status: "ok",
      data: { staged: [{ path: "a.txt", status: "Modified" }], unstaged: [] },
    });

    await useStagingStore.getState().stageAll("/repo");
    expect(useStagingStore.getState().emptyCommitMode).toBe(false);
  });

  it("unstageAll cancels emptyCommitMode", async () => {
    useStagingStore.setState({
      emptyCommitMode: true,
      staged: [{ path: "a.txt", status: "Modified" }],
    });

    mockUnstageFiles.mockResolvedValue({ status: "ok", data: null });
    mockGetStatus.mockResolvedValue({
      status: "ok",
      data: { staged: [], unstaged: [{ path: "a.txt", status: "Modified" }] },
    });

    await useStagingStore.getState().unstageAll("/repo");
    expect(useStagingStore.getState().emptyCommitMode).toBe(false);
  });

  it("clear resets emptyCommitMode", () => {
    useStagingStore.setState({ emptyCommitMode: true });
    useStagingStore.getState().clear();
    expect(useStagingStore.getState().emptyCommitMode).toBe(false);
  });

  // ── Optimistic update rollback tests ─────────────────────────────

  it("stageFile rolls back optimistic update on error", async () => {
    useStagingStore.setState({
      staged: [],
      unstaged: [{ path: "a.txt", status: "Modified" }],
      initialized: true,
    });

    mockStageFiles.mockResolvedValue({
      status: "error",
      error: { Git: "staging failed", Io: undefined, Other: undefined },
    });

    await useStagingStore.getState().stageFile("/repo", "a.txt");

    const state = useStagingStore.getState();
    // Should rollback: a.txt back in unstaged, not in staged
    expect(state.unstaged).toEqual([{ path: "a.txt", status: "Modified" }]);
    expect(state.staged).toEqual([]);
  });

  it("unstageFile rolls back optimistic update on error", async () => {
    useStagingStore.setState({
      staged: [{ path: "b.txt", status: "Added" }],
      unstaged: [],
      initialized: true,
    });

    mockUnstageFiles.mockResolvedValue({
      status: "error",
      error: { Git: "unstaging failed", Io: undefined, Other: undefined },
    });

    await useStagingStore.getState().unstageFile("/repo", "b.txt");

    const state = useStagingStore.getState();
    // Should rollback: b.txt back in staged, not in unstaged
    expect(state.staged).toEqual([{ path: "b.txt", status: "Added" }]);
    expect(state.unstaged).toEqual([]);
  });

  it("stageAll rolls back optimistic update on error", async () => {
    useStagingStore.setState({
      staged: [{ path: "existing.txt", status: "Modified" }],
      unstaged: [
        { path: "a.txt", status: "Modified" },
        { path: "b.txt", status: "Untracked" },
      ],
      initialized: true,
    });

    mockStageFiles.mockResolvedValue({
      status: "error",
      error: { Git: "staging failed", Io: undefined, Other: undefined },
    });

    await useStagingStore.getState().stageAll("/repo");

    const state = useStagingStore.getState();
    // Should rollback to original state
    expect(state.staged).toEqual([{ path: "existing.txt", status: "Modified" }]);
    expect(state.unstaged).toEqual([
      { path: "a.txt", status: "Modified" },
      { path: "b.txt", status: "Untracked" },
    ]);
  });

  it("unstageAll rolls back optimistic update on error", async () => {
    useStagingStore.setState({
      staged: [
        { path: "a.txt", status: "Modified" },
        { path: "b.txt", status: "Added" },
      ],
      unstaged: [{ path: "existing.txt", status: "Untracked" }],
      initialized: true,
    });

    mockUnstageFiles.mockResolvedValue({
      status: "error",
      error: { Git: "unstaging failed", Io: undefined, Other: undefined },
    });

    await useStagingStore.getState().unstageAll("/repo");

    const state = useStagingStore.getState();
    // Should rollback to original state
    expect(state.staged).toEqual([
      { path: "a.txt", status: "Modified" },
      { path: "b.txt", status: "Added" },
    ]);
    expect(state.unstaged).toEqual([{ path: "existing.txt", status: "Untracked" }]);
  });
});
