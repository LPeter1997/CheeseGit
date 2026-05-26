/**
 * E2E: Multi-repo tab management — open multiple repos, switch between them.
 *
 * Tests that the app correctly handles multiple open repository tabs,
 * preserves per-repo state, and supports tab switching.
 */
import {
  waitForAppReady,
  openRepoByPath,
  waitForStagingLoaded,
  getOpenTabs,
  switchToTab,
  closeTab,
  getTabCount,
  getCurrentBranch,
  isWelcomeVisible,
  sleep,
  TEST_REPO_PATH,
} from "../helpers/app.js";
import path from "path";

describe("Multi-Repo Tab Management", () => {
  before(async () => {
    await waitForAppReady();
    // Ensure test repo is open
    await openRepoByPath(TEST_REPO_PATH);
    await waitForStagingLoaded();
  });

  it("has at least one repo tab open", async () => {
    const count = await getTabCount();
    expect(count).toBeGreaterThanOrEqual(1);
  });

  it("shows the repo name in tabs", async () => {
    const tabs = await getOpenTabs();
    expect(tabs.some((t) => t.includes(".test-repo"))).toBe(true);
  });

  it("can close a tab and return to welcome screen", async () => {
    // Close all tabs
    const tabs = await getOpenTabs();
    for (const tab of tabs) {
      await closeTab(tab);
    }
    await sleep(500);
    expect(await isWelcomeVisible()).toBe(true);
  });

  it("can reopen the repo after closing", async () => {
    await openRepoByPath(TEST_REPO_PATH);
    await waitForStagingLoaded();
    const count = await getTabCount();
    expect(count).toBe(1);
  });

  it("opening the same repo twice does not duplicate tabs", async () => {
    await openRepoByPath(TEST_REPO_PATH);
    await sleep(500);
    const count = await getTabCount();
    // Should still be 1 tab (switches to existing)
    expect(count).toBe(1);
  });
});
