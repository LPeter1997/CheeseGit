import { create } from "zustand";

export type ThemeChoice = "system" | "light" | "dark" | "high-contrast";

interface ThemeState {
  theme: ThemeChoice;
  setTheme: (theme: ThemeChoice) => void;
  previewTheme: (theme: ThemeChoice | null) => void;
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

export const useThemeStore = create<ThemeState>((set, get) => ({
  theme: initialTheme,

  setTheme: (theme: ThemeChoice) => {
    applyTheme(theme);
    localStorage.setItem("cheesegit-theme", theme);
    set({ theme });
  },

  previewTheme: (theme: ThemeChoice | null) => {
    if (theme === null) {
      applyTheme(get().theme);
    } else {
      applyTheme(theme);
    }
  },
}));
