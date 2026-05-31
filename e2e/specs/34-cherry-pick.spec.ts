/**
 * E2E: History cherry-pick flow.
 *
 * Covers:
 * - selecting multiple commits in history
 * - opening cherry-pick target picker
 * - selecting an existing branch target
 * - verifying commits are cherry-picked onto the target branch
 */
import {
  openRepoByPath,
  waitForStagingLoaded,
  switchLeftPanel,
  switchBranch,
  getCurrentBranch,
  getUnstagedFiles,
  stageAll,
  setCommitSummary,
  clickCommit,
  getHistoryCommits,
  clickHistoryCommit,
  ctrlClickHistoryCommit,
  clickHistoryCherryPick,
  selectCherryPickBranch,
  setupTestClean,
  appendTestFile,
  sleep,
  TEST_REPO_PATH,
} from "../helpers/app.js";

describe("History Cherry-Pick", () => {
  async function createMainCommit(fileName: string, summary: string) {
    await switchLeftPanel("staging");
    await waitForStagingLoaded();

    appendTestFile(fileName, `\n${summary}\n`);

    await browser.waitUntil(
      async () => (await getUnstagedFiles()).includes(fileName),
      { timeout: 15_000, timeoutMsg: `Expected ${fileName} to appear in unstaged files` },
    );

    await stageAll();
    await setCommitSummary(summary);
    await clickCommit();
  }

  const stamp = Date.now();
  const summary1 = `test: cherry pick source one ${stamp}`;
  const summary2 = `test: cherry pick source two ${stamp}`;
  const file1 = `src/cherry-pick-e2e-${stamp}-one.txt`;
  const file2 = `src/cherry-pick-e2e-${stamp}-two.txt`;

  before(async () => {
    await setupTestClean();
  });

  it("cherry-picks two selected commits to an existing branch", async () => {
    await createMainCommit(file1, summary1);
    await createMainCommit(file2, summary2);

    await switchLeftPanel("history");

    // Select latest two commits with multi-select behavior.
    await clickHistoryCommit(0);
    await ctrlClickHistoryCommit(1);

    await clickHistoryCherryPick();
    await selectCherryPickBranch("feature/dark-mode");

    // Wait for the cherry-pick operation to complete — it checks out the
    // target branch and applies commits, which can take several seconds.
    await browser.waitUntil(
      async () => (await getCurrentBranch()) === "feature/dark-mode",
      { timeout: 15_000, timeoutMsg: "Expected branch to switch to feature/dark-mode after cherry-pick" },
    );

    const commits = await getHistoryCommits();
    expect(commits.some((message) => message.includes(summary2))).toBe(true);
    expect(commits.some((message) => message.includes(summary1))).toBe(true);
  });
});
