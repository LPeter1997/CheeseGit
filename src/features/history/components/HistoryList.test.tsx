import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, fireEvent, screen } from "@testing-library/react";
import { HistoryList } from "./HistoryList";
import { useHistoryStore } from "../store";
import type { GraphLayout } from "../graph/layout";
import type { GraphCommit } from "../../../ipc/bindings";

vi.mock("../../../ipc/bindings", () => ({
  commands: {
    getCommitLog: vi.fn(),
    getBranchGraph: vi.fn(),
    listCommitFiles: vi.fn().mockResolvedValue({ status: "ok", data: [] }),
    getCommitFileStats: vi.fn().mockResolvedValue({ status: "ok", data: [] }),
  },
}));

function resetStore() {
  useHistoryStore.setState({
    commits: [
      { hash: "abc123", short_hash: "abc12", summary: "latest commit", author: "User", timestamp: "2026-01-02T00:00:00Z" },
      { hash: "def456", short_hash: "def45", summary: "older commit", author: "User", timestamp: "2026-01-01T00:00:00Z" },
    ],
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
    commitFiles: [],
    commitFilesLoading: false,
    commitFileStats: new Map(),
    selectedFilePath: null,
    selectedFileDiff: null,
    selectedFileContent: null,
    selectedFileDiffLoading: false,
  });
}

function withGraphLayout(commits: GraphCommit[]) {
  const graphLayout: GraphLayout = {
    lanes: [{ branch: "main", column: 0, color: "#0ea5e9" }],
    nodes: commits.map((commit, index) => ({
      row: index,
      column: 0,
      branch: "main",
      color: "#0ea5e9",
      isLocalOnly: false,
      hash: commit.hash,
    })),
    edges: [],
    columnCount: 2,
    rowHeight: 50,
    commits,
  };

  useHistoryStore.setState({
    commits,
    graphData: {
      commits,
      branches: ["main"],
      local_only_commits: [],
    },
    graphLayout,
  });
}

