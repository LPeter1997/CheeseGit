import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen } from "@testing-library/react";
import { MergeConflictDialog } from "./components/MergeConflictDialog";
import { useMergeStore } from "./store";

vi.mock("../../ipc/bindings", () => ({
  commands: {
    mergeBranch: vi.fn(),
    mergeAbort: vi.fn(),
    getConflictCounts: vi.fn().mockResolvedValue({ status: "ok", data: [] }),
    resolveConflict: vi.fn(),
    openInMergeTool: vi.fn(),
    mergeContinue: vi.fn(),
    watchRepo: vi.fn().mockResolvedValue({ status: "ok", data: null }),
    unwatchRepo: vi.fn().mockResolvedValue({ status: "ok", data: null }),
  },
}));

vi.mock("@tauri-apps/api/event", () => ({
  listen: vi.fn().mockResolvedValue(() => {}),
}));

vi.mock("../../shared/stores/alerts", () => ({
  useAlertStore: Object.assign(
    (selector: (s: Record<string, unknown>) => unknown) => selector({ addAlert: vi.fn() }),
    { getState: () => ({ addAlert: vi.fn() }), subscribe: () => () => {} },
  ),
}));

function resetStore() {
  useMergeStore.setState({
    merging: false,
    incomingBranch: "",
    conflictFiles: [],
    resolutions: {},
    resolvedExternally: new Set(),
    loading: false,
  });
}

describe("MergeConflictDialog", () => {
  beforeEach(() => {
    resetStore();
    vi.clearAllMocks();
  });

  it("renders nothing when not merging", () => {
    const { container } = render(
      <MergeConflictDialog repoPath="/repo" />,
    );
    expect(container.firstChild).toBeNull();
  });

  it("shows conflict dialog when merging with conflicts", () => {
    useMergeStore.setState({
      merging: true,
      incomingBranch: "feature-branch",
      conflictFiles: [
        { path: "src/main.ts", conflict_count: 2 },
        { path: "README.md", conflict_count: 1 },
      ],
      resolutions: { "src/main.ts": null, "README.md": null },
    });

    render(<MergeConflictDialog repoPath="/repo" />);

    expect(screen.getByText("Merge Conflicts")).toBeInTheDocument();
    expect(screen.getByText("feature-branch")).toBeInTheDocument();
  });

  it("shows pill buttons for each file", () => {
    useMergeStore.setState({
      merging: true,
      incomingBranch: "feature",
      conflictFiles: [{ path: "file.txt", conflict_count: 3 }],
      resolutions: { "file.txt": null },
    });

    render(<MergeConflictDialog repoPath="/repo" />);

    expect(screen.getByText("file.txt")).toBeInTheDocument();
    expect(screen.getByText("3")).toBeInTheDocument(); // conflict count badge
    expect(screen.getByText("Current")).toBeInTheDocument();
    expect(screen.getByText("Incoming")).toBeInTheDocument();
    expect(screen.getByText("Both")).toBeInTheDocument();
    expect(screen.getByText("Edit")).toBeInTheDocument();
  });

  it("shows resolved state for externally resolved files", () => {
    useMergeStore.setState({
      merging: true,
      incomingBranch: "feature",
      conflictFiles: [],
      resolutions: { "file.txt": null },
      resolvedExternally: new Set(["file.txt"]),
    });

    render(<MergeConflictDialog repoPath="/repo" />);

    expect(screen.getByText("Resolved")).toBeInTheDocument();
  });

  it("disables Complete Merge when not all files are handled", () => {
    useMergeStore.setState({
      merging: true,
      incomingBranch: "feature",
      conflictFiles: [{ path: "file.txt", conflict_count: 1 }],
      resolutions: { "file.txt": null },
    });

    render(<MergeConflictDialog repoPath="/repo" />);

    const btn = screen.getByText("Complete Merge");
    expect(btn).toBeDisabled();
  });

  it("enables Complete Merge when all files have resolutions", () => {
    useMergeStore.setState({
      merging: true,
      incomingBranch: "feature",
      conflictFiles: [{ path: "file.txt", conflict_count: 1 }],
      resolutions: { "file.txt": "AcceptCurrent" },
    });

    render(<MergeConflictDialog repoPath="/repo" />);

    const btn = screen.getByText("Complete Merge");
    expect(btn).not.toBeDisabled();
  });

  it("has an abort merge button", () => {
    useMergeStore.setState({
      merging: true,
      incomingBranch: "feature",
      conflictFiles: [{ path: "file.txt", conflict_count: 1 }],
      resolutions: { "file.txt": null },
    });

    render(<MergeConflictDialog repoPath="/repo" />);

    expect(screen.getByText("Abort Merge")).toBeInTheDocument();
  });
});
