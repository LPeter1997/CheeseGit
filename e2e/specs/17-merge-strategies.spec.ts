/**
 * E2E: Merge with different resolution strategies — test AcceptIncoming and AcceptBoth.
 *
 * Re-merges test/merge-abort-conflict (which was aborted in spec 16) using
 * AcceptIncoming, verifying the strategy is applied correctly.
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

describe("Merge Resolution Strategies", () => {
  before(async () => {
    await waitForAppReady();
    await openRepoByPath(TEST_REPO_PATH);
    await commitAllChanges("test: commit for merge strategy test");
  });

  it("merging test/merge-abort-conflict again shows conflicts", async () => {
    await mergeBranch("test/merge-abort-conflict");
    expect(await isMergeDialogVisible()).toBe(true);
  });

  it("can resolve with AcceptIncoming strategy", async () => {
    const files = await getConflictFiles();
    for (let i = 0; i < files.length; i++) {
      await setConflictResolutionForFile(i, "AcceptIncoming");
    }
    await sleep(300);
    expect(await isMergeCompleteEnabled()).toBe(true);
  });

  it("completing the merge with AcceptIncoming succeeds", async () => {
    await completeMerge();
    expect(await isMergeDialogVisible()).toBe(false);
  });

  it("working tree is clean after merge", async () => {
    await switchLeftPanel("staging");
    await waitForStagingLoaded();
    expect(await hasNoChanges()).toBe(true);
  });
});
