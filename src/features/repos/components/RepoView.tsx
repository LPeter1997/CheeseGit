import type { RepoInfo } from "../../../ipc/bindings";
import { BranchBar } from "./BranchBar";
import { LeftPanel } from "./LeftPanel";
import { DiffPanel } from "./DiffPanel";

interface RepoViewProps {
  repo: RepoInfo;
}

export function RepoView({ repo }: RepoViewProps) {
  return (
    <div className="flex h-full flex-col">
      <BranchBar repoPath={repo.path} />
      <div className="flex flex-1 overflow-hidden">
        <div className="w-80 flex-shrink-0 border-r border-border overflow-hidden">
          <LeftPanel repoPath={repo.path} />
        </div>
        <div className="flex-1 overflow-auto">
          <DiffPanel />
        </div>
      </div>
    </div>
  );
}
