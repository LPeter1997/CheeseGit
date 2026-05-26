import { useState, useRef, useCallback, useMemo } from "react";
import {
  DndContext,
  closestCenter,
  PointerSensor,
  useSensor,
  useSensors,
  type DragStartEvent,
  type DragEndEvent,
} from "@dnd-kit/core";
import {
  SortableContext,
  horizontalListSortingStrategy,
  useSortable,
} from "@dnd-kit/sortable";
import { CSS } from "@dnd-kit/utilities";
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
  const [draggingId, setDraggingId] = useState<string | null>(null);

  // Use a small activation distance so clicks don't accidentally start drags.
  const sensors = useSensors(
    useSensor(PointerSensor, {
      activationConstraint: { distance: 5 },
    }),
  );

  // Stable array of unique IDs for SortableContext (repo paths are unique).
  const itemIds = useMemo(() => repos.map((r) => r.path), [repos]);

  const handleDragStart = useCallback((event: DragStartEvent) => {
    setDraggingId(String(event.active.id));
  }, []);

  const handleDragEnd = useCallback(
    (event: DragEndEvent) => {
      setDraggingId(null);
      const { active, over } = event;
      if (over && active.id !== over.id) {
        const oldIndex = repos.findIndex((r) => r.path === active.id);
        const newIndex = repos.findIndex((r) => r.path === over.id);
        if (oldIndex !== -1 && newIndex !== -1) {
          moveRepo(oldIndex, newIndex);
        }
      }
    },
    [repos, moveRepo],
  );

  return (
    <div className="flex h-9 items-stretch border-b border-border bg-bg-surface">
      <DndContext
        sensors={sensors}
        collisionDetection={closestCenter}
        onDragStart={handleDragStart}
        onDragEnd={handleDragEnd}
      >
        <SortableContext items={itemIds} strategy={horizontalListSortingStrategy}>
          {repos.map((repo, i) => (
            <SortableTab
              key={repo.path}
              id={repo.path}
              name={repo.name}
              path={repo.path}
              isActive={i === activeIndex}
              onActivate={() => setActiveIndex(i)}
              onClose={() => closeRepo(i)}
            />
          ))}
        </SortableContext>
      </DndContext>

      <AddRepoButton />

      {/* Draggable title bar region filling remaining space */}
      <div
        {...(draggingId === null ? { "data-tauri-drag-region": true } : {})}
        className="min-w-4 flex-1"
      />

      <WindowControls />
    </div>
  );
}

function SortableTab({
  id,
  name,
  path,
  isActive,
  onActivate,
  onClose,
}: {
  id: string;
  name: string;
  path: string;
  isActive: boolean;
  onActivate: () => void;
  onClose: () => void;
}) {
  const {
    attributes,
    listeners,
    setNodeRef,
    transform,
    transition,
    isDragging,
  } = useSortable({ id });

  const style: React.CSSProperties = {
    transform: CSS.Transform.toString(transform),
    transition,
    zIndex: isDragging ? 10 : undefined,
    position: "relative",
  };

  return (
    <div
      ref={setNodeRef}
      style={style}
      {...attributes}
      {...listeners}
      data-testid="repo-tab"
      className={`group flex w-44 min-w-0 shrink cursor-pointer items-center gap-2 border-r border-border px-4 text-sm transition-colors ${
        isActive
          ? "bg-bg text-fg"
          : "text-fg-muted hover:bg-bg-hover"
      } ${isDragging ? "opacity-60 shadow-lg" : ""}`}
      title={path}
      onClick={onActivate}
    >
      <span className="min-w-0 flex-1 truncate">{name}</span>
      <button
        onClick={(e) => {
          e.stopPropagation();
          onClose();
        }}
        className="shrink-0 cursor-pointer px-1 opacity-0 transition-opacity hover:text-danger group-hover:opacity-100"
        data-testid="close-tab"
      >
        ×
      </button>
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
        data-testid="add-repo-button"
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
            data-testid="menu-create-repo"
            className="w-full cursor-pointer px-4 py-2 text-left text-sm text-fg hover:bg-bg-hover"
          >
            Create New Repository…
          </button>
          <button
            onClick={() => {
              browse();
              setOpen(false);
            }}
            data-testid="menu-open-repo"
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
