import { describe, expect, it, vi } from "vitest";
import { render, waitFor } from "@testing-library/react";
import { LeftPanel } from "./LeftPanel";
import { useStashStore } from "../../stash/store";

vi.mock("../../history", () => ({
  HistoryList: () => <div data-testid="history-panel" />,
}));

vi.mock("../../staging", () => ({
  StagingPanel: () => <div data-testid="staging-panel" />,
}));

vi.mock("../../stash", () => ({
  StashList: () => <div data-testid="stash-panel" />,
}));

describe("LeftPanel", () => {
  it("switches back to staging when stash tab is active but stash list becomes empty", async () => {
    useStashStore.setState({ stashes: [] });
    const onTabChange = vi.fn();

    render(
      <LeftPanel
        repoPath="/repo"
        currentBranch="main"
        browsingHistory={false}
        activeTab="stash"
        onTabChange={onTabChange}
      />,
    );

    await waitFor(() => {
      expect(onTabChange).toHaveBeenCalledWith("staging");
    });
  });

  it("does not force switch when stash entries exist", async () => {
    useStashStore.setState({
      stashes: [
        {
          index: 0,
          stash_ref: "stash@{0}",
          message: "On main: test",
          timestamp: "2026-01-01T00:00:00Z",
          author: "Dev",
          hash: "abc123",
          short_hash: "abc123",
        },
      ],
    });

    const onTabChange = vi.fn();

    render(
      <LeftPanel
        repoPath="/repo"
        currentBranch="main"
        browsingHistory={false}
        activeTab="stash"
        onTabChange={onTabChange}
      />,
    );

    await waitFor(() => {
      expect(onTabChange).not.toHaveBeenCalled();
    });
  });
});
