import { describe, it, expect, vi, beforeEach } from "vitest";
import { useHistoryStore } from "./store";

vi.mock("../../ipc/bindings", () => ({
  commands: {
    getCommitLog: vi.fn(),
    getBranchGraph: vi.fn(),
    listCommitFiles: vi.fn().mockResolvedValue({ status: "ok", data: [] }),
  },
}));

import { commands } from "../../ipc/bindings";
const mockGetCommitLog = vi.mocked(commands.getCommitLog);
const mockGetBranchGraph = vi.mocked(commands.getBranchGraph);

function resetStore() {
  useHistoryStore.setState({
    commits: [],
    selectedHash: null,
    loading: false,
    graphData: null,
    graphLayout: null,
    hoveredBranch: null,
    visibleBranches: [],
    requiredBranches: [],
    allBranches: [],
    _layoutParams: null,
    graphMaxCommits: 500,
    hasMoreCommits: false,
    loadingMore: false,
  });
}

describe("useHistoryStore", () => {
  beforeEach(() => {
    resetStore();
    vi.clearAllMocks();
  });

  it("starts empty with no selection", () => {
    const state = useHistoryStore.getState();
    expect(state.commits).toEqual([]);
    expect(state.selectedHash).toBeNull();
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

  it("selectCommit updates selectedHash", () => {
    useHistoryStore.getState().selectCommit("abc123", "/tmp/repo");
    expect(useHistoryStore.getState().selectedHash).toBe("abc123");
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
      selectedHash: "abc",
      loading: true,
    });

    useHistoryStore.getState().clear();
    const state = useHistoryStore.getState();
    expect(state.commits).toEqual([]);
    expect(state.selectedHash).toBeNull();
    expect(state.loading).toBe(false);
  });

  it("fetchGraph calls getBranchGraph with max_commits=500", async () => {
    mockGetBranchGraph.mockResolvedValue({
      status: "ok",
      data: {
        commits: [
          {
            hash: "abc123",
            short_hash: "abc123d",
            summary: "initial",
            author: "Test",
            timestamp: "2026-01-01T00:00:00Z",
            parents: [],
            refs: ["main"],
          },
        ],
        branches: ["main"],
        local_only_commits: [],
      },
    });

    await useHistoryStore.getState().fetchGraph("/repo", [], null, "main");

    expect(mockGetBranchGraph).toHaveBeenCalledWith("/repo", [], null, 500);
  });

  it("fetchGraph populates graphData and graphLayout", async () => {
    mockGetBranchGraph.mockResolvedValue({
      status: "ok",
      data: {
        commits: [
          {
            hash: "abc123",
            short_hash: "abc123d",
            summary: "initial",
            author: "Test",
            timestamp: "2026-01-01T00:00:00Z",
            parents: [],
            refs: ["main"],
          },
        ],
        branches: ["main"],
        local_only_commits: [],
      },
    });

    await useHistoryStore.getState().fetchGraph("/repo", [], null, "main");

    const state = useHistoryStore.getState();
    expect(state.graphData).not.toBeNull();
    expect(state.graphData!.commits).toHaveLength(1);
    expect(state.graphLayout).not.toBeNull();
    expect(state.graphLayout!.nodes).toHaveLength(1);
    expect(state.allBranches).toEqual(["main"]);
  });

  it("fetchGraph sets required and visible branches", async () => {
    mockGetBranchGraph.mockResolvedValue({
      status: "ok",
      data: {
        commits: [
          {
            hash: "abc",
            short_hash: "abc",
            summary: "init",
            author: "Test",
            timestamp: "2026-01-01T00:00:00Z",
            parents: [],
            refs: ["main"],
          },
        ],
        branches: ["main"],
        local_only_commits: [],
      },
    });

    await useHistoryStore.getState().fetchGraph("/repo", [], null, "main");

    const state = useHistoryStore.getState();
    expect(state.requiredBranches).toContain("main");
    expect(state.visibleBranches).toContain("main");
  });

  it("fetchGraph sets hasMoreCommits when commits === maxCommits", async () => {
    // Create exactly 500 commits (the default max).
    const commits = Array.from({ length: 500 }, (_, i) => ({
      hash: `h${i}`,
      short_hash: `h${i}`.slice(0, 7),
      summary: `commit ${i}`,
      author: "Test",
      timestamp: "2026-01-01T00:00:00Z",
      parents: i < 499 ? [`h${i + 1}`] : [],
      refs: i === 0 ? ["main"] : [],
    }));

    mockGetBranchGraph.mockResolvedValue({
      status: "ok",
      data: { commits, branches: ["main"], local_only_commits: [] },
    });

    await useHistoryStore.getState().fetchGraph("/repo", [], null, "main");

    expect(useHistoryStore.getState().hasMoreCommits).toBe(true);
  });

  it("fetchGraph sets hasMoreCommits=false when commits < maxCommits", async () => {
    mockGetBranchGraph.mockResolvedValue({
      status: "ok",
      data: {
        commits: [
          {
            hash: "abc",
            short_hash: "abc",
            summary: "init",
            author: "Test",
            timestamp: "2026-01-01T00:00:00Z",
            parents: [],
            refs: ["main"],
          },
        ],
        branches: ["main"],
        local_only_commits: [],
      },
    });

    await useHistoryStore.getState().fetchGraph("/repo", [], null, "main");

    expect(useHistoryStore.getState().hasMoreCommits).toBe(false);
  });

  it("loadMoreGraph increases graphMaxCommits and fetches more", async () => {
    // Set up initial state as if fetchGraph was already called.
    useHistoryStore.setState({
      graphMaxCommits: 500,
      hasMoreCommits: true,
      _layoutParams: { currentBranch: "main", remote: "origin" },
      graphData: {
        commits: Array.from({ length: 500 }, (_, i) => ({
          hash: `h${i}`,
          short_hash: `h${i}`.slice(0, 7),
          summary: `commit ${i}`,
          author: "Test",
          timestamp: "2026-01-01T00:00:00Z",
          parents: i < 499 ? [`h${i + 1}`] : [],
          refs: i === 0 ? ["main"] : [],
        })),
        branches: ["main"],
        local_only_commits: [],
      },
      visibleBranches: ["main"],
      requiredBranches: ["main"],
      allBranches: ["main"],
    });

    // Mock the backend returning 1000 commits on re-fetch.
    const expandedCommits = Array.from({ length: 1000 }, (_, i) => ({
      hash: `h${i}`,
      short_hash: `h${i}`.slice(0, 7),
      summary: `commit ${i}`,
      author: "Test",
      timestamp: "2026-01-01T00:00:00Z",
      parents: i < 999 ? [`h${i + 1}`] : [],
      refs: i === 0 ? ["main"] : [],
    }));

    mockGetBranchGraph.mockResolvedValue({
      status: "ok",
      data: { commits: expandedCommits, branches: ["main"], local_only_commits: [] },
    });

    await useHistoryStore.getState().loadMoreGraph("/repo");

    const state = useHistoryStore.getState();
    expect(state.graphMaxCommits).toBe(1000);
    expect(state.graphLayout!.nodes).toHaveLength(1000);
    expect(state.hasMoreCommits).toBe(true); // 1000 === new max
    expect(state.loadingMore).toBe(false);
    expect(mockGetBranchGraph).toHaveBeenCalledWith("/repo", [], "origin", 1000);
  });

  it("loadMoreGraph does nothing when hasMoreCommits is false", async () => {
    useHistoryStore.setState({
      hasMoreCommits: false,
      _layoutParams: { currentBranch: "main", remote: null },
    });

    await useHistoryStore.getState().loadMoreGraph("/repo");

    expect(mockGetBranchGraph).not.toHaveBeenCalled();
  });

  it("loadMoreGraph does nothing while already loading", async () => {
    useHistoryStore.setState({
      hasMoreCommits: true,
      loadingMore: true,
      _layoutParams: { currentBranch: "main", remote: null },
    });

    await useHistoryStore.getState().loadMoreGraph("/repo");

    expect(mockGetBranchGraph).not.toHaveBeenCalled();
  });
});
