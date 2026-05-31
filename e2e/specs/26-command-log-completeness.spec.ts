/**
 * E2E: Command log completeness — verify commands are logged for operations.
 *
 * After opening a repo, the command log should contain entries for
 * the git commands triggered by the repo open.
 */
import {
  toggleCommandLog,
  isCommandLogOpen,
  getCommandLogEntryCount,
  setupTest,
  sleep,
} from "../helpers/app.js";

describe("Command Log Completeness", () => {
  before(async () => {
    await setupTest();
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
