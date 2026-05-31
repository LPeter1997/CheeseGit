import { describe, it, expect } from "vitest";
import { render, screen } from "@testing-library/react";
import { BranchBar } from "./BranchBar";

// Minimal mock for commands used inside BranchBar
vi.mock("../../../ipc/bindings", () => ({
  commands: {
    listBranches: vi.fn().mockResolvedValue({ status: "ok", data: [] }),
    listRemoteBranches: vi.fn().mockResolvedValue({ status: "ok", data: [] }),
    deleteBranch: vi.fn(),
    deleteRemoteBranch: vi.fn(),
    getBranchDeleteInfo: vi.fn(),
    listRemotes: vi.fn().mockResolvedValue({ status: "ok", data: [] }),
    getTrackingStatus: vi.fn().mockResolvedValue({ status: "ok", data: null }),
    getRemoteBranchStatus: vi.fn().mockResolvedValue({ status: "ok", data: null }),
    push: vi.fn(),
    pull: vi.fn(),
    fetch: vi.fn(),
    publishBranch: vi.fn(),
  },
}));

vi.mock("../../history", () => {
  const store = (selector: (s: Record<string, unknown>) => unknown) =>
    selector({ graphLayout: null });
  store.getState = () => ({ graphLayout: null });
  store.subscribe = () => () => {};
  return { useHistoryStore: store };
});

vi.mock("../../../shared/stores/alerts", () => ({
  useAlertStore: Object.assign(
    (selector: (s: Record<string, unknown>) => unknown) => selector({ addAlert: () => {} }),
    { getState: () => ({ addAlert: () => {} }), subscribe: () => () => {} },
  ),
}));

import { vi } from "vitest";

describe("BranchBar — history browsing display", () => {
  it("shows branch name normally when not browsing history", () => {
    render(
      <BranchBar
        repoPath="/repo"
        currentBranch="main"
        browsingHistory={false}
        tracking={null}
        switching={false}
        panelWidth={280}
        onSwitch={() => {}}
        onCreate={() => {}}
        onMerge={() => {}}
        onRemoteComplete={() => {}}
      />,
    );
    expect(screen.getByText("main")).toBeInTheDocument();
    expect(screen.queryByText("(history)")).not.toBeInTheDocument();
  });

  it("shows previous branch name with (history) label when browsing history", () => {
    render(
      <BranchBar
        repoPath="/repo"
        currentBranch="feature/my-branch"
        browsingHistory={true}
        tracking={null}
        switching={false}
        panelWidth={280}
        onSwitch={() => {}}
        onCreate={() => {}}
        onMerge={() => {}}
        onRemoteComplete={() => {}}
      />,
    );
    expect(screen.getByText("feature/my-branch")).toBeInTheDocument();
    expect(screen.getByText("(history)")).toBeInTheDocument();
  });

  it("does not show (history) label when on a normal branch", () => {
    render(
      <BranchBar
        repoPath="/repo"
        currentBranch="develop"
        browsingHistory={false}
        tracking={null}
        switching={false}
        panelWidth={280}
        onSwitch={() => {}}
        onCreate={() => {}}
        onMerge={() => {}}
        onRemoteComplete={() => {}}
      />,
    );
    expect(screen.queryByText("(history)")).not.toBeInTheDocument();
  });
});
