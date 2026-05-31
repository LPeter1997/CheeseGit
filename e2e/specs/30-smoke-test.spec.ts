/**
 * E2E: Full workflow smoke test — performs a complete end-to-end workflow
 * covering the most common developer interactions in a single test.
 *
 * This tests the integration between all features working together:
 * 1. Open repo
 * 2. View status
 * 3. View history
 * 4. View a diff
 * 5. Switch branches
 * 6. Switch back
 * 7. View stashes
 * 8. Check command log
 * 9. Close repo
 */
import {
  waitForAppReady,
  openRepoByPath,
  commitAllChanges,
  switchLeftPanel,
  getCurrentBranch,
  switchBranch,
  getBranchList,
  getHistoryCommits,
  clickHistoryCommit,
  selectFirstCommitFile,
  isDiffVisible,
  waitForDiffVisible,
  getStashEntries,
  toggleCommandLog,
  isCommandLogOpen,
  getCommandLogEntryCount,
  closeTab,
  getOpenTabs,
  isWelcomeVisible,
  setupTest,
  sleep,
  TEST_REPO_PATH,
} from "../helpers/app.js";

describe("Full Workflow Smoke Test", () => {
  before(async () => {
    await setupTest();
  });

  it("opens repository and shows repo view", async () => {
    // setupTest already opened the repo
    const repoView = await $("[data-testid='repo-view']");
    expect(await repoView.isExisting()).toBe(true);
  });

  it("branch bar shows the current branch", async () => {
    const branch = await getCurrentBranch();
    expect(branch.length).toBeGreaterThan(0);
  });

  it("views commit history with entries", async () => {
    await switchLeftPanel("history");
    await sleep(500);
    const commits = await getHistoryCommits();
    expect(commits.length).toBeGreaterThan(5);
  });

  it("clicking a commit shows diff", async () => {
    // Commit 0 may be an empty commit from commitAllChanges; use commit 1
    await clickHistoryCommit(1);
    await selectFirstCommitFile();
    await waitForDiffVisible();
    expect(await isDiffVisible()).toBe(true);
  });

  it("switches to a different branch and back", async () => {
    await switchLeftPanel("staging");
    await sleep(300);

    // Switch to an existing branch from the template repo
    const branches = await getBranchList();
    const otherBranch = branches.find((b) => b !== "main" && b.startsWith("feature/"));
    if (otherBranch) {
      await switchBranch(otherBranch);
      await sleep(500);
      expect(await getCurrentBranch()).toBe(otherBranch);
      await switchBranch("main");
    }
    const finalBranch = await getCurrentBranch();
    expect(finalBranch).toBe("main");
  });

  it("command log has entries from the workflow", async () => {
    await toggleCommandLog();
    expect(await isCommandLogOpen()).toBe(true);
    const count = await getCommandLogEntryCount();
    expect(count).toBeGreaterThan(0);
    await toggleCommandLog();
  });

  it("can close the repo and see welcome screen", async () => {
    const tabs = await getOpenTabs();
    for (const tab of tabs) {
      await closeTab(tab);
    }
    await sleep(500);
    expect(await isWelcomeVisible()).toBe(true);
  });

  it("can reopen repo after full workflow", async () => {
    await openRepoByPath(TEST_REPO_PATH);
    await sleep(500);
    const repoView = await $("[data-testid='repo-view']");
    expect(await repoView.isExisting()).toBe(true);
  });
});
