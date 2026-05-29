/**
 * E2E: Conflict Resolution Strategies — AcceptCurrent, AcceptIncoming, AcceptBoth.
 *
 * Uses three dedicated test branches that each modify src/constants.ts:
 *   test/conflict-accept-current  → APP_VERSION = "1.0.0-alpha"
 *   test/conflict-accept-incoming → APP_VERSION = "2.0.0-beta"
 *   test/conflict-accept-both     → APP_VERSION = "3.0.0-rc"
 *
 * After spec 35 / before this suite, commitAllChanges() commits the unstaged
 * constants.ts (APP_VERSION = "0.4.0"). All three branches therefore conflict
 * with main because both sides diverge from the common ancestor (0.3.0).
 */
import {
  waitForAppReady,
  openRepoByPath,
  commitAllChanges,
  getCurrentBranch,
  mergeBranch,
  isMergeDialogVisible,
  getConflictFiles,
  setConflictResolutionForFile,
  completeMerge,
  isMergeCompleteEnabled,
  hasNoChanges,
  waitForStagingLoaded,
  switchLeftPanel,
  sleep,
  TEST_REPO_PATH,
} from "../helpers/app.js";

describe("Conflict Resolution Strategies", () => {
  describe("AcceptCurrent Strategy", () => {
    before(async () => {
      await waitForAppReady();
      await openRepoByPath(TEST_REPO_PATH);
      // Commit any dirty state so the merge base is well-defined.
      await commitAllChanges("setup: commit all changes for conflict strategy test");
    });

    it("merging test/conflict-accept-current shows conflict dialog", async () => {
      await mergeBranch("test/conflict-accept-current");
      expect(await isMergeDialogVisible()).toBe(true);
    });

    it("shows conflicted files (including src/constants.ts)", async () => {
      const files = await getConflictFiles();
      expect(files.length).toBeGreaterThanOrEqual(1);
      expect(files.some((f) => f.includes("constants"))).toBe(true);
    });

    it("complete button is disabled before resolving", async () => {
      expect(await isMergeCompleteEnabled()).toBe(false);
    });

    it("resolves all conflicts with AcceptCurrent", async () => {
      const files = await getConflictFiles();
      for (let i = 0; i < files.length; i++) {
        await setConflictResolutionForFile(i, "AcceptCurrent");
      }
      await sleep(500);
      expect(await isMergeCompleteEnabled()).toBe(true);
    });

    it("completes merge successfully", async () => {
      await completeMerge();
      expect(await isMergeDialogVisible()).toBe(false);
    });

    it("working tree is clean after AcceptCurrent merge", async () => {
      await switchLeftPanel("staging");
      await waitForStagingLoaded();
      expect(await hasNoChanges()).toBe(true);
    });

    it("stays on main branch", async () => {
      const branch = await getCurrentBranch();
      expect(branch).toBe("main");
    });
  });

  describe("AcceptIncoming Strategy", () => {
    before(async () => {
      await waitForAppReady();
      await openRepoByPath(TEST_REPO_PATH);
      // Working tree should be clean from the AcceptCurrent describe block.
    });

    it("merging test/conflict-accept-incoming shows conflict dialog", async () => {
      await mergeBranch("test/conflict-accept-incoming");
      expect(await isMergeDialogVisible()).toBe(true);
    });

    it("shows conflicted files", async () => {
      const files = await getConflictFiles();
      expect(files.length).toBeGreaterThanOrEqual(1);
    });

    it("resolves all conflicts with AcceptIncoming", async () => {
      const files = await getConflictFiles();
      for (let i = 0; i < files.length; i++) {
        await setConflictResolutionForFile(i, "AcceptIncoming");
      }
      await sleep(500);
      expect(await isMergeCompleteEnabled()).toBe(true);
    });

    it("completes merge successfully", async () => {
      await completeMerge();
      expect(await isMergeDialogVisible()).toBe(false);
    });

    it("working tree is clean after AcceptIncoming merge", async () => {
      await switchLeftPanel("staging");
      await waitForStagingLoaded();
      expect(await hasNoChanges()).toBe(true);
    });

    it("stays on main branch", async () => {
      const branch = await getCurrentBranch();
      expect(branch).toBe("main");
    });
  });

  describe("AcceptBoth Strategy", () => {
    before(async () => {
      await waitForAppReady();
      await openRepoByPath(TEST_REPO_PATH);
      // Working tree should be clean from the AcceptIncoming describe block.
    });

    it("merging test/conflict-accept-both shows conflict dialog", async () => {
      await mergeBranch("test/conflict-accept-both");
      expect(await isMergeDialogVisible()).toBe(true);
    });

    it("shows conflicted files", async () => {
      const files = await getConflictFiles();
      expect(files.length).toBeGreaterThanOrEqual(1);
    });

    it("resolves all conflicts with AcceptBoth", async () => {
      const files = await getConflictFiles();
      for (let i = 0; i < files.length; i++) {
        await setConflictResolutionForFile(i, "AcceptBoth");
      }
      await sleep(500);
      expect(await isMergeCompleteEnabled()).toBe(true);
    });

    it("completes merge successfully", async () => {
      await completeMerge();
      expect(await isMergeDialogVisible()).toBe(false);
    });

    it("working tree is clean after AcceptBoth merge", async () => {
      await switchLeftPanel("staging");
      await waitForStagingLoaded();
      expect(await hasNoChanges()).toBe(true);
    });

    it("stays on main branch", async () => {
      const branch = await getCurrentBranch();
      expect(branch).toBe("main");
    });
  });
});

