/**
 * E2E: Theme switching
 */
import {
  setThemeViaWelcome,
  getActiveTheme,
  setupTestNoRepo,
  sleep,
} from "../helpers/app.js";

describe("Theme Switching", () => {
  before(async () => {
    await setupTestNoRepo();
  });

  it("switches to light theme", async () => {
    await setThemeViaWelcome("light");
    expect(await getActiveTheme()).toBe("light");
  });

  it("switches to dark theme", async () => {
    await setThemeViaWelcome("dark");
    expect(await getActiveTheme()).toBe("dark");
  });

  it("switches to high-contrast theme", async () => {
    await setThemeViaWelcome("high-contrast");
    expect(await getActiveTheme()).toBe("high-contrast");
  });

  it("switches back to system theme", async () => {
    await setThemeViaWelcome("system");
    // System theme may resolve to light/dark, or remain as "system" in the attribute
    const theme = await getActiveTheme();
    expect(["light", "dark", "system"]).toContain(theme);
  });

  it("active theme button has accent styling", async () => {
    await setThemeViaWelcome("dark");
    const darkBtn = await $("[data-testid='theme-dark']");
    const cls = await darkBtn.getAttribute("class");
    expect(cls).toContain("bg-accent");
  });
});
