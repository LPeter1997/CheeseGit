import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen, fireEvent, waitFor } from "@testing-library/react";
import { useRef } from "react";
import { BranchDropdown } from "./BranchDropdown";
import { clearCachedBranchList } from "../branchListCache";

const listBranches = vi.fn();
const listRemoteBranches = vi.fn();

vi.mock("../../../ipc/bindings", () => ({
  commands: {
    listBranches: (...args: unknown[]) => listBranches(...args),
    listRemoteBranches: (...args: unknown[]) => listRemoteBranches(...args),
  },
}));

vi.mock("../../history", () => {
  const state = {
    visibleBranches: [],
    requiredBranches: [],
    allBranches: [],
    toggleBranchVisibility: () => {},
    showAllBranches: () => {},
    hideNonRequired: () => {},
  };
  const store = (selector: (s: typeof state) => unknown) => selector(state);
  store.getState = () => state;
  store.subscribe = () => () => {};
  return { useHistoryStore: store };
});

function Wrapper(props: { onMerge: (name: string) => void }) {
  const toggleRef = useRef<HTMLButtonElement>(null);
  return (
    <>
      <button ref={toggleRef}>toggle</button>
      <BranchDropdown
        repoPath="/repo"
        currentBranch="main"
        toggleRef={toggleRef}
        onSelect={() => {}}
        onCreate={() => {}}
        onMerge={props.onMerge}
        onDelete={() => {}}
        onClose={() => {}}
      />
    </>
  );
}

describe("BranchDropdown — merge remote branch", () => {
  beforeEach(() => {
    clearCachedBranchList("/repo");
    listBranches.mockResolvedValue({ status: "ok", data: [] });
    listRemoteBranches.mockResolvedValue({
      status: "ok",
      data: [
        { name: "tech/feature", remote: "origin", last_commit_date: "2025-05-08T12:00:00Z" },
      ],
    });
  });

  it("passes the fully-qualified remote ref to onMerge", async () => {
    const onMerge = vi.fn();
    render(<Wrapper onMerge={onMerge} />);

    const mergeBtn = await screen.findByTestId("merge-remote-branch-tech/feature");
    fireEvent.click(mergeBtn);

    await waitFor(() => expect(onMerge).toHaveBeenCalledWith("origin/tech/feature"));
  });
});

describe("BranchDropdown — branch list caching", () => {
  beforeEach(() => {
    clearCachedBranchList("/repo");
    listBranches.mockReset();
    listRemoteBranches.mockReset();
  });

  it("shows a Loading… state on first open with no cache", async () => {
    let resolveLocal!: (v: unknown) => void;
    listBranches.mockReturnValue(new Promise((r) => { resolveLocal = r; }));
    listRemoteBranches.mockResolvedValue({ status: "ok", data: [] });

    render(<Wrapper onMerge={() => {}} />);

    // Nothing cached yet → spinner while the fetch is pending.
    expect(screen.getByText("Loading…")).toBeInTheDocument();

    resolveLocal({
      status: "ok",
      data: [{ name: "main", remote: null, last_commit_date: "2025-05-08T12:00:00Z" }],
    });
    await waitFor(() => expect(screen.queryByText("Loading…")).not.toBeInTheDocument());
  });

  it("renders cached branches immediately without a Loading… state on reopen", async () => {
    listBranches.mockResolvedValue({
      status: "ok",
      data: [{ name: "main", remote: null, last_commit_date: "2025-05-08T12:00:00Z" }],
    });
    listRemoteBranches.mockResolvedValue({ status: "ok", data: [] });

    // First open populates the cache.
    const first = render(<Wrapper onMerge={() => {}} />);
    await screen.findByTestId("branch-row-main");
    first.unmount();

    // Make the next fetch hang so we can observe the cached render alone.
    listBranches.mockReturnValue(new Promise(() => {}));

    render(<Wrapper onMerge={() => {}} />);
    // Cached branch shows instantly; no spinner even though the refetch is pending.
    expect(screen.getByTestId("branch-row-main")).toBeInTheDocument();
    expect(screen.queryByText("Loading…")).not.toBeInTheDocument();
  });
});
