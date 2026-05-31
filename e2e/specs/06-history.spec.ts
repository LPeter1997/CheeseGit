/**
 * E2E: History viewer — commit list, selection, diff display
 */
import {
  switchLeftPanel,
  getHistoryCommits,
  clickHistoryCommit,
  selectFirstCommitFile,
  isDiffVisible,
  waitForDiffVisible,
  setupTest,
  sleep,
} from "../helpers/app.js";

describe("History Viewer", () => {
  before(async () => {
    await setupTest();
  });

  it("switches to the history tab", async () => {
    await switchLeftPanel("history");
    const historyTab = await $("[data-testid='left-tab-history']");
    const cls = await historyTab.getAttribute("class");
    expect(cls).toContain("border-accent");
  });

  it("shows commit history from the test repo", async () => {
    const commits = await getHistoryCommits();
    expect(commits.length).toBeGreaterThanOrEqual(10);
  });

  it("shows known commit messages", async () => {
    const commits = await getHistoryCommits();
    // These commit messages are recent and should be visible in the viewport
    // (older commits may be off-screen due to virtualization)
    expect(commits.some((m) => m.includes("changelog"))).toBe(true);
    expect(commits.some((m) => m.includes("auth module"))).toBe(true);
    expect(commits.some((m) => m.includes("greeting"))).toBe(true);
  });

  it("clicking a commit shows its diff", async () => {
    await clickHistoryCommit(0);
    await selectFirstCommitFile();
    await waitForDiffVisible();
    expect(await isDiffVisible()).toBe(true);
  });

  it("can switch back to staging tab", async () => {
    await switchLeftPanel("staging");
    const stagingTab = await $("[data-testid='left-tab-staging']");
    const cls = await stagingTab.getAttribute("class");
    expect(cls).toContain("border-accent");
  });
});
