/**
 * E2E: Options menu and theme switching (from repo view)
 */
import {
  openOptionsMenu,
  getActiveTheme,
  setupTest,
  sleep,
  jsClick,
  jsMoveTo,
} from "../helpers/app.js";

describe("Options Menu", () => {
  before(async () => {
    await setupTest();
  });

  it("opens the options menu via gear button", async () => {
    await openOptionsMenu();
    // The menu should appear
    const menu = await $("[data-testid='options-menu']");
    expect(await menu.isExisting()).toBe(true);
  });

  it("has a Theme submenu", async () => {
    // Hover over Theme item to open submenu
    const themeItem = await $("button=Theme");
    await jsMoveTo(themeItem);
    await sleep(300);

    // Submenu should show theme options
    const lightBtn = await $("button=Light");
    expect(await lightBtn.isExisting()).toBe(true);
  });

  it("changes theme via options menu", async () => {
    const lightBtn = await $("button=Light");
    await jsClick(lightBtn);
    await sleep(200);
    expect(await getActiveTheme()).toBe("light");
  });
});
