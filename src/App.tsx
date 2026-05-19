import { useEffect, useRef } from "react";
import { useReposStore, WelcomePanel, TabBar, RepoView } from "./features/repos";
import { CommandLogPanel } from "./features/command-log";
import { AlertBanners } from "./shared/components/AlertBanners";
import { WindowControls } from "./shared/components/WindowControls";
import { useDiffStore } from "./features/diff/store";
import { useHistoryStore } from "./features/history";
import { useStagingStore } from "./features/staging";
import { useAlertStore } from "./shared/stores/alerts";
import { useUpdater, WhatsNewDialog, showWhatsNew } from "./features/updater";

export function App() {
  const repos = useReposStore((s) => s.repos);
  const activeIndex = useReposStore((s) => s.activeIndex);
  const initialized = useReposStore((s) => s.initialized);
  const initialize = useReposStore((s) => s.initialize);
  const activeRepo = repos[activeIndex] ?? null;
  const prevRepoPathRef = useRef<string | null>(null);

  // Check for updates on startup (production only).
  useUpdater();

  useEffect(() => {
    initialize();
  }, [initialize]);

  // Save/restore per-repo state when switching tabs.
  useEffect(() => {
    const newPath = activeRepo?.path ?? null;
    if (prevRepoPathRef.current !== newPath && newPath !== null) {
      useDiffStore.getState().switchRepo(prevRepoPathRef.current, newPath);
      useHistoryStore.getState().switchRepo(prevRepoPathRef.current, newPath);
      useStagingStore.getState().switchRepo(prevRepoPathRef.current, newPath);
    }
    prevRepoPathRef.current = newPath;
  }, [activeRepo?.path]);

  // Dev-mode shortcut: Ctrl+Shift+U triggers a fake update banner.
  // Dev-mode shortcut: Ctrl+Shift+L triggers the "What's new" dialog.
  useEffect(() => {
    if (!import.meta.env.DEV) return;
    const handler = (e: KeyboardEvent) => {
      if (e.ctrlKey && e.shiftKey && e.key === "U") {
        useAlertStore.getState().addUpdateAlert("99.0.0");
      }
      if (e.ctrlKey && e.shiftKey && e.key === "L") {
        showWhatsNew("0.0.0-dev", "This is a **test** changelog entry.\n\n- Feature one\n- Feature two\n- Bug fix");
      }
    };
    document.addEventListener("keydown", handler);
    return () => document.removeEventListener("keydown", handler);
  }, []);

  if (!initialized) {
    return null;
  }

  if (repos.length === 0) {
    return (
      <div className="flex h-full flex-col">
        <div className="flex h-9 items-stretch border-b border-border bg-bg-surface">
          <div data-tauri-drag-region className="flex-1" />
          <WindowControls />
        </div>
        <AlertBanners />
        <div className="flex-1 overflow-auto">
          <WelcomePanel />
        </div>
        <CommandLogPanel />
        <WhatsNewDialog />
      </div>
    );
  }

  return (
    <div className="flex h-full flex-col">
      <TabBar />
      <div className="flex-1 overflow-auto">
        {activeRepo && <RepoView repo={activeRepo} />}
      </div>
      <CommandLogPanel />
      <WhatsNewDialog />
    </div>
  );
}
