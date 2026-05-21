import { useState, useRef, useCallback } from "react";
import { useReposStore } from "../store";
import { useOpenRepo } from "../hooks/useOpenRepo";
import { WindowControls } from "../../../shared/components/WindowControls";
import { CreateRepoDialog } from "./CreateRepoDialog";
import { useClickOutside } from "../../../shared/hooks/useClickOutside";
import { CloneRepoDialog } from "./CloneRepoDialog";

export function TabBar() {
  const repos = useReposStore((s) => s.repos);
  const activeIndex = useReposStore((s) => s.activeIndex);
  const setActiveIndex = useReposStore((s) => s.setActiveIndex);
  const closeRepo = useReposStore((s) => s.closeRepo);
  const moveRepo = useReposStore((s) => s.moveRepo);
  const [dragIndex, setDragIndex] = useState<number | null>(null);
  const [dropIndex, setDropIndex] = useState<number | null>(null);

  const handleDragStart = useCallback((e: React.DragEvent, index: number) => {
    setDragIndex(index);
    e.dataTransfer.effectAllowed = "move";
    e.dataTransfer.setData("text/plain", String(index));
  }, []);

  const handleDragOver = useCallback((e: React.DragEvent, index: number) => {
    e.preventDefault();
    e.dataTransfer.dropEffect = "move";
    setDropIndex(index);
  }, []);

  const handleDrop = useCallback((e: React.DragEvent, index: number) => {
    e.preventDefault();
    if (dragIndex !== null && dragIndex !== index) {
      moveRepo(dragIndex, index);
    }
    setDragIndex(null);
    setDropIndex(null);
  }, [dragIndex, moveRepo]);

  const handleDragEnd = useCallback(() => {
    setDragIndex(null);
    setDropIndex(null);
  }, []);

  return (
    <div className="flex h-9 items-stretch border-b border-border bg-bg-surface">
      {repos.map((repo, i) => (
        <div
          key={repo.path}
          draggable
          onDragStart={(e) => handleDragStart(e, i)}
          onDragOver={(e) => handleDragOver(e, i)}
          onDrop={(e) => handleDrop(e, i)}
          onDragEnd={handleDragEnd}
          className={`group flex w-44 min-w-0 shrink cursor-pointer items-center gap-2 border-r border-border px-4 text-sm transition-colors ${
            i === activeIndex
              ? "bg-bg text-fg"
              : "text-fg-muted hover:bg-bg-hover"
          } ${dragIndex === i ? "opacity-50" : ""} ${dropIndex === i && dragIndex !== i ? "border-l-2 border-l-accent" : ""}`}
          title={repo.path}
          onClick={() => setActiveIndex(i)}
        >
          <span className="min-w-0 flex-1 truncate">{repo.name}</span>
          <button
            onClick={(e) => {
              e.stopPropagation();
              closeRepo(i);
            }}
            className="shrink-0 cursor-pointer px-1 opacity-0 transition-opacity hover:text-danger group-hover:opacity-100"
          >
            ×
          </button>
        </div>
      ))}

      {/* Drop indicator for moving tabs to the end (between last tab and + button) */}
      <div
        className={`w-1 ${dropIndex === repos.length && dragIndex !== null ? "border-l-2 border-l-accent" : ""}`}
        onDragOver={(e) => { e.preventDefault(); e.dataTransfer.dropEffect = "move"; setDropIndex(repos.length); }}
        onDrop={(e) => { e.preventDefault(); if (dragIndex !== null && dragIndex !== repos.length - 1) { moveRepo(dragIndex, repos.length - 1); } setDragIndex(null); setDropIndex(null); }}
        onDragLeave={() => setDropIndex(null)}
      />

      <AddRepoButton />

      {/* Draggable title bar region + drop zone filling remaining space */}
      <div
        {...(dragIndex === null ? { "data-tauri-drag-region": true } : {})}
        className="min-w-4 flex-1"
        onDragOver={(e) => { e.preventDefault(); e.dataTransfer.dropEffect = "move"; setDropIndex(repos.length); }}
        onDrop={(e) => { e.preventDefault(); if (dragIndex !== null && dragIndex !== repos.length - 1) { moveRepo(dragIndex, repos.length - 1); } setDragIndex(null); setDropIndex(null); }}
        onDragLeave={() => setDropIndex(null)}
      />

      <WindowControls />
    </div>
  );
}

function AddRepoButton() {
  const { browse } = useOpenRepo();
  const [open, setOpen] = useState(false);
  const [createOpen, setCreateOpen] = useState(false);
  const [cloneOpen, setCloneOpen] = useState(false);
  const ref = useRef<HTMLDivElement>(null);

  const close = useCallback(() => setOpen(false), []);
  useClickOutside([ref], close, open);

  return (
    <div className="relative" ref={ref}>
      <button
        onClick={() => setOpen(!open)}
        className="flex h-full items-center px-4 text-base text-fg-muted transition-colors hover:bg-bg-hover hover:text-fg cursor-pointer"
        title="Add repository"
      >
        +
      </button>

      {open && (
        <div className="absolute left-0 top-full z-50 mt-0.5 min-w-48 rounded-md border border-border bg-bg-surface py-1 shadow-lg">
          <button
            onClick={() => {
              setCreateOpen(true);
              setOpen(false);
            }}
            className="w-full cursor-pointer px-4 py-2 text-left text-sm text-fg hover:bg-bg-hover"
          >
            Create New Repository…
          </button>
          <button
            onClick={() => {
              browse();
              setOpen(false);
            }}
            className="w-full cursor-pointer px-4 py-2 text-left text-sm text-fg hover:bg-bg-hover"
          >
            Open Existing Repository…
          </button>
          <button
            onClick={() => {
              setCloneOpen(true);
              setOpen(false);
            }}
            className="w-full cursor-pointer px-4 py-2 text-left text-sm text-fg hover:bg-bg-hover"
          >
            Clone Repository…
          </button>
        </div>
      )}

      <CreateRepoDialog open={createOpen} onClose={() => setCreateOpen(false)} />
      <CloneRepoDialog open={cloneOpen} onClose={() => setCloneOpen(false)} />
    </div>
  );
}
