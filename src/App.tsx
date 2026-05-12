import { useReposStore, WelcomePanel, TabBar, RepoView } from "./features/repos";
import { CommandLogPanel } from "./features/command-log";
import { ToastContainer } from "./shared/components/ToastContainer";

export function App() {
  const repos = useReposStore((s) => s.repos);
  const activeIndex = useReposStore((s) => s.activeIndex);
  const activeRepo = repos[activeIndex] ?? null;

  if (repos.length === 0) {
    return (
      <div className="flex h-full flex-col">
        <div className="flex-1 overflow-auto">
          <WelcomePanel />
        </div>
        <CommandLogPanel />
        <ToastContainer />
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
      <ToastContainer />
    </div>
  );
}
