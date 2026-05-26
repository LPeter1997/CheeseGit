/**
 * E2E: Welcome screen + app startup
 */
import {
  waitForAppReady,
  isWelcomeVisible,
  closeAllTabs,
  sleep,
  openRepoByPath,
  TEST_REPO_PATH,
} from "../helpers/app.js";

describe("Welcome Screen", () => {
  before(async () => {
    await waitForAppReady();
    // If app restored a previous session, close all tabs to get welcome screen
    await closeAllTabs();
    await sleep(300);
  });

  it("shows the welcome panel on first launch", async () => {
    expect(await isWelcomeVisible()).toBe(true);
  });

  it("displays CheeseGit heading", async () => {
    // Use browser.execute for reliable text extraction in WebKitWebDriver
    const text = await browser.execute(() => {
      const h1 = document.querySelector("h1");
      return h1?.textContent?.trim() ?? "";
    });
    expect(text).toBe("CheeseGit");
  });

  it("shows all three action buttons", async () => {
    const createBtn = await $("[data-testid='welcome-create-repo']");
    const openBtn = await $("[data-testid='welcome-open-repo']");
    const cloneBtn = await $("[data-testid='welcome-clone-repo']");

    expect(await createBtn.isExisting()).toBe(true);
    expect(await openBtn.isExisting()).toBe(true);
    expect(await cloneBtn.isExisting()).toBe(true);
  });

  it("shows theme selector with four options", async () => {
    const system = await $("[data-testid='theme-system']");
    const light = await $("[data-testid='theme-light']");
    const dark = await $("[data-testid='theme-dark']");
    const hc = await $("[data-testid='theme-high-contrast']");

    expect(await system.isExisting()).toBe(true);
    expect(await light.isExisting()).toBe(true);
    expect(await dark.isExisting()).toBe(true);
    expect(await hc.isExisting()).toBe(true);
  });
});
