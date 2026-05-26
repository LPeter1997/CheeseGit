/**
 * E2E: Merge with conflicts — merge a branch that conflicts with main,
 * resolve conflicts via the dialog, and complete the merge.
 *
 * Uses feature/new-greeting which modifies src/main.ts differently from main.
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
  getMergeCommitMessage,
  setMergeCommitMessage,
  getMergeCompleteButtonText,
  getHistoryCommits,
  hasNoChanges,
  waitForStagingLoaded,
  switchLeftPanel,
  sleep,
  TEST_REPO_PATH,
} from "../helpers/app.js";

describe("Merge with Conflicts", () => {
  before(async () => {
    await waitForAppReady();
    await openRepoByPath(TEST_REPO_PATH);
    await commitAllChanges("test: commit for conflict merge test");
  });

  it("starts on main branch", async () => {
    const branch = await getCurrentBranch();
    expect(branch).toBe("main");
  });

  it("merging feature/new-greeting shows conflict dialog", async () => {
    await mergeBranch("feature/new-greeting");
    // This branch conflicts with main on src/main.ts
    expect(await isMergeDialogVisible()).toBe(true);
  });

  it("shows conflicting files in the dialog", async () => {
    const files = await getConflictFiles();
    expect(files.length).toBeGreaterThanOrEqual(1);
    // src/main.ts should be listed as a conflict
    expect(files.some((f) => f.includes("main.ts"))).toBe(true);
  });

  it("has a default merge commit message", async () => {
    const msg = await getMergeCommitMessage();
    expect(msg).toContain("Merge");
    expect(msg).toContain("feature/new-greeting");
  });

  it("complete button is disabled before resolving all conflicts", async () => {
    expect(await isMergeCompleteEnabled()).toBe(false);
  });

  it("complete button text says 'Complete Merge'", async () => {
    const text = await getMergeCompleteButtonText();
    expect(text).toBe("Complete Merge");
  });

  it("can set a resolution for conflicting files", async () => {
    // Resolve all conflict files with AcceptCurrent (keep main's version)
    const files = await getConflictFiles();
    for (let i = 0; i < files.length; i++) {
      await setConflictResolutionForFile(i, "AcceptCurrent");
    }
    await sleep(500);

    // Now the complete button should be enabled
    expect(await isMergeCompleteEnabled()).toBe(true);
  });

  it("can customize the merge commit message", async () => {
    await setMergeCommitMessage("Merge feature/new-greeting into main (e2e test)");
    const msg = await getMergeCommitMessage();
    expect(msg).toContain("e2e test");
  });

  it("completing the merge closes the dialog", async () => {
    await completeMerge();
    expect(await isMergeDialogVisible()).toBe(false);
  });

  it("stays on main branch after merge", async () => {
    const branch = await getCurrentBranch();
    expect(branch).toBe("main");
  });

  it("merge commit appears in history", async () => {
    const commits = await getHistoryCommits();
    expect(commits.some((m) => m.includes("e2e test") || m.includes("Merge"))).toBe(true);
  });

  it("working tree is clean after merge", async () => {
    await switchLeftPanel("staging");
    await waitForStagingLoaded();
    expect(await hasNoChanges()).toBe(true);
  });
});
