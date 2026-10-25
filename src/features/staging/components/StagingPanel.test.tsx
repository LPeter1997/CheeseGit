import { describe, it, expect, vi, beforeEach } from "vitest";
import { fireEvent, render, screen } from "@testing-library/react";
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

describe("StagingPanel — file filter", () => {
  beforeEach(() => {
    resetStore();
    useStagingStore.setState({
      staged: [
        { path: "src/App.tsx", status: "Modified" },
        { path: "README.md", status: "Modified" },
      ],
      unstaged: [
        { path: "src/features/StagingPanel.tsx", status: "Modified" },
        { path: "package.json", status: "Modified" },
      ],
    });
    vi.clearAllMocks();
  });

  it("filters staged and unstaged files case-insensitively", () => {
    render(<StagingPanel repoPath="/repo" currentBranch="main" />);

    fireEvent.change(screen.getByTestId("staging-filter-input"), {
      target: { value: "tsx" },
    });

    expect(screen.getByText("src/App.tsx")).toBeInTheDocument();
    expect(screen.getByText("src/features/StagingPanel.tsx")).toBeInTheDocument();
    expect(screen.queryByText("README.md")).not.toBeInTheDocument();
    expect(screen.queryByText("package.json")).not.toBeInTheDocument();

    fireEvent.change(screen.getByTestId("staging-filter-input"), {
      target: { value: "readme" },
    });
    expect(screen.getByText("README.md")).toBeInTheDocument();
  });

  it("shows empty results and clears the filter with Escape or the clear button", () => {
    render(<StagingPanel repoPath="/repo" currentBranch="main" />);
    const input = screen.getByTestId("staging-filter-input") as HTMLInputElement;

    fireEvent.change(input, { target: { value: "missing" } });
    expect(screen.getByText("No unstaged files match your filter.")).toBeInTheDocument();
    expect(screen.getByText("No staged files match your filter.")).toBeInTheDocument();

    fireEvent.keyDown(input, { key: "Escape" });
    expect(input.value).toBe("");
    expect(screen.getAllByTestId("file-row")).toHaveLength(4);

    fireEvent.change(input, { target: { value: "json" } });
    fireEvent.click(screen.getByTestId("staging-filter-clear"));
    expect(input.value).toBe("");
    expect(screen.queryByTestId("staging-filter-clear")).not.toBeInTheDocument();
  });
});
