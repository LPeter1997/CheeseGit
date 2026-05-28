/**
 * E2E: Undo last commit from History row.
 *
 * Covers:
 * - Undo action on the latest history entry
 * - Restoring commit summary/description into commit form
 * - Restoring committed changes into staged files
 * - Hiding undo action while browsing history (detached HEAD)
 */
import { appendFile } from "fs/promises";
import path from "path";

import {
  waitForAppReady,
  openRepoByPath,
  waitForStagingLoaded,
  switchLeftPanel,
  getHistoryCommits,
  getStagedFiles,
  getUnstagedFiles,
  stageAll,
  unstageAll,
  setCommitSummary,
  setCommitDescription,
  clickCommit,
  jsClick,
  jsMoveTo,
  sleep,
  TEST_REPO_PATH,
} from "../helpers/app.js";

describe("Undo Last Commit", () => {
  const targetFile = "src/constants.ts";

  before(async () => {
    await waitForAppReady();
    await openRepoByPath(TEST_REPO_PATH);
    await switchLeftPanel("staging");
    await waitForStagingLoaded();
  });

  it("undoes the latest commit and restores message + staged changes", async () => {
    const stamp = Date.now();
    const summary = `test: undo commit e2e ${stamp}`;
    const description = `restores description after undo ${stamp}`;

    // Create a deterministic local change so this spec doesn't depend on prior suite state.
    await appendFile(path.join(TEST_REPO_PATH, targetFile), `\n// e2e-undo-${stamp}\n`, "utf8");

    await browser.waitUntil(
      async () => (await getUnstagedFiles()).includes(targetFile),
      { timeout: 15_000, timeoutMsg: `Expected ${targetFile} to appear in unstaged files` },
    );

    await stageAll();
    await browser.waitUntil(
      async () => (await getStagedFiles()).includes(targetFile),
      { timeout: 10_000, timeoutMsg: `Expected ${targetFile} to appear in staged files` },
    );

    await setCommitSummary(summary);
    await setCommitDescription(description);
    await clickCommit();

    await switchLeftPanel("history");
    await sleep(400);

    const commits = await getHistoryCommits();
    expect(commits[0]).toContain(`undo commit e2e ${stamp}`);

    const firstRow = (await $$("[data-testid='history-row']"))[0];
    await jsMoveTo(firstRow);
    await sleep(200);

    const undoBtn = await firstRow.$("[data-testid='undo-last-commit-button']");
    await jsClick(undoBtn);

    // Undo handler switches back to staging after success.
    await switchLeftPanel("staging");
    await waitForStagingLoaded();

    const restored = await browser.execute(() => {
      const summaryInput = document.querySelector("[data-testid='commit-summary']") as HTMLInputElement | null;
      const descInput = document.querySelector("[data-testid='commit-description']") as HTMLTextAreaElement | null;
      return {
        summary: summaryInput?.value ?? "",
        description: descInput?.value ?? "",
      };
    });

    expect(restored.summary).toBe(summary);
    expect(restored.description).toBe(description);

    const staged = await getStagedFiles();
    const unstaged = await getUnstagedFiles();

    expect(staged).toContain(targetFile);
    expect(unstaged).not.toContain(targetFile);

    // Leave repo in a friendlier state for later specs (staged -> unstaged).
    await unstageAll();
  });

  it("shows undo action only on the head row", async () => {
    await switchLeftPanel("history");
    await sleep(300);

    const rows = await $$("[data-testid='history-row']");
    expect(rows.length).toBeGreaterThan(1);

    const firstRowUndo = await rows[0].$("[data-testid='undo-last-commit-button']");
    expect(await firstRowUndo.isExisting()).toBe(true);

    const secondRowUndo = await rows[1].$("[data-testid='undo-last-commit-button']");
    expect(await secondRowUndo.isExisting()).toBe(false);

    await switchLeftPanel("staging");
  });
});
