import { describe, it, expect, vi, beforeEach } from "vitest";
import { useHistoryStore } from "./store";

vi.mock("../../ipc/bindings", () => ({
  commands: {
    getCommitLog: vi.fn(),
  },
}));

import { commands } from "../../ipc/bindings";
const mockGetCommitLog = vi.mocked(commands.getCommitLog);

function resetStore() {
  useHistoryStore.setState({ commits: [], selectedIndex: -1, loading: false });
}

describe("useHistoryStore", () => {
  beforeEach(() => {
    resetStore();
    vi.clearAllMocks();
  });

  it("starts empty with no selection", () => {
    const state = useHistoryStore.getState();
    expect(state.commits).toEqual([]);
    expect(state.selectedIndex).toBe(-1);
    expect(state.loading).toBe(false);
  });

  it("fetchLog populates commits on success", async () => {
    mockGetCommitLog.mockResolvedValue({
      status: "ok",
      data: [
        {
          hash: "abc123def",
          short_hash: "abc123d",
          summary: "initial commit",
          author: "Test User",
          timestamp: "2026-01-01T00:00:00+00:00",
        },
      ],
    });

    await useHistoryStore.getState().fetchLog("/some/repo");

    const state = useHistoryStore.getState();
    expect(state.commits).toHaveLength(1);
    expect(state.commits[0].summary).toBe("initial commit");
    expect(state.loading).toBe(false);
    expect(mockGetCommitLog).toHaveBeenCalledWith("/some/repo", 200);
  });

  it("fetchLog clears commits on error", async () => {
    mockGetCommitLog.mockResolvedValue({
      status: "error",
      error: { Git: "not a repo" },
    });

    await useHistoryStore.getState().fetchLog("/bad/path");

    const state = useHistoryStore.getState();
    expect(state.commits).toEqual([]);
    expect(state.loading).toBe(false);
  });

  it("selectCommit updates selectedIndex", () => {
    useHistoryStore.getState().selectCommit(3);
    expect(useHistoryStore.getState().selectedIndex).toBe(3);
  });

  it("clear resets all state", () => {
    useHistoryStore.setState({
      commits: [
        {
          hash: "abc",
          short_hash: "ab",
          summary: "test",
          author: "a",
          timestamp: "t",
        },
      ],
      selectedIndex: 0,
      loading: true,
    });

    useHistoryStore.getState().clear();
    const state = useHistoryStore.getState();
    expect(state.commits).toEqual([]);
    expect(state.selectedIndex).toBe(-1);
    expect(state.loading).toBe(false);
  });
});
