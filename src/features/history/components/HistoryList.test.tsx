import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, fireEvent, screen } from "@testing-library/react";
import { HistoryList } from "./HistoryList";
import { useHistoryStore } from "../store";

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
    const firstButton = rows[0].querySelector("button");
    const secondButton = rows[1].querySelector("button");
    expect(firstButton).not.toBeNull();
    expect(secondButton).not.toBeNull();

    fireEvent.click(firstButton!);
    fireEvent.click(secondButton!, { ctrlKey: true });

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
    const firstButton = rows[0].querySelector("button");
    const secondButton = rows[1].querySelector("button");
    expect(firstButton).not.toBeNull();
    expect(secondButton).not.toBeNull();

    fireEvent.click(firstButton!);
    fireEvent.click(secondButton!, { ctrlKey: true });
    fireEvent.click(screen.getByTestId("history-cherry-pick-button"));

    expect(onCherryPick).toHaveBeenCalledTimes(1);
    expect(onCherryPick).toHaveBeenCalledWith(["def456", "abc123"]);
  });
});
