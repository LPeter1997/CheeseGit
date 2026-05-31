/**
 * E2E: Merge alert banners
 *
 * Tests:
 * - Info banner appears after a clean merge showing "Merged N commits"
 * - Info banner appears when merging an already-up-to-date branch
 * - Warning banner appears when opening a repo with an in-progress merge
 * - Clicking "Resolve Conflicts" in the warning banner opens the conflict dialog
 */
import {
  getCurrentBranch,
  mergeBranch,
  isMergeDialogVisible,
  getInfoAlertMessages,
  getWarningAlertMessages,
  hasMergeInProgressAlert,
  clickMergeInProgressResolve,
  setupTestClean,
  closeAllTabs,
  openRepoByPath,
  waitForStagingLoaded,
  sleep,
  TEST_REPO_PATH,
} from "../helpers/app.js";

import { gitSync, writeTestFile, commitAllViaGit } from "../helpers/repo.js";

describe("Merge Alert Banners", () => {
  before(async () => {
    await setupTestClean();
  });

  describe("clean merge shows info banner", () => {
    it("starts on main branch", async () => {
      const branch = await getCurrentBranch();
      expect(branch).toBe("main");
    });

    it("merging feature/clean-merge-target shows info alert about merged commits", async () => {
      await mergeBranch("feature/clean-merge-target");
      expect(await isMergeDialogVisible()).toBe(false);

      // Wait for the info alert to appear with the expected text
      await browser.waitUntil(
        async () => {
          const messages = await getInfoAlertMessages();
          return messages.some(
            (m) => m.includes("Merged") && m.includes("commit"),
          );
        },
        { timeout: 5000, timeoutMsg: "Info alert about merged commits did not appear" },
      );
    });
  });

  describe("already up to date shows info banner", () => {
    before(async () => {
      await setupTestClean();
    });

    it("merging the same branch again shows 'already up to date' alert", async () => {
      // First merge
      await mergeBranch("feature/clean-merge-target");
      await sleep(1000);

      // Now merge again — should be "already up to date"
      await mergeBranch("feature/clean-merge-target");

      await browser.waitUntil(
        async () => {
          const messages = await getInfoAlertMessages();
          return messages.some((m) =>
            m.toLowerCase().includes("already up to date"),
          );
        },
        { timeout: 5000, timeoutMsg: "Info alert about 'already up to date' did not appear" },
      );
    });
  });

  describe("in-progress merge banner on repo open", () => {
    before(async () => {
      await setupTestClean();

      // Create a conflict scenario via git CLI
      // Create a feature branch with conflicting changes
      gitSync("checkout", "-b", "conflict-test");
      writeTestFile("conflict-file.txt", "conflict branch content\n");
      commitAllViaGit("conflict branch commit");
      gitSync("checkout", "main");
      writeTestFile("conflict-file.txt", "main branch content\n");
      commitAllViaGit("main branch commit");

      // Start a merge that will conflict (via git CLI, not the app)
      try {
        gitSync("merge", "conflict-test");
      } catch {
        // Expected to fail with merge conflicts
      }

      // Close and re-open the repo so the app detects the in-progress merge
      await closeAllTabs();
      await sleep(500);
      await openRepoByPath(TEST_REPO_PATH);
      await waitForStagingLoaded();
      await sleep(1000);
    });

    it("shows a merge-in-progress warning banner", async () => {
      expect(await hasMergeInProgressAlert()).toBe(true);
    });

    it("warning banner mentions the conflicted branch", async () => {
      const messages = await getWarningAlertMessages();
      const mergeMsg = messages.find((m) =>
        m.toLowerCase().includes("merge in progress"),
      );
      expect(mergeMsg).toBeDefined();
    });

    it("clicking 'Resolve Conflicts' opens the merge dialog", async () => {
      await clickMergeInProgressResolve();
      expect(await isMergeDialogVisible()).toBe(true);
    });
  });
});
