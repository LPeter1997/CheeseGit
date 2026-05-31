/**
 * E2E: Command log panel
 */
import {
  toggleCommandLog,
  isCommandLogOpen,
  getCommandLogEntryCount,
  setupTest,
  sleep,
} from "../helpers/app.js";

describe("Command Log Panel", () => {
  before(async () => {
    await setupTest();
  });

  it("command log toggle button exists", async () => {
    const btn = await $("[data-testid='command-log-toggle']");
    expect(await btn.isExisting()).toBe(true);
    // Should contain "Command Log" text
    const text: string = await browser.execute(() => {
      const el = document.querySelector("[data-testid='command-log-toggle']");
      return el?.textContent?.trim() ?? "";
    });
    expect(text).toContain("Command Log");
  });

  it("opens the command log panel", async () => {
    await toggleCommandLog();
    expect(await isCommandLogOpen()).toBe(true);
  });

  it("shows recorded git commands", async () => {
    const count = await getCommandLogEntryCount();
    // Opening a repo triggers several git commands (status, branch, log, etc.)
    expect(count).toBeGreaterThan(0);
  });

  it("displays command log table with columns", async () => {
    const panel = await $("[data-testid='command-log-panel']");
    const headerTexts: string[] = await browser.execute(() => {
      const headers = document.querySelectorAll("[data-testid='command-log-panel'] th");
      return Array.from(headers).map((h) => h.textContent?.trim() ?? "");
    });
    expect(headerTexts).toContain("Time");
    expect(headerTexts).toContain("Command");
    expect(headerTexts).toContain("Duration");
    expect(headerTexts).toContain("Exit");
  });

  it("shows git commands in the entries", async () => {
    const panel = await $("[data-testid='command-log-panel']");
    const firstCmd: string = await browser.execute(() => {
      const cells = document.querySelectorAll("[data-testid='command-log-panel'] td.font-mono");
      return cells.length > 0 ? (cells[0].textContent?.trim() ?? "") : "";
    });
    expect(firstCmd).toContain("git");
  });

  it("closes the command log panel", async () => {
    await toggleCommandLog();
    await sleep(300);
    expect(await isCommandLogOpen()).toBe(false);
  });
});
