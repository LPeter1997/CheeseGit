import { create } from "zustand";
import { getAppStateSafe, saveAppStateSafe } from "../../shared/utils/app-state";

export type ThemeChoice = "system" | "light" | "dark" | "high-contrast";

interface ThemeState {
  theme: ThemeChoice;
  setTheme: (theme: ThemeChoice) => void;
  previewTheme: (theme: ThemeChoice | null) => void;
}

function isThemeChoice(value: unknown): value is ThemeChoice {
  return value === "system" || value === "light" || value === "dark" || value === "high-contrast";
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

async function persistThemeToAppState(theme: ThemeChoice) {
  const state = await getAppStateSafe();
  if (state.theme === theme) return;
  await saveAppStateSafe({ ...state, theme });
}

export async function hydrateThemeFromAppState() {
  const state = await getAppStateSafe();
  if (!isThemeChoice(state.theme)) return;

  const persistedTheme = state.theme;
  const currentTheme = useThemeStore.getState().theme;
  if (persistedTheme === currentTheme) return;

  applyTheme(persistedTheme);
  localStorage.setItem("cheesegit-theme", persistedTheme);
  useThemeStore.setState({ theme: persistedTheme });
}

export const useThemeStore = create<ThemeState>((set, get) => ({
  theme: initialTheme,

  setTheme: (theme: ThemeChoice) => {
    applyTheme(theme);
    localStorage.setItem("cheesegit-theme", theme);
    set({ theme });
    void persistThemeToAppState(theme);
  },

  previewTheme: (theme: ThemeChoice | null) => {
    if (theme === null) {
      applyTheme(get().theme);
    } else {
      applyTheme(theme);
    }
  },
}));
