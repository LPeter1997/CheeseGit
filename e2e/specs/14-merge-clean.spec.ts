/**
 * E2E: Clean merge — merge a branch that has no conflicts with current branch.
 *
 * Uses feature/clean-merge-target which only adds src/format.ts (no overlap with main).
 * Starts from a fresh repo with a clean working tree.
 */
import {
  getCurrentBranch,
  mergeBranch,
  isMergeDialogVisible,
  getHistoryCommits,
  getBranchList,
  switchLeftPanel,
  setupTestClean,
  sleep,
} from "../helpers/app.js";

describe("Clean Merge", () => {
  before(async () => {
    await setupTestClean();
  });

  it("starts on main branch", async () => {
    const branch = await getCurrentBranch();
    expect(branch).toBe("main");
  });

  it("feature/clean-merge-target exists in branch list", async () => {
    const branches = await getBranchList();
    expect(branches).toContain("feature/clean-merge-target");
  });

  it("merges feature/clean-merge-target cleanly (no conflict dialog)", async () => {
    await mergeBranch("feature/clean-merge-target");
    // A clean merge should NOT show the conflict dialog
    expect(await isMergeDialogVisible()).toBe(false);
  });

  it("stays on main branch after merge", async () => {
    const branch = await getCurrentBranch();
    expect(branch).toBe("main");
  });

  it("merge commit appears in history", async () => {
    const commits = await getHistoryCommits();
    // The merge commit or the merged branch's commit should be visible
    expect(commits.some((m) => m.includes("format") || m.includes("Merge"))).toBe(true);
  });

  it("merged branch is still in branch list (not auto-deleted)", async () => {
    const branches = await getBranchList();
    expect(branches).toContain("feature/clean-merge-target");
  });
});
