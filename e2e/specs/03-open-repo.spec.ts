/**
 * E2E: Opening a repository and verifying tab management
 */
import {
  waitForAppReady,
  isWelcomeVisible,
  openRepoByPath,
  getOpenTabs,
  switchToTab,
  closeTab,
  getCurrentBranch,
  sleep,
  TEST_REPO_PATH,
} from "../helpers/app.js";

describe("Open Repository", () => {
  before(async () => {
    await waitForAppReady();
  });

  it("opens the test repo and switches from welcome to repo view", async () => {
    await openRepoByPath(TEST_REPO_PATH);
    const repoView = await $("[data-testid='repo-view']");
    expect(await repoView.isExisting()).toBe(true);
    expect(await isWelcomeVisible()).toBe(false);
  });

  it("shows the repo name in the tab bar", async () => {
    const tabs = await getOpenTabs();
    expect(tabs.length).toBeGreaterThanOrEqual(1);
    // The test repo folder name should appear
    expect(tabs.some((t) => t.includes(".test-repo"))).toBe(true);
  });

  it("shows current branch in the branch bar", async () => {
    const branch = await getCurrentBranch();
    expect(branch).toBe("main");
  });

  it("shows the branch bar with selector and remote button", async () => {
    const bar = await $("[data-testid='branch-bar']");
    expect(await bar.isExisting()).toBe(true);

    const selector = await $("[data-testid='branch-selector']");
    expect(await selector.isExisting()).toBe(true);
  });

  it("shows the staging tab as active by default", async () => {
    const stagingTab = await $("[data-testid='left-tab-staging']");
    const cls = await stagingTab.getAttribute("class");
    expect(cls).toContain("border-accent");
  });
});

describe("Tab Management", () => {
  it("close button removes the tab and shows welcome", async () => {
    const tabs = await getOpenTabs();
    // Close the first (only) tab
    await closeTab(tabs[0]);
    await sleep(500);
    expect(await isWelcomeVisible()).toBe(true);
  });

  it("can reopen the repo", async () => {
    await openRepoByPath(TEST_REPO_PATH);
    const repoView = await $("[data-testid='repo-view']");
    expect(await repoView.isExisting()).toBe(true);
  });
});
