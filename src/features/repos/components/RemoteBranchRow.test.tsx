import { describe, it, expect, vi } from "vitest";
import { render, screen, fireEvent } from "@testing-library/react";
import { RemoteBranchRow } from "./RemoteBranchRow";
import { type RemoteBranchInfo } from "../../../ipc/bindings";

const branch: RemoteBranchInfo = {
  name: "tech/feature",
  remote: "origin",
  last_commit_date: "2025-05-08T12:00:00Z",
};

describe("RemoteBranchRow", () => {
  it("checks out the branch when the name is clicked", () => {
    const onSelect = vi.fn();
    render(<RemoteBranchRow branch={branch} onSelect={onSelect} onMerge={() => {}} />);
    fireEvent.click(screen.getByText("tech/feature"));
    expect(onSelect).toHaveBeenCalledTimes(1);
  });

  it("merges the remote branch without checking it out", () => {
    const onSelect = vi.fn();
    const onMerge = vi.fn();
    render(<RemoteBranchRow branch={branch} onSelect={onSelect} onMerge={onMerge} />);
    fireEvent.click(screen.getByTestId("merge-remote-branch-tech/feature"));
    expect(onMerge).toHaveBeenCalledTimes(1);
    expect(onSelect).not.toHaveBeenCalled();
  });
});
