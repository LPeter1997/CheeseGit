/**
 * E2E: History cherry-pick flow.
 *
 * Covers:
 * - selecting multiple commits in history
 * - opening cherry-pick target picker
 * - selecting an existing branch target
 * - verifying commits are cherry-picked onto the target branch
 */
import { appendFile } from "fs/promises";
import path from "path";

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
  TEST_REPO_PATH,
} from "../helpers/app.js";

async function waitForAppReadyExtended() {
  await browser.waitUntil(
    async () => {
      const welcome = await $("[data-testid='welcome-panel']");
      const repo = await $("[data-testid='repo-view']");
      return (await welcome.isExisting()) || (await repo.isExisting());
    },
    { timeout: 45_000, timeoutMsg: "App did not become ready within 45 s" },
  );
}

async function createMainCommit(fileName: string, summary: string) {
  await switchLeftPanel("staging");
  await waitForStagingLoaded();

  await appendFile(path.join(TEST_REPO_PATH, fileName), `\n${summary}\n`, "utf8");

  await browser.waitUntil(
    async () => (await getUnstagedFiles()).includes(fileName),
    { timeout: 15_000, timeoutMsg: `Expected ${fileName} to appear in unstaged files` },
  );

  await stageAll();
  await setCommitSummary(summary);
  await clickCommit();
}

describe("History Cherry-Pick", () => {
  const stamp = Date.now();
  const summary1 = `test: cherry pick source one ${stamp}`;
  const summary2 = `test: cherry pick source two ${stamp}`;
  const file1 = `src/cherry-pick-e2e-${stamp}-one.txt`;
  const file2 = `src/cherry-pick-e2e-${stamp}-two.txt`;

  before(async () => {
    await waitForAppReadyExtended();
    await openRepoByPath(TEST_REPO_PATH);
    await switchBranch("main");
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

    expect(await getCurrentBranch()).toBe("feature/dark-mode");

    const commits = await getHistoryCommits();
    expect(commits.some((message) => message.includes(summary2))).toBe(true);
    expect(commits.some((message) => message.includes(summary1))).toBe(true);
  });
});
