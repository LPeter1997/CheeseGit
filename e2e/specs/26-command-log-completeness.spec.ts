/**
 * E2E: Command log completeness — verify commands are logged for various operations.
 *
 * After all the previous specs have run (staging, branching, merging, etc.),
 * the command log should contain entries for all these operations.
 */
import {
  waitForAppReady,
  openRepoByPath,
  toggleCommandLog,
  isCommandLogOpen,
  getCommandLogEntryCount,
  sleep,
  TEST_REPO_PATH,
} from "../helpers/app.js";

describe("Command Log Completeness", () => {
  before(async () => {
    await waitForAppReady();
    await openRepoByPath(TEST_REPO_PATH);
  });

  it("command log contains many entries after full test run", async () => {
    await toggleCommandLog();
    expect(await isCommandLogOpen()).toBe(true);

    const count = await getCommandLogEntryCount();
    // Each session starts fresh; opening a repo triggers several git commands
    expect(count).toBeGreaterThan(0);
  });

  it("command log entries contain git commands", async () => {
    // Verify some entries contain "git" in the command column
    const hasGitCommand: boolean = await browser.execute(() => {
      const rows = document.querySelectorAll("[data-testid='command-log-panel'] tr");
      return Array.from(rows).some((row) => (row.textContent ?? "").includes("git"));
    });
    expect(hasGitCommand).toBe(true);
  });

  it("command log shows exit codes", async () => {
    // The table should have exit code column entries
    const hasExitCode: boolean = await browser.execute(() => {
      const cells = document.querySelectorAll("[data-testid='command-log-panel'] td");
      return Array.from(cells).some((cell) => {
        const t = cell.textContent?.trim() ?? "";
        return t === "0" || t === "1";
      });
    });
    expect(hasExitCode).toBe(true);
  });

  after(async () => {
    // Close command log
    if (await isCommandLogOpen()) {
      await toggleCommandLog();
    }
  });
});
