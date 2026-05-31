/**
 * E2E: Merge with different resolution strategies — test AcceptIncoming.
 *
 * Merges test/merge-abort-conflict using AcceptIncoming strategy from a fresh state,
 * verifying the strategy is applied correctly.
 */
import {
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
  setupTestClean,
  sleep,
} from "../helpers/app.js";

describe("Merge Resolution Strategies", () => {
  before(async () => {
    await setupTestClean();
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
