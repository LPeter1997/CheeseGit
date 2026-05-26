/**
 * E2E: Staging panel — view changes, stage, unstage, discard, commit
 */
import {
  waitForAppReady,
  openRepoByPath,
  waitForStagingLoaded,
  getUnstagedFiles,
  getStagedFiles,
  stageFile,
  stageAll,
  unstageAll,
  selectFileForDiff,
  setCommitSummary,
  setCommitDescription,
  clickCommit,
  getCommitButtonText,
  isCommitDisabled,
  hasNoChanges,
  isDiffVisible,
  isDiscardDialogVisible,
  confirmDiscard,
  cancelDiscard,
  sleep,
  jsClick,
  jsShiftClick,
  jsMoveTo,
  TEST_REPO_PATH,
} from "../helpers/app.js";

describe("Staging Panel", () => {
  before(async () => {
    await waitForAppReady();
    await openRepoByPath(TEST_REPO_PATH);
    await waitForStagingLoaded();
  });

  describe("Initial state from test repo", () => {
    it("shows unstaged files", async () => {
      const unstaged = await getUnstagedFiles();
      expect(unstaged.length).toBeGreaterThanOrEqual(2);
      // Test repo leaves src/constants.ts (modified) and src/helpers.ts (new) unstaged
      expect(unstaged).toContain("src/constants.ts");
      expect(unstaged).toContain("src/helpers.ts");
    });

    it("shows staged files", async () => {
      const staged = await getStagedFiles();
      expect(staged.length).toBeGreaterThanOrEqual(1);
      // Test repo stages docs/getting-started.md
      expect(staged).toContain("docs/getting-started.md");
    });

    it("commit button shows branch name", async () => {
      const text = await getCommitButtonText();
      expect(text).toContain("main");
    });
  });

  describe("File selection and diff", () => {
    it("clicking a file shows the diff viewer", async () => {
      await selectFileForDiff("src/constants.ts");
      expect(await isDiffVisible()).toBe(true);
    });
  });

  describe("Stage and unstage operations", () => {
    it("stages a single file", async () => {
      const beforeUnstaged = await getUnstagedFiles();
      const beforeStaged = await getStagedFiles();

      await stageFile("src/constants.ts");

      const afterUnstaged = await getUnstagedFiles();
      const afterStaged = await getStagedFiles();

      expect(afterUnstaged.length).toBe(beforeUnstaged.length - 1);
      expect(afterStaged.length).toBe(beforeStaged.length + 1);
      expect(afterStaged).toContain("src/constants.ts");
    });

    it("stage-all stages remaining files", async () => {
      await stageAll();
      const unstaged = await getUnstagedFiles();
      const staged = await getStagedFiles();
      expect(unstaged.length).toBe(0);
      expect(staged.length).toBeGreaterThanOrEqual(3);
    });

    it("unstage-all moves everything back", async () => {
      await unstageAll();
      const unstaged = await getUnstagedFiles();
      const staged = await getStagedFiles();
      expect(unstaged.length).toBeGreaterThanOrEqual(3);
      expect(staged.length).toBe(0);
    });
  });

  describe("Discard changes", () => {
    it("shift-clicking file action shows discard confirmation dialog", async () => {
      // Use Shift + click on a file action button to trigger discard
      const row = await $("[data-testid='file-row'][data-filepath='src/helpers.ts']");
      const actionBtn = await row.$("[data-testid='file-action']");

      // Hover first so the button becomes visible (opacity transition)
      await jsMoveTo(row);
      await sleep(200);

      // Shift-click to trigger discard
      await jsShiftClick(actionBtn);
      await sleep(300);

      expect(await isDiscardDialogVisible()).toBe(true);
    });

    it("cancel discard keeps the file", async () => {
      await cancelDiscard();
      await sleep(300);

      // File should still be in unstaged list
      const unstaged = await getUnstagedFiles();
      expect(unstaged).toContain("src/helpers.ts");
    });

    it("confirming discard removes the file from changes", async () => {
      // Shift-click to discard again, this time confirm
      const row = await $("[data-testid='file-row'][data-filepath='src/helpers.ts']");
      const actionBtn = await row.$("[data-testid='file-action']");

      // Hover first so the button becomes visible
      await jsMoveTo(row);
      await sleep(200);

      // Shift-click to trigger discard
      await jsShiftClick(actionBtn);
      await sleep(300);

      await confirmDiscard();
      await sleep(500);

      // File should no longer be in unstaged list
      const unstaged = await getUnstagedFiles();
      expect(unstaged).not.toContain("src/helpers.ts");
    });
  });

  describe("Commit workflow", () => {
    it("commit button is disabled with no staged files", async () => {
      expect(await isCommitDisabled()).toBe(true);
    });

    it("can stage files and commit", async () => {
      // Stage all remaining files
      await stageAll();

      // Set commit message
      await setCommitSummary("test: e2e commit from test suite");
      await setCommitDescription("This commit was made by the e2e test suite.");

      // Commit
      await clickCommit();

      // After committing, there should be no changes
      await sleep(500);
      expect(await hasNoChanges()).toBe(true);
    });
  });
});
