import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen } from "@testing-library/react";
import { StagingPanel } from "./StagingPanel";
import { useStagingStore } from "../store";

vi.mock("../../../ipc/bindings", () => ({
  commands: {
    getStatus: vi.fn().mockResolvedValue({ status: "ok", data: { staged: [], unstaged: [] } }),
    commit: vi.fn(),
    undoLastCommit: vi.fn(),
    stageFiles: vi.fn(),
    unstageFiles: vi.fn(),
    getDiffStats: vi.fn().mockResolvedValue({ status: "ok", data: [] }),
    getCommitLog: vi.fn(),
    getBranchGraph: vi.fn(),
    listCommitFiles: vi.fn().mockResolvedValue({ status: "ok", data: [] }),
    getCommitFileStats: vi.fn().mockResolvedValue({ status: "ok", data: [] }),
  },
}));

vi.mock("../../history", () => {
  const store = (selector: (s: Record<string, unknown>) => unknown) =>
    selector({ fetchLog: vi.fn() });
  store.getState = () => ({ fetchLog: vi.fn() });
  store.subscribe = () => () => {};
  return { useHistoryStore: store };
});

vi.mock("../../diff/store", () => {
  const state = { selectFile: vi.fn(), clearSelection: vi.fn(), selectedFile: null };
  const store = (selector: (s: typeof state) => unknown) => selector(state);
  store.getState = () => state;
  store.subscribe = () => () => {};
  return { useDiffStore: store };
});

vi.mock("../../diff/hooks/useDiffPrefetch", () => ({
  useDiffPrefetch: vi.fn(),
}));

function resetStore() {
  useStagingStore.setState({
    staged: [{ path: "file.txt", status: "Modified" }],
    unstaged: [],
    summary: "test commit",
    description: "",
    loading: false,
    committing: false,
    defaultSummary: "",
    initialized: true,
    emptyCommitMode: false,
    stagedStats: new Map(),
    unstagedStats: new Map(),
  });
}

describe("StagingPanel — browsing history", () => {
  beforeEach(() => {
    resetStore();
    vi.clearAllMocks();
  });

  it("enables commit button when not browsing history", () => {
    render(<StagingPanel repoPath="/repo" currentBranch="main" browsingHistory={false} />);
    const button = screen.getByRole("button", { name: /commit to main/i });
    expect(button).not.toBeDisabled();
  });

  it("disables commit button when browsing history", () => {
    render(<StagingPanel repoPath="/repo" currentBranch="HEAD" browsingHistory={true} />);
    const button = screen.getByRole("button", { name: /viewing history/i });
    expect(button).toBeDisabled();
  });

  it("shows appropriate tooltip when browsing history", () => {
    render(<StagingPanel repoPath="/repo" currentBranch="HEAD" browsingHistory={true} />);
    const button = screen.getByRole("button", { name: /viewing history/i });
    expect(button).toHaveAttribute(
      "title",
      "Cannot commit while viewing history — jump back to present or create a new branch",
    );
  });

  it("shows 'Viewing history' text on commit button when browsing history", () => {
    render(<StagingPanel repoPath="/repo" currentBranch="HEAD" browsingHistory={true} />);
    expect(screen.getByText("Viewing history")).toBeInTheDocument();
  });

  it("does not show more-actions button when no staged changes", () => {
    useStagingStore.setState({
      staged: [],
      unstaged: [],
      summary: "",
      description: "",
      defaultSummary: "",
      loading: false,
      committing: false,
      initialized: true,
      emptyCommitMode: false,
      stagedStats: new Map(),
      unstagedStats: new Map(),
    });

    render(<StagingPanel repoPath="/repo" currentBranch="main" browsingHistory={false} />);
    expect(screen.queryByTestId("commit-more-actions")).not.toBeInTheDocument();
  });
});
