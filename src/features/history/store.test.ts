import { describe, it, expect, vi, beforeEach } from "vitest";
import { useHistoryStore } from "./store";

vi.mock("../../ipc/bindings", () => ({
  commands: {
    getCommitLog: vi.fn(),
    getBranchGraph: vi.fn(),
    listCommitFiles: vi.fn().mockResolvedValue({ status: "ok", data: [] }),
    getCommitFileStats: vi.fn().mockResolvedValue({ status: "ok", data: [] }),
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
            insertions: null,
            deletions: null,
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
            insertions: null,
            deletions: null,
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
            insertions: null,
            deletions: null,
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
      insertions: null as number | null,
      deletions: null as number | null,
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
            insertions: null,
            deletions: null,
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
          insertions: null as number | null,
          deletions: null as number | null,
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
      insertions: null as number | null,
      deletions: null as number | null,
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

  it("fetchGraph updates layout when branch refs move after pull (fast-forward)", async () => {
    // Simulate: user has fetched remote, so origin/master commit M is already
    // in the graph. Local master still points to commit A.
    const prePullData = {
      commits: [
        {
          hash: "merge_commit_M",
          short_hash: "merge_c",
          summary: "Merge PR #42",
          author: "Dev",
          timestamp: "2026-01-02T00:00:00Z",
          parents: ["commit_A", "feature_tip"],
          refs: ["origin/master"],  // only remote points here
          insertions: null,
          deletions: null,
        },
        {
          hash: "feature_tip",
          short_hash: "feature",
          summary: "feature work",
          author: "Dev",
          timestamp: "2026-01-01T12:00:00Z",
          parents: ["commit_A"],
          refs: ["feature"],
          insertions: null,
          deletions: null,
        },
        {
          hash: "commit_A",
          short_hash: "commit_",
          summary: "initial",
          author: "Dev",
          timestamp: "2026-01-01T00:00:00Z",
          parents: [],
          refs: ["master"],  // local master points here
          insertions: null,
          deletions: null,
        },
      ],
      branches: ["master", "origin/master", "feature"],
      local_only_commits: [],
    };

    mockGetBranchGraph.mockResolvedValue({ status: "ok", data: prePullData });
    await useHistoryStore.getState().fetchGraph("/repo", [], "origin", "master");

    const stateBeforePull = useHistoryStore.getState();
    expect(stateBeforePull.graphData).not.toBeNull();
    // master tip should be at commit_A
    const masterNodeBefore = stateBeforePull.graphLayout!.nodes.find(
      (n) => n.hash === "commit_A" && n.branch === "master"
    );
    expect(masterNodeBefore).toBeDefined();

    // After pull: same commits, same branches, same local_only — but refs change.
    // Local "master" moves from commit_A to merge_commit_M (fast-forward).
    const postPullData = {
      commits: [
        {
          hash: "merge_commit_M",
          short_hash: "merge_c",
          summary: "Merge PR #42",
          author: "Dev",
          timestamp: "2026-01-02T00:00:00Z",
          parents: ["commit_A", "feature_tip"],
          refs: ["master", "origin/master"],  // both point here now
          insertions: null,
          deletions: null,
        },
        {
          hash: "feature_tip",
          short_hash: "feature",
          summary: "feature work",
          author: "Dev",
          timestamp: "2026-01-01T12:00:00Z",
          parents: ["commit_A"],
          refs: ["feature"],
          insertions: null,
          deletions: null,
        },
        {
          hash: "commit_A",
          short_hash: "commit_",
          summary: "initial",
          author: "Dev",
          timestamp: "2026-01-01T00:00:00Z",
          parents: [],
          refs: [],  // no longer a branch tip
          insertions: null,
          deletions: null,
        },
      ],
      branches: ["master", "origin/master", "feature"],
      local_only_commits: [],
    };

    mockGetBranchGraph.mockResolvedValue({ status: "ok", data: postPullData });
    await useHistoryStore.getState().fetchGraph("/repo", [], "origin", "master");

    // The layout MUST have been recomputed — master should now be at merge_commit_M
    const stateAfterPull = useHistoryStore.getState();
    expect(stateAfterPull.graphData).not.toBeNull();
    expect(stateAfterPull.graphData!.commits[0].refs).toContain("master");
    // The graph layout's node for the master tip should now be at merge_commit_M
    const masterNodeAfter = stateAfterPull.graphLayout!.nodes.find(
      (n) => n.hash === "merge_commit_M" && n.branch === "master"
    );
    expect(masterNodeAfter).toBeDefined();
  });
});
