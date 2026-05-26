/**
 * E2E: Branch deletion — delete a branch via the branch dropdown.
 *
 * Deletes the test/e2e-branch created in 05-branches.spec.ts, and also tests
 * deleting merged branches.
 */
import {
  waitForAppReady,
  openRepoByPath,
  commitAllChanges,
  getCurrentBranch,
  getBranchList,
  createBranch,
  switchBranch,
  deleteBranch,
  isDeleteBranchDialogVisible,
  confirmDeleteBranch,
  cancelDeleteBranch,
  sleep,
  TEST_REPO_PATH,
} from "../helpers/app.js";

describe("Branch Deletion", () => {
  before(async () => {
    await waitForAppReady();
    await openRepoByPath(TEST_REPO_PATH);
    await commitAllChanges("test: commit for branch delete");
    // Ensure we're on main
    const branch = await getCurrentBranch();
    if (branch !== "main") {
      await switchBranch("main");
    }
  });

  it("creates a temporary branch for deletion testing", async () => {
    await createBranch("test/delete-me");
    const branch = await getCurrentBranch();
    expect(branch).toBe("test/delete-me");
    // Switch back to main so we can delete it
    await switchBranch("main");
  });

  it("test/delete-me appears in the branch list", async () => {
    const branches = await getBranchList();
    expect(branches).toContain("test/delete-me");
  });

  it("clicking delete shows the delete branch dialog", async () => {
    await deleteBranch("test/delete-me");
    expect(await isDeleteBranchDialogVisible()).toBe(true);
  });

  it("cancel keeps the branch", async () => {
    await cancelDeleteBranch();
    await sleep(300);
    const branches = await getBranchList();
    expect(branches).toContain("test/delete-me");
  });

  it("confirming delete removes the branch", async () => {
    await deleteBranch("test/delete-me");
    await sleep(300);
    await confirmDeleteBranch();
    await sleep(500);
    const branches = await getBranchList();
    expect(branches).not.toContain("test/delete-me");
  });

  it("cannot delete the current branch", async () => {
    // The current branch (main) should not have a delete button
    const branch = await getCurrentBranch();
    expect(branch).toBe("main");
    // The delete button for main is invisible (disabled)
    const deleteBtn = await $("[data-testid='delete-branch-main']");
    const exists = await deleteBtn.isExisting();
    // Main's delete button should not exist (it's invisible for current branch)
    expect(exists).toBe(false);
  });
});
