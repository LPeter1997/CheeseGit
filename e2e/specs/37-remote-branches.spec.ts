/**
 * E2E: Remote branches — list remote-only branches, checkout to create local tracking
 */
import {
  getCurrentBranch,
  getBranchList,
  getRemoteBranchList,
  switchToRemoteBranch,
  switchBranch,
  setupTestClean,
  sleep,
} from "../helpers/app.js";

describe("Remote Branches", () => {
  before(async () => {
    await setupTestClean();
  });

  it("shows remote-only branches in the dropdown", async () => {
    const remoteBranches = await getRemoteBranchList();
    expect(remoteBranches).toContain("feature/remote-analytics");
    expect(remoteBranches).toContain("feature/remote-notifications");
  });

  it("does not show locally tracked branches in the remote section", async () => {
    const remoteBranches = await getRemoteBranchList();
    // 'main' and other local branches should NOT appear in remote-only list.
    expect(remoteBranches).not.toContain("main");
    expect(remoteBranches).not.toContain("feature/dark-mode");
  });

  it("can checkout a remote branch, making it local", async () => {
    await switchToRemoteBranch("feature/remote-analytics");
    await sleep(500);
    expect(await getCurrentBranch()).toBe("feature/remote-analytics");
  });

  it("checked-out branch now appears in local branch list", async () => {
    const locals = await getBranchList();
    expect(locals).toContain("feature/remote-analytics");
  });

  it("checked-out branch no longer appears in remote branch list", async () => {
    const remotes = await getRemoteBranchList();
    expect(remotes).not.toContain("feature/remote-analytics");
  });

  it("can switch back to main", async () => {
    await switchBranch("main");
    expect(await getCurrentBranch()).toBe("main");
  });
});
