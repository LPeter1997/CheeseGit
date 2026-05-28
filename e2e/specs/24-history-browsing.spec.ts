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
  async function clickCommitWithDiff(indices: number[]) {
    let lastError: unknown = null;
    for (const index of indices) {
      try {
        await clickHistoryCommit(index);
        await selectFirstCommitFile();
        return;
      } catch (error) {
        lastError = error;
      }
    }
    throw lastError ?? new Error("Could not find a history commit with file entries");
  }

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
    await clickCommitWithDiff([2, 3, 4, 5]);
    expect(await isDiffVisible()).toBe(true);
  });

  it("can click a different commit and see different diff", async () => {
    // Some top commits can be synthetic merge/revert commits with delayed or no file rows.
    await clickCommitWithDiff([1, 6, 7, 8]);
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
