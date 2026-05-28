import { describe, it, expect, vi, beforeEach } from "vitest";
import { fireEvent, render, screen, waitFor } from "@testing-library/react";
import { CherryPickBranchPicker } from "./CherryPickBranchPicker";

vi.mock("../../../ipc/bindings", () => ({
  commands: {
    listBranches: vi.fn(),
  },
}));

import { commands } from "../../../ipc/bindings";

const mockListBranches = vi.mocked(commands.listBranches);

describe("CherryPickBranchPicker", () => {
  beforeEach(() => {
    vi.clearAllMocks();
    mockListBranches.mockResolvedValue({
      status: "ok",
      data: [
        { name: "main", is_current: true, last_commit_date: "2026-01-01T00:00:00Z" },
        { name: "feature", is_current: false, last_commit_date: "2026-01-01T00:00:00Z" },
      ],
    });
  });

  it("selects an existing branch", async () => {
    const onSelect = vi.fn();
    render(
      <CherryPickBranchPicker
        repoPath="/repo"
        selectedCount={2}
        onSelect={onSelect}
        onClose={() => {}}
      />,
    );

    await waitFor(() => expect(screen.getByTestId("cherry-pick-branch-row-feature")).toBeInTheDocument());
    fireEvent.click(screen.getByTestId("cherry-pick-branch-row-feature"));

    expect(onSelect).toHaveBeenCalledWith("feature", false);
  });

  it("creates and selects a new branch from search", async () => {
    const onSelect = vi.fn();
    render(
      <CherryPickBranchPicker
        repoPath="/repo"
        selectedCount={1}
        onSelect={onSelect}
        onClose={() => {}}
      />,
    );

    await waitFor(() => expect(screen.getByTestId("cherry-pick-branch-search")).toBeInTheDocument());
    fireEvent.change(screen.getByTestId("cherry-pick-branch-search"), {
      target: { value: "release/cp" },
    });

    fireEvent.click(screen.getByTestId("cherry-pick-create-branch-button"));
    expect(onSelect).toHaveBeenCalledWith("release/cp", true);
  });
});
