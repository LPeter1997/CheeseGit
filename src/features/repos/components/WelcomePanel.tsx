import { useState } from "react";
import { useOpenRepo } from "../hooks/useOpenRepo";
import { ThemeSelector } from "../../theme";
import { CreateRepoDialog } from "./CreateRepoDialog";
import { CloneRepoDialog } from "./CloneRepoDialog";

export function WelcomePanel() {
  const { browse, loading } = useOpenRepo();
  const [createOpen, setCreateOpen] = useState(false);
  const [cloneOpen, setCloneOpen] = useState(false);

  return (
    <div className="flex h-full items-center justify-center">
      <div className="flex flex-col items-center gap-6 text-center">
        <img src="/icon.png" alt="CheeseGit" className="h-20 w-20 rounded-2xl shadow-lg" />
        <h1 className="text-4xl font-bold text-accent">CheeseGit</h1>
        <p className="text-fg-muted">
          Open a repository to get started.
        </p>

        <div className="flex flex-col gap-2">
          <button
            onClick={() => setCreateOpen(true)}
            data-testid="welcome-create-repo"
            className="rounded-lg bg-accent px-6 py-2.5 font-medium text-accent-fg transition-colors hover:opacity-90 cursor-pointer"
          >
            Create New Repository
          </button>
          <button
            onClick={browse}
            disabled={loading}
            data-testid="welcome-open-repo"
            className="rounded-lg border border-border px-6 py-2.5 font-medium text-fg transition-colors hover:bg-bg-hover disabled:opacity-50 cursor-pointer"
          >
            {loading ? "Opening…" : "Open Existing Repository"}
          </button>
          <button
            onClick={() => setCloneOpen(true)}
            data-testid="welcome-clone-repo"
            className="rounded-lg border border-border px-6 py-2.5 font-medium text-fg transition-colors hover:bg-bg-hover cursor-pointer"
          >
            Clone Repository
          </button>
        </div>

        <ThemeSelector />
      </div>

      <CreateRepoDialog open={createOpen} onClose={() => setCreateOpen(false)} />
      <CloneRepoDialog open={cloneOpen} onClose={() => setCloneOpen(false)} />
    </div>
  );
}