describe("HistoryList", () => {
  beforeEach(() => {
    resetStore();
    vi.clearAllMocks();
  });

  it("does not show 'Jump back to present' when not browsing history", () => {
    render(<HistoryList repoPath="/repo" browsingHistory={false} onJumpToPresent={() => {}} />);
    expect(screen.queryByText("← Jump back to present")).not.toBeInTheDocument();
  });

  it("shows 'Jump back to present' when browsing history", () => {
    const onJump = vi.fn();
    render(<HistoryList repoPath="/repo" browsingHistory={true} onJumpToPresent={onJump} />);
    expect(screen.getByText("← Jump back to present")).toBeInTheDocument();
  });

  it("calls onJumpToPresent when the link is clicked", () => {
    const onJump = vi.fn();
    render(<HistoryList repoPath="/repo" browsingHistory={true} onJumpToPresent={onJump} />);
    fireEvent.click(screen.getByText("← Jump back to present"));
    expect(onJump).toHaveBeenCalledTimes(1);
  });

  it("does not show 'Jump back to present' when browsing history but no callback", () => {
    render(<HistoryList repoPath="/repo" browsingHistory={true} />);
    expect(screen.queryByText("← Jump back to present")).not.toBeInTheDocument();
  });

  it("shows undo-last-commit button only for the head commit", () => {
    render(
      <HistoryList
        repoPath="/repo"
        browsingHistory={false}
        onUndoLastCommit={() => {}}
      />,
    );

    // Only one button should exist (latest commit only).
    expect(screen.getAllByTestId("undo-last-commit-button")).toHaveLength(1);
  });

  it("calls onUndoLastCommit when undo button is clicked", () => {
    const onUndo = vi.fn();
    render(
      <HistoryList
        repoPath="/repo"
        browsingHistory={false}
        onUndoLastCommit={onUndo}
      />,
    );

    fireEvent.click(screen.getByTestId("undo-last-commit-button"));
    expect(onUndo).toHaveBeenCalledTimes(1);
    expect(onUndo).toHaveBeenCalledWith("abc123");
  });

  it("hides undo-last-commit button while browsing history", () => {
    render(
      <HistoryList
        repoPath="/repo"
        browsingHistory={true}
        onUndoLastCommit={() => {}}
      />,
    );

    expect(screen.queryByTestId("undo-last-commit-button")).not.toBeInTheDocument();
  });

  it("supports ctrl multi-select and shows cherry-pick action", () => {
    render(
      <HistoryList
        repoPath="/repo"
        browsingHistory={false}
        onCherryPickCommits={() => {}}
      />,
    );

    const rows = screen.getAllByTestId("history-row");
    fireEvent.click(rows[0]);
    fireEvent.click(rows[1], { ctrlKey: true });

    expect(screen.getByTestId("history-cherry-pick-button")).toHaveTextContent("Cherry-pick 2 commits");
  });

  it("cherry-pick callback receives hashes in oldest-to-newest order", () => {
    const onCherryPick = vi.fn();
    render(
      <HistoryList
        repoPath="/repo"
        browsingHistory={false}
        onCherryPickCommits={onCherryPick}
      />,
    );

    const rows = screen.getAllByTestId("history-row");
    fireEvent.click(rows[0]);
    fireEvent.click(rows[1], { ctrlKey: true });
    fireEvent.click(screen.getByTestId("history-cherry-pick-button"));

    expect(onCherryPick).toHaveBeenCalledTimes(1);
    expect(onCherryPick).toHaveBeenCalledWith(["def456", "abc123"]);
  });

  it("filters commits by typed search across commit fields", () => {
    render(<HistoryList repoPath="/repo" browsingHistory={false} />);

    const search = screen.getByTestId("history-search-input");
    fireEvent.change(search, { target: { value: "older" } });

    expect(screen.getByText("older commit")).toBeInTheDocument();
    expect(screen.queryByText("latest commit")).not.toBeInTheDocument();

    fireEvent.change(search, { target: { value: "abc123" } });
    expect(screen.getByText("latest commit")).toBeInTheDocument();
    expect(screen.queryByText("older commit")).not.toBeInTheDocument();

    fireEvent.change(search, { target: { value: "does-not-exist" } });
    expect(screen.getByText("No commits match your search.")).toBeInTheDocument();
  });

  it("clears search when clear button is clicked", () => {
    render(<HistoryList repoPath="/repo" browsingHistory={false} />);

    const search = screen.getByTestId("history-search-input") as HTMLInputElement;
    fireEvent.change(search, { target: { value: "older" } });

    expect(search.value).toBe("older");
    fireEvent.click(screen.getByTestId("history-search-clear"));
    expect(search.value).toBe("");
    expect(screen.queryByTestId("history-search-clear")).not.toBeInTheDocument();
  });

  it("clears search when escape is pressed in the search input", () => {
    render(<HistoryList repoPath="/repo" browsingHistory={false} />);

    const search = screen.getByTestId("history-search-input") as HTMLInputElement;
    fireEvent.change(search, { target: { value: "older" } });
    fireEvent.keyDown(search, { key: "Escape" });

    expect(search.value).toBe("");
  });

  it("scrolls to selected commit when it is out of view", () => {
    const many = Array.from({ length: 30 }, (_, i) => ({
      hash: `hash-${i}`,
      short_hash: `h${i}`,
      summary: `commit ${i}`,
      author: "User",
      timestamp: "2026-01-01T00:00:00Z",
    }));

    useHistoryStore.setState({
      commits: many,
      selectedHash: "hash-20",
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
      commitFiles: [],
      commitFilesLoading: false,
      commitFileStats: new Map(),
      selectedFilePath: null,
      selectedFileDiff: null,
      selectedFileContent: null,
      selectedFileDiffLoading: false,
    });

    const { container } = render(<HistoryList repoPath="/repo" browsingHistory={false} />);
    const scroller = container.firstElementChild as HTMLDivElement;

    expect(scroller.scrollTop).toBeGreaterThan(0);
  });

  it("hides graph while searching but keeps graph column spacing", () => {
    const graphCommits: GraphCommit[] = [
      {
        hash: "abc123",
        short_hash: "abc12",
        summary: "latest commit",
        author: "User",
        timestamp: "2026-01-02T00:00:00Z",
        parents: ["def456"],
        refs: ["main", "origin/main"],
        insertions: 5,
        deletions: 2,
      },
      {
        hash: "def456",
        short_hash: "def45",
        summary: "older commit",
        author: "User",
        timestamp: "2026-01-01T00:00:00Z",
        parents: [],
        refs: [],
        insertions: 1,
        deletions: 0,
      },
    ];
    withGraphLayout(graphCommits);

    render(<HistoryList repoPath="/repo" browsingHistory={false} />);

    expect(screen.getByTestId("history-graph-overlay")).toBeInTheDocument();

    const firstRow = screen.getAllByTestId("history-row")[0];
    expect(firstRow).toHaveStyle("padding-left: 44px");

    fireEvent.change(screen.getByTestId("history-search-input"), { target: { value: "latest" } });

    expect(screen.queryByTestId("history-graph-overlay")).not.toBeInTheDocument();
    expect(firstRow).toHaveStyle("padding-left: 44px");
  });
});
