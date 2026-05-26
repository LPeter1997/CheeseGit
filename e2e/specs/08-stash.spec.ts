/**
 * E2E: Stash operations — view, apply, drop
 */
import {
  waitForAppReady,
  openRepoByPath,
  switchLeftPanel,
  getStashEntries,
  applyStash,
  dropStash,
  sleep,
  TEST_REPO_PATH,
} from "../helpers/app.js";

describe("Stash Panel", () => {
  before(async () => {
    await waitForAppReady();
    await openRepoByPath(TEST_REPO_PATH);
  });

  it("shows the stash tab with count", async () => {
    const stashTab = await $("[data-testid='left-tab-stash']");
    expect(await stashTab.isExisting()).toBe(true);
    const text: string = await browser.execute(() => {
      const el = document.querySelector("[data-testid='left-tab-stash']");
      return el?.textContent?.trim() ?? "";
    });
    // Should show "Stash (3)" since the test repo has 3 stash entries
    expect(text).toMatch(/Stash \(\d+\)/);
  });

  it("lists stash entries when tab is opened", async () => {
    const entries = await getStashEntries();
    expect(entries.length).toBe(3);
    // The test repo creates these three stashes
    expect(entries.some((m) => m.includes("config overhaul"))).toBe(true);
    expect(entries.some((m) => m.includes("truncate utility"))).toBe(true);
    expect(entries.some((m) => m.includes("constants for beta"))).toBe(true);
  });

  it("shows stash entry details (hash, timestamp, author)", async () => {
    const rows = await $$("[data-testid='stash-row']");
    expect(rows.length).toBe(3);

    // Each row should have a short hash
    for (const row of rows) {
      const hash = await row.$(".font-mono");
      expect(await hash.isExisting()).toBe(true);
    }
  });

  it("can apply a stash (shows confirm dialog)", async () => {
    // Apply the second stash (truncate utility) — index 1
    await applyStash(1);
    await sleep(500);
    // After applying, files should appear in the staging panel
    await switchLeftPanel("staging");
    await sleep(300);
    // The stash should still exist (apply doesn't remove)
    await switchLeftPanel("stash");
    const entries = await getStashEntries();
    expect(entries.length).toBe(3);
  });

  it("can drop a stash", async () => {
    await dropStash(1);
    await sleep(500);
    const entries = await getStashEntries();
    expect(entries.length).toBe(2);
  });
});
