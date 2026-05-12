import { useState, useRef, useEffect } from "react";
import { useReposStore } from "../store";
import { useOpenRepo } from "../hooks/useOpenRepo";

export function TabBar() {
  const repos = useReposStore((s) => s.repos);
  const activeIndex = useReposStore((s) => s.activeIndex);
  const setActiveIndex = useReposStore((s) => s.setActiveIndex);
  const closeRepo = useReposStore((s) => s.closeRepo);

  return (
    <div className="flex h-10 items-stretch border-b border-border bg-bg-surface">
      {repos.map((repo, i) => (
        <div
          key={repo.path}
          className={`group flex cursor-pointer items-center gap-1.5 border-r border-border px-3 text-sm transition-colors ${
            i === activeIndex
              ? "bg-bg text-fg"
              : "text-fg-muted hover:bg-bg-hover"
          }`}
          onClick={() => setActiveIndex(i)}
        >
          <span className="max-w-40 truncate">{repo.name}</span>
          <button
            onClick={(e) => {
              e.stopPropagation();
              closeRepo(i);
            }}
            className="ml-1 opacity-0 transition-opacity hover:text-danger group-hover:opacity-100"
          >
            ×
          </button>
        </div>
      ))}

      <AddRepoButton />
    </div>
  );
}

function AddRepoButton() {
  const { browse } = useOpenRepo();
  const [open, setOpen] = useState(false);
  const ref = useRef<HTMLDivElement>(null);

  useEffect(() => {
    if (!open) return;
    const handler = (e: MouseEvent) => {
      if (ref.current && !ref.current.contains(e.target as Node)) {
        setOpen(false);
      }
    };
    document.addEventListener("mousedown", handler);
    return () => document.removeEventListener("mousedown", handler);
  }, [open]);

  return (
    <div className="relative" ref={ref}>
      <button
        onClick={() => setOpen(!open)}
        className="flex h-full items-center px-3 text-fg-muted transition-colors hover:bg-bg-hover hover:text-fg"
        title="Add repository"
      >
        +
      </button>

      {open && (
        <div className="absolute left-0 top-full z-50 mt-0.5 min-w-48 rounded-md border border-border bg-bg-surface py-1 shadow-lg">
          <button
            onClick={() => {
              browse();
              setOpen(false);
            }}
            className="w-full px-4 py-2 text-left text-sm text-fg hover:bg-bg-hover"
          >
            Open Repository…
          </button>
        </div>
      )}
    </div>
  );
}
