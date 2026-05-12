import { useEffect, useState } from "react";
import { commands } from "../../../ipc/bindings";

interface BranchBarProps {
  repoPath: string;
}

export function BranchBar({ repoPath }: BranchBarProps) {
  const [branch, setBranch] = useState<string | null>(null);

  useEffect(() => {
    let cancelled = false;

    async function fetch() {
      const result = await commands.getCurrentBranch(repoPath);
      if (!cancelled && result.status === "ok") {
        setBranch(result.data);
      }
    }

    fetch();
    return () => { cancelled = true; };
  }, [repoPath]);

  return (
    <div className="flex h-10 items-center border-b border-border bg-bg-surface px-3">
      <div className="flex items-center gap-2 text-sm">
        <svg
          className="h-4 w-4 text-fg-muted"
          viewBox="0 0 16 16"
          fill="currentColor"
        >
          <path d="M11.75 2.5a.75.75 0 1 0 0 1.5.75.75 0 0 0 0-1.5zm-2.25.75a2.25 2.25 0 1 1 3 2.122V6A2.5 2.5 0 0 1 10 8.5H6a1 1 0 0 0-1 1v1.128a2.251 2.251 0 1 1-1.5 0V5.372a2.25 2.25 0 1 1 1.5 0v1.836A2.492 2.492 0 0 1 6 7h4a1 1 0 0 0 1-1v-.628A2.25 2.25 0 0 1 9.5 3.25zM4.25 12a.75.75 0 1 0 0 1.5.75.75 0 0 0 0-1.5zM3.5 3.25a.75.75 0 1 1 1.5 0 .75.75 0 0 1-1.5 0z" />
        </svg>
        <span className="font-medium text-fg">
          {branch ?? "…"}
        </span>
      </div>
    </div>
  );
}
