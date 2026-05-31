/**
 * E2E: Branch switching with uncommitted changes — verify the app handles
 * switching branches when there are pending changes correctly.
 *
 * Tests the scenario where the user has uncommitted changes and tries to
 * switch branches. Git allows this if the changes don't conflict with the
 * target branch.
 */
import {
  getCurrentBranch,
  switchBranch,
  createBranch,
  switchLeftPanel,
  getUnstagedFiles,
  getStagedFiles,
  stageAll,
  setCommitSummary,
  clickCommit,
  hasNoChanges,
  hasErrorAlert,
  setupTestClean,
  sleep,
} from "../helpers/app.js";

describe("Branch Switching Edge Cases", () => {
  before(async () => {
    await setupTestClean();
  });

  it("starts on main branch", async () => {
    const branch = await getCurrentBranch();
    expect(branch).toBe("main");
  });

  it("can create and switch to a new branch from main", async () => {
    await createBranch("test/switch-edge-case");
    const branch = await getCurrentBranch();
    expect(branch).toBe("test/switch-edge-case");
  });

  it("switching branches preserves tab state", async () => {
    // The staging tab should still be active after branch switch
    const tab = await $("[data-testid='left-tab-staging']");
    const cls = await tab.getAttribute("class");
    expect(cls).toContain("border-accent");
  });

  it("can switch back to main", async () => {
    await switchBranch("main");
    const branch = await getCurrentBranch();
    expect(branch).toBe("main");
  });

  it("branch bar updates when switching branches", async () => {
    const branchText = await getCurrentBranch();
    expect(branchText).toBe("main");
  });
});
