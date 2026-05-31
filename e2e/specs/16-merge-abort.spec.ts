/**
 * E2E: Merge abort — start a merge with conflicts, then abort.
 *
 * Uses test/merge-abort-conflict which modifies CHANGELOG.md differently from main.
 * Starts from a fresh repo with a clean working tree.
 */
import {
  getCurrentBranch,
  mergeBranch,
  isMergeDialogVisible,
  getConflictFiles,
  abortMerge,
  hasNoChanges,
  waitForStagingLoaded,
  switchLeftPanel,
  getHistoryCommits,
  setupTestClean,
  sleep,
} from "../helpers/app.js";

describe("Merge Abort", () => {
  before(async () => {
    await setupTestClean();
  });

  it("starts on main branch", async () => {
    const branch = await getCurrentBranch();
    expect(branch).toBe("main");
  });

  it("merging test/merge-abort-conflict shows conflict dialog", async () => {
    await mergeBranch("test/merge-abort-conflict");
    expect(await isMergeDialogVisible()).toBe(true);
  });

  it("shows conflicting files (CHANGELOG.md)", async () => {
    const files = await getConflictFiles();
    expect(files.length).toBeGreaterThanOrEqual(1);
    expect(files.some((f) => f.includes("CHANGELOG"))).toBe(true);
  });

  it("aborting the merge closes the dialog", async () => {
    await abortMerge();
    expect(await isMergeDialogVisible()).toBe(false);
  });

  it("stays on main after abort", async () => {
    const branch = await getCurrentBranch();
    expect(branch).toBe("main");
  });

  it("working tree is clean after abort (reverts to pre-merge state)", async () => {
    await switchLeftPanel("staging");
    await sleep(1000);
    await waitForStagingLoaded();
    // After aborting, the working tree should revert to the pre-merge state.
    expect(await hasNoChanges()).toBe(true);
  });

  it("history does not contain a merge commit for aborted merge", async () => {
    const commits = await getHistoryCommits();
    // After abort, the branch's unique commit should not be merged into main.
    const hasAbortBranchCommit = commits.some((m) => m.includes("ABORT TEST"));
    expect(hasAbortBranchCommit).toBe(false);
  });
});
