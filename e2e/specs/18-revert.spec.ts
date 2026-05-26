/**
 * E2E: Revert commit — revert the most recent commit and verify the result.
 *
 * After previous merge specs, we have several merge commits in history.
 * We'll revert the most recent non-merge commit to test the revert flow.
 */
import {
  waitForAppReady,
  openRepoByPath,
  commitAllChanges,
  getCurrentBranch,
  isMergeDialogVisible,
  revertCommit,
  getHistoryCommits,
  hasNoChanges,
  jsClick,
  switchLeftPanel,
  sleep,
  TEST_REPO_PATH,
} from "../helpers/app.js";

describe("Revert Commit", () => {
  before(async () => {
    await waitForAppReady();
    await openRepoByPath(TEST_REPO_PATH);
    await commitAllChanges("test: commit for revert test");
    await switchLeftPanel("history");
    await sleep(500);
  });

  it("shows commit history", async () => {
    const commits = await getHistoryCommits();
    expect(commits.length).toBeGreaterThan(0);
  });

  it("can revert a recent commit (clean revert)", async () => {
    // Revert the most recent commit (index 0)
    // This should be a clean revert since nothing else has modified those files
    await revertCommit(0);
    await sleep(500);

    // If clean revert, dialog should not appear
    // If conflicts arise, dialog will appear — either way is a valid test
    const dialogVisible = await isMergeDialogVisible();

    if (dialogVisible) {
      // Revert caused conflicts — this is the revert conflict dialog
      // Verify it says "Revert" not "Merge"
      const abortText: string = await browser.execute(() => {
        const el = document.querySelector("[data-testid='merge-abort-button']");
        return el?.textContent?.trim() ?? "";
      });
      expect(abortText).toContain("Abort Revert");

      const completeText: string = await browser.execute(() => {
        const el = document.querySelector("[data-testid='merge-complete-button']");
        return el?.textContent?.trim() ?? "";
      });
      expect(completeText).toContain("Complete Revert");

      // Abort the revert to leave working tree clean for subsequent tests
      const abortBtn = await $("[data-testid='merge-abort-button']");
      await jsClick(abortBtn);
      await sleep(500);
    } else {
      // Clean revert succeeded — verify the Revert commit is in history
      const commits = await getHistoryCommits();
      expect(commits.some((m) => m.includes("Revert"))).toBe(true);
    }
  });

  it("stays on main after revert", async () => {
    const branch = await getCurrentBranch();
    expect(branch).toBe("main");
  });
});
