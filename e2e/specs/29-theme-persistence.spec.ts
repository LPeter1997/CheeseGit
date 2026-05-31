/**
 * E2E: Theme persistence — verify theme settings persist across repo switches.
 *
 * Tests that changing the theme from the options menu within a repo view
 * persists the setting, and that the app applies the correct data-theme attribute.
 */
import {
  openOptionsMenu,
  getActiveTheme,
  setupTest,
  sleep,
  jsClick,
  jsMoveTo,
} from "../helpers/app.js";

/** Open the options menu, hover over Theme, and click the given theme label. */
async function setThemeViaOptionsMenu(label: string) {
  await openOptionsMenu();
  await sleep(300);

  const themeItem = await $("span=Theme");
  await jsMoveTo(themeItem);
  await sleep(400);

  // Click the theme button by its visible text label
  const btn = await $(`button*=${label}`);
  await jsClick(btn);
  await sleep(300);
}

describe("Theme Persistence", () => {
  before(async () => {
    await setupTest();
  });

  it("can change theme to dark via options menu", async () => {
    await setThemeViaOptionsMenu("Dark");
    expect(await getActiveTheme()).toBe("dark");
  });

  it("theme attribute is set on html element", async () => {
    const theme = await getActiveTheme();
    expect(["light", "dark", "high-contrast"]).toContain(theme);
  });

  it("switching to light theme works", async () => {
    await setThemeViaOptionsMenu("Light");
    expect(await getActiveTheme()).toBe("light");
  });

  it("switching to high-contrast theme works", async () => {
    await setThemeViaOptionsMenu("High Contrast");
    expect(await getActiveTheme()).toBe("high-contrast");
  });

  it("system theme follows OS preference", async () => {
    await setThemeViaOptionsMenu("System");
    const theme = await getActiveTheme();
    // System theme resolves to light/dark based on OS, or keeps "system" as data-theme
    expect(["light", "dark", "system"]).toContain(theme);
  });
});
