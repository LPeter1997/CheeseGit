/**
 * E2E: Left panel tab navigation — staging, history, stash tabs
 */
import {
  waitForAppReady,
  openRepoByPath,
  switchLeftPanel,
  getActiveLeftTab,
  sleep,
  TEST_REPO_PATH,
} from "../helpers/app.js";

describe("Left Panel Tab Navigation", () => {
  before(async () => {
    await waitForAppReady();
    await openRepoByPath(TEST_REPO_PATH);
  });

  it("shows staging tab by default", async () => {
    const stagingTab = await $("[data-testid='left-tab-staging']");
    const cls = await stagingTab.getAttribute("class");
    expect(cls).toContain("border-accent");
  });

  it("switches to history tab", async () => {
    await switchLeftPanel("history");
    const historyTab = await $("[data-testid='left-tab-history']");
    const cls = await historyTab.getAttribute("class");
    expect(cls).toContain("border-accent");

    // History content should be visible
    const historyRows = await $$("[data-testid='history-row']");
    expect(historyRows.length).toBeGreaterThan(0);
  });

  it("switches to stash tab", async () => {
    await switchLeftPanel("stash");
    const stashTab = await $("[data-testid='left-tab-stash']");
    const cls = await stashTab.getAttribute("class");
    expect(cls).toContain("border-accent");

    // Stash content should be visible
    const stashRows = await $$("[data-testid='stash-row']");
    expect(stashRows.length).toBeGreaterThan(0);
  });

  it("switches back to staging tab", async () => {
    await switchLeftPanel("staging");
    const stagingTab = await $("[data-testid='left-tab-staging']");
    const cls = await stagingTab.getAttribute("class");
    expect(cls).toContain("border-accent");
  });

  it("stash tab shows count in label", async () => {
    const text: string = await browser.execute(() => {
      const el = document.querySelector("[data-testid='left-tab-stash']");
      return el?.textContent?.trim() ?? "";
    });
    expect(text).toMatch(/Stash \(\d+\)/);
  });
});
