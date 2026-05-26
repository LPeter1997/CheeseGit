/**
 * E2E: Branch management — list, create, switch
 */
import {
  waitForAppReady,
  openRepoByPath,
  getCurrentBranch,
  openBranchDropdown,
  switchBranch,
  createBranch,
  getBranchList,
  commitAllChanges,
  sleep,
  waitFor,
  jsKeys,
  TEST_REPO_PATH,
} from "../helpers/app.js";

describe("Branch Management", () => {
  before(async () => {
    await waitForAppReady();
    await openRepoByPath(TEST_REPO_PATH);
    await commitAllChanges("test: commit for branch switching");
  });

  it("starts on the main branch", async () => {
    expect(await getCurrentBranch()).toBe("main");
  });

  it("opens the branch dropdown", async () => {
    await openBranchDropdown();
    const dropdown = await $("[data-testid='branch-dropdown']");
    expect(await dropdown.isExisting()).toBe(true);

    // Has a search input
    const search = await $("[data-testid='branch-search']");
    expect(await search.isExisting()).toBe(true);

    // Close it
    await jsKeys("Escape");
    await sleep(200);
  });

  it("lists all branches from the test repo", async () => {
    const branches = await getBranchList();
    expect(branches).toContain("main");
    expect(branches).toContain("feature/dark-mode");
    expect(branches).toContain("feature/new-greeting");
  });

  it("switches to a different branch", async () => {
    await switchBranch("feature/dark-mode");
    expect(await getCurrentBranch()).toBe("feature/dark-mode");
  });

  it("switches back to main", async () => {
    await switchBranch("main");
    expect(await getCurrentBranch()).toBe("main");
  });

  it("creates a new branch", async () => {
    await createBranch("test/e2e-branch");
    await sleep(500);
    expect(await getCurrentBranch()).toBe("test/e2e-branch");
  });

  it("new branch appears in the branch list", async () => {
    const branches = await getBranchList();
    expect(branches).toContain("test/e2e-branch");
  });

  it("can switch back to main after creating a branch", async () => {
    await switchBranch("main");
    expect(await getCurrentBranch()).toBe("main");
  });
});
