import { describe, it, expect, beforeEach } from "vitest";

import { useThemeStore } from "./store";

function resetStore() {
  document.documentElement.removeAttribute("data-theme");
  localStorage.removeItem("cheesegit-theme");
  useThemeStore.setState({ theme: "system", appliedTheme: "system" });
}

describe("useThemeStore", () => {
  beforeEach(() => {
    resetStore();
  });

  it("defaults to system theme", () => {
    expect(useThemeStore.getState().theme).toBe("system");
  });

  it("setTheme applies data-theme attribute", () => {
    useThemeStore.getState().setTheme("dark");
    expect(document.documentElement.getAttribute("data-theme")).toBe("dark");
    expect(useThemeStore.getState().theme).toBe("dark");
  });

  it("setTheme removes data-theme for system", () => {
    useThemeStore.getState().setTheme("dark");
    useThemeStore.getState().setTheme("system");
    expect(document.documentElement.hasAttribute("data-theme")).toBe(false);
  });

  it("persists theme choice to localStorage", () => {
    useThemeStore.getState().setTheme("high-contrast");
    expect(localStorage.getItem("cheesegit-theme")).toBe("high-contrast");
  });

  it("supports all theme choices", () => {
    for (const theme of ["light", "dark", "high-contrast", "system"] as const) {
      useThemeStore.getState().setTheme(theme);
      expect(useThemeStore.getState().theme).toBe(theme);
    }
  });
});
