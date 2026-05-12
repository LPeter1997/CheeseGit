import { useOpenRepo } from "../hooks/useOpenRepo";
import { ThemeSelector } from "../../theme";

export function WelcomePanel() {
  const { browse, loading } = useOpenRepo();

  return (
    <div className="flex h-full items-center justify-center">
      <div className="flex flex-col items-center gap-6 text-center">
        <h1 className="text-4xl font-bold text-accent">CheeseGit</h1>
        <p className="text-fg-muted">
          Open a repository to get started.
        </p>

        <button
          onClick={browse}
          disabled={loading}
          className="rounded-lg bg-accent px-6 py-2.5 font-medium text-accent-fg transition-colors hover:opacity-90 disabled:opacity-50"
        >
          {loading ? "Opening…" : "Open Repository"}
        </button>

        <ThemeSelector />
      </div>
    </div>
  );
}
