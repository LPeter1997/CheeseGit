/**
 * E2E: Merge dark-mode branch — clean merge of the unmerged feature branch.
 *
 * Tests merging feature/dark-mode (which adds new files without conflicts).
 * This verifies that branches with only additions merge cleanly.
 */
import {
  waitForAppReady,
  openRepoByPath,
  commitAllChanges,
  getCurrentBranch,
  mergeBranch,
  isMergeDialogVisible,
  getHistoryCommits,
  switchLeftPanel,
  hasNoChanges,
  waitForStagingLoaded,
  sleep,
  TEST_REPO_PATH,
} from "../helpers/app.js";

describe("Merge Dark Mode Branch (Clean)", () => {
  before(async () => {
    await waitForAppReady();
    await openRepoByPath(TEST_REPO_PATH);
    await commitAllChanges("test: commit for dark mode merge");
  });

  it("starts on main branch", async () => {
    const branch = await getCurrentBranch();
    expect(branch).toBe("main");
  });

  it("merges feature/dark-mode cleanly", async () => {
    await mergeBranch("feature/dark-mode");
    // dark-mode only adds theme.ts and theme.test.ts — no conflicts
    expect(await isMergeDialogVisible()).toBe(false);
  });

  it("stays on main after merge", async () => {
    const branch = await getCurrentBranch();
    expect(branch).toBe("main");
  });

  it("merge commit appears in history", async () => {
    const commits = await getHistoryCommits();
    expect(commits.some((m) =>
      m.includes("dark-mode") || m.includes("theme") || m.includes("Merge"),
    )).toBe(true);
  });

  it("working tree is clean after clean merge", async () => {
    await switchLeftPanel("staging");
    await waitForStagingLoaded();
    expect(await hasNoChanges()).toBe(true);
  });
});
