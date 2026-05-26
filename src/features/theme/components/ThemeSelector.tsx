import { useThemeStore, type ThemeChoice } from "../store";

const themes: { value: ThemeChoice; label: string }[] = [
  { value: "system", label: "System" },
  { value: "light", label: "Light" },
  { value: "dark", label: "Dark" },
  { value: "high-contrast", label: "High Contrast" },
];

export function ThemeSelector() {
  const theme = useThemeStore((s) => s.theme);
  const setTheme = useThemeStore((s) => s.setTheme);

  return (
    <div className="flex items-center gap-1.5">
      {themes.map(({ value, label }) => (
        <button
          key={value}
          onClick={() => setTheme(value)}
          data-testid={`theme-${value}`}
          className={`rounded px-3 py-1 text-xs transition-colors ${
            theme === value
              ? "bg-accent text-accent-fg"
              : "text-fg-muted hover:bg-bg-hover hover:text-fg"
          }`}
        >
          {label}
        </button>
      ))}
    </div>
  );
}
