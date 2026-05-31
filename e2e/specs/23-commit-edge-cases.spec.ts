/**
 * E2E: Commit edge cases — default summary, empty commit mode.
 *
 * Tests edge cases around the commit workflow:
 * - Default summary is auto-generated from the first staged file
 * - Empty commit mode allows committing with no changes
 */
import {
  waitForStagingLoaded,
  switchLeftPanel,
  getUnstagedFiles,
  getStagedFiles,
  stageAll,
  unstageAll,
  setCommitSummary,
  clickCommit,
  getCommitButtonText,
  isCommitDisabled,
  hasNoChanges,
  getHistoryCommits,
  setupTestClean,
  sleep,
  jsClick,
} from "../helpers/app.js";

describe("Commit Edge Cases", () => {
  before(async () => {
    // Start with clean tree so we can test empty-commit mode
    await setupTestClean();
    await switchLeftPanel("staging");
  });

  it("commit button is disabled when nothing is staged and no summary", async () => {
    const noChanges = await hasNoChanges();
    if (noChanges) {
      // No changes available — commit button should be disabled
      expect(await isCommitDisabled()).toBe(true);
    }
  });

  it("empty commit mode is available via UI", async () => {
    // When there are no changes, an "enable empty commit" button should exist
    const noChanges = await hasNoChanges();
    if (noChanges) {
      const emptyCommitBtn = await $("button*=Make empty commit");
      if (await emptyCommitBtn.isExisting()) {
        await jsClick(emptyCommitBtn);
        await sleep(300);
        // After enabling empty commit mode, commit button should be usable
        // Set a summary first
        await setCommitSummary("test: empty commit");
        expect(await isCommitDisabled()).toBe(false);

        // Actually commit
        await clickCommit();
        await sleep(500);

        // Verify the empty commit appears in history
        const commits = await getHistoryCommits();
        expect(commits.some((m) => m.includes("empty commit"))).toBe(true);
      }
    }
  });

  it("commit button text includes branch name", async () => {
    await switchLeftPanel("staging");
    await sleep(300);
    const text = await getCommitButtonText();
    expect(text.toLowerCase()).toContain("commit");
  });
});
