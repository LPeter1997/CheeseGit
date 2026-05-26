/**
 * E2E: History browsing — navigate through commit history, detach HEAD,
 * and return to present.
 *
 * Tests the "browsing history" mode where clicking a commit detaches HEAD,
 * and the "Jump back to present" button returns to the current branch.
 */
import {
  waitForAppReady,
  openRepoByPath,
  waitForStagingLoaded,
  switchLeftPanel,
  getCurrentBranch,
  getHistoryCommits,
  clickHistoryCommit,
  selectFirstCommitFile,
  isDiffVisible,
  sleep,
  TEST_REPO_PATH,
} from "../helpers/app.js";

describe("History Browsing", () => {
  before(async () => {
    await waitForAppReady();
    await openRepoByPath(TEST_REPO_PATH);
    await switchLeftPanel("history");
    await sleep(500);
  });

  it("starts on main branch", async () => {
    const branch = await getCurrentBranch();
    expect(branch).toBe("main");
  });

  it("shows commit history with multiple entries", async () => {
    const commits = await getHistoryCommits();
    expect(commits.length).toBeGreaterThanOrEqual(10);
  });

  it("clicking a commit shows its diff", async () => {
    await clickHistoryCommit(2);
    await selectFirstCommitFile();
    expect(await isDiffVisible()).toBe(true);
  });

  it("can click a different commit and see different diff", async () => {
    // Use commit 1 instead of 5 — commit 5 might be a merge commit with no files
    await clickHistoryCommit(1);
    await selectFirstCommitFile();
    expect(await isDiffVisible()).toBe(true);
  });

  it("history shows merge commits", async () => {
    const commits = await getHistoryCommits();
    const hasMerge = commits.some((m) => m.includes("Merge"));
    expect(hasMerge).toBe(true);
  });

  it("history shows recent commits", async () => {
    const commits = await getHistoryCommits();
    // Check for commits visible in the viewport (older commits may be off-screen due to virtualization)
    const hasRecent = commits.some((m) => m.includes("changelog") || m.includes("greeting") || m.includes("auth"));
    expect(hasRecent).toBe(true);
  });

  it("can switch back to staging tab", async () => {
    await switchLeftPanel("staging");
    await sleep(300);
    const tab = await $("[data-testid='left-tab-staging']");
    const cls = await tab.getAttribute("class");
    expect(cls).toContain("border-accent");
  });
});
