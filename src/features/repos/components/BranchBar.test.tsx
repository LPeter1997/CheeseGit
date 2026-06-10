import { describe, it, expect } from "vitest";
import { render, screen, fireEvent } from "@testing-library/react";
import { BranchBar } from "./BranchBar";
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

describe("BranchBar — copy current branch name", () => {
  it("copies the current branch name to the clipboard and shows feedback", async () => {
    const writeText = vi.fn().mockResolvedValue(undefined);
    Object.assign(navigator, { clipboard: { writeText } });

    render(
      <BranchBar
        repoPath="/repo"
        currentBranch="feature/copy-me"
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

    const copyButton = screen.getByTestId("copy-branch-name");
    expect(copyButton).toHaveAttribute("title", "Copy current branch name");

    fireEvent.click(copyButton);

    expect(writeText).toHaveBeenCalledWith("feature/copy-me");
    // Feedback: the button title flips to "Copied!".
    expect(copyButton).toHaveAttribute("title", "Copied!");
  });

  it("disables the copy button when there is no current branch", () => {
    render(
      <BranchBar
        repoPath="/repo"
        currentBranch={null}
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
    expect(screen.getByTestId("copy-branch-name")).toBeDisabled();
  });
});
