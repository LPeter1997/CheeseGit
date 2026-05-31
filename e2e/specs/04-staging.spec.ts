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
  ctrlSelectFile,
  shiftSelectFile,
  getSelectedFilesInSection,
  getBulkActionLabel,
  setCommitSummary,
  setCommitDescription,
  clickCommit,
  getCommitButtonText,
  isCommitDisabled,
  hasNoChanges,
  isDiffVisible,
  waitForDiffVisible,
  isDiscardDialogVisible,
  confirmDiscard,
  cancelDiscard,
  setupTest,
  sleep,
  jsClick,
  jsShiftClick,
  jsMoveTo,
  TEST_REPO_PATH,
} from "../helpers/app.js";

describe("Staging Panel", () => {
  before(async () => {
    await setupTest();
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
      await waitForDiffVisible();
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

    it("visually highlights neighbor after staging a file", async () => {
      // Get remaining unstaged files to find a neighbor
      const unstagedFiles = await getUnstagedFiles();
      expect(unstagedFiles.length).toBeGreaterThanOrEqual(1);

      // Click on the first file to select it
      const firstFile = unstagedFiles[0]!;
      await selectFileForDiff(firstFile);

      // Verify it's selected
      let selectedFiles = await getSelectedFilesInSection("unstaged");
      expect(selectedFiles).toContain(firstFile);

      // Stage the file (which should select the neighbor)
      await stageFile(firstFile);

      // After staging, the neighbor should be visually highlighted
      if (unstagedFiles.length > 1) {
        const expectedNeighbor = unstagedFiles[1]!;
        const selectedFilesAfter = await getSelectedFilesInSection("unstaged");
        expect(selectedFilesAfter).toContain(expectedNeighbor);
      }
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

    it("supports shift-range multiselect and stages only selected files", async () => {
      const unstagedNow = await getUnstagedFiles();
      expect(unstagedNow.length).toBeGreaterThanOrEqual(3);
      const firstPath = unstagedNow[0];
      const thirdPath = unstagedNow[2];

      // Start from single selection to keep diff behavior unchanged.
      await selectFileForDiff(firstPath);

      // Extend selection with Shift to include first three files.
      await shiftSelectFile(thirdPath);

      const selected = await getSelectedFilesInSection("unstaged");
      expect(selected).toContain(firstPath);
      expect(selected).toContain(thirdPath);
      expect(selected.length).toBeGreaterThanOrEqual(3);

      const stageLabel = await getBulkActionLabel("stage");
      expect(stageLabel).toContain(`Stage ${selected.length} Selected`);

      await stageAll();

      const stagedAfter = await getStagedFiles();
      expect(stagedAfter).toEqual(expect.arrayContaining(selected));
    });

    it("supports ctrl-click toggle and reverts to Stage All when only one stays selected", async () => {
      await unstageAll();
      await sleep(300);

      const unstagedNow = await getUnstagedFiles();
      expect(unstagedNow.length).toBeGreaterThanOrEqual(1);
      const target = unstagedNow[0];

      await ctrlSelectFile(target);
      let selected = await getSelectedFilesInSection("unstaged");
      expect(selected).toContain(target);
      expect(selected.length).toBe(1);

      await ctrlSelectFile(target);
      selected = await getSelectedFilesInSection("unstaged");
      expect(selected.length).toBe(0);

      await selectFileForDiff(target);
      const stageLabel = await getBulkActionLabel("stage");
      expect(stageLabel).toContain("Stage All");
    });

    it("shows Unstage N Selected for staged multi-selection", async () => {
      await stageAll();
      await sleep(300);

      const stagedNow = await getStagedFiles();
      expect(stagedNow.length).toBeGreaterThanOrEqual(2);

      await selectFileForDiff(stagedNow[0]);
      await shiftSelectFile(stagedNow[1]);

      const selected = await getSelectedFilesInSection("staged");
      expect(selected.length).toBeGreaterThanOrEqual(2);

      const unstageLabel = await getBulkActionLabel("unstage");
      expect(unstageLabel).toContain(`Unstage ${selected.length} Selected`);
    });

    it("shows Discard N Selected while shift is held in multiselect mode", async () => {
      await unstageAll();
      await sleep(300);

      const unstagedNow = await getUnstagedFiles();
      expect(unstagedNow.length).toBeGreaterThanOrEqual(2);
      const firstPath = unstagedNow[0];
      const secondPath = unstagedNow[1];

      await selectFileForDiff(firstPath);
      await shiftSelectFile(secondPath);

      const selected = await getSelectedFilesInSection("unstaged");
      expect(selected.length).toBeGreaterThanOrEqual(2);

      await browser.execute(() => {
        window.dispatchEvent(new KeyboardEvent("keydown", { key: "Shift", bubbles: true }));
      });
      await sleep(200);

      const discardLabel = await getBulkActionLabel("stage");
      expect(discardLabel).toContain(`Discard ${selected.length} Selected`);

      await browser.execute(() => {
        window.dispatchEvent(new KeyboardEvent("keyup", { key: "Shift", bubbles: true }));
      });
    });
  });

  describe("Discard changes", () => {
    let discardTarget = "";

    before(async () => {
      await unstageAll();
      await sleep(300);
      const unstaged = await getUnstagedFiles();
      expect(unstaged.length).toBeGreaterThanOrEqual(1);
      discardTarget = unstaged[unstaged.length - 1];
    });

    it("shift-clicking file action shows discard confirmation dialog", async () => {
      // Use Shift + click on a file action button to trigger discard
      const row = await $(`[data-testid='file-row'][data-filepath='${discardTarget}']`);
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
      expect(unstaged).toContain(discardTarget);
    });

    it("confirming discard removes the file from changes", async () => {
      // Shift-click to discard again, this time confirm
      const row = await $(`[data-testid='file-row'][data-filepath='${discardTarget}']`);
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
      expect(unstaged).not.toContain(discardTarget);
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
