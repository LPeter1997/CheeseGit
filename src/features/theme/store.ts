import { create } from "zustand";
import { listen } from "@tauri-apps/api/event";

export type ThemeChoice = "system" | "light" | "dark" | "high-contrast";

interface ThemeState {
  theme: ThemeChoice;
  setTheme: (theme: ThemeChoice) => void;
}

function applyTheme(theme: ThemeChoice) {
  if (theme === "system") {
    document.documentElement.removeAttribute("data-theme");
  } else {
    document.documentElement.setAttribute("data-theme", theme);
  }
}

function loadStoredTheme(): ThemeChoice {
  const stored = localStorage.getItem("cheesegit-theme");
  if (
    stored === "system" ||
    stored === "light" ||
    stored === "dark" ||
    stored === "high-contrast"
  ) {
    return stored;
  }
  return "system";
}

const initialTheme = loadStoredTheme();
applyTheme(initialTheme);

export const useThemeStore = create<ThemeState>((set) => ({
  theme: initialTheme,

  setTheme: (theme: ThemeChoice) => {
    applyTheme(theme);
    localStorage.setItem("cheesegit-theme", theme);
    set({ theme });
  },
}));

// Listen for theme changes from the system menu bar.
listen<string>("set-theme", (event) => {
  useThemeStore.getState().setTheme(event.payload as ThemeChoice);
});
