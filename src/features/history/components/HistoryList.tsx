import { formatRelativeDate } from "../../../shared/utils/format";
import { useHistoryStore } from "../store";

export function HistoryList() {
  const commits = useHistoryStore((s) => s.commits);
  const selectedIndex = useHistoryStore((s) => s.selectedIndex);
  const selectCommit = useHistoryStore((s) => s.selectCommit);
  const loading = useHistoryStore((s) => s.loading);

  if (loading) {
    return (
      <div className="flex h-full items-center justify-center text-xs text-fg-muted">
        Loading…
      </div>
    );
  }

  if (commits.length === 0) {
    return (
      <div className="flex h-full items-center justify-center text-xs text-fg-muted">
        No commits yet.
      </div>
    );
  }

  return (
    <div className="flex flex-col overflow-auto">
      {commits.map((commit, i) => (
        <button
          key={commit.hash}
          onClick={() => selectCommit(i)}
          className={`flex flex-col gap-0.5 border-b border-border px-3 py-2 text-left transition-colors ${
            i === selectedIndex
              ? "bg-accent/10 text-fg"
              : "text-fg hover:bg-bg-hover"
          }`}
        >
          <span className="truncate text-sm font-medium">{commit.summary}</span>
          <div className="flex items-center gap-2 text-xs text-fg-muted">
            <span>{commit.author}</span>
            <span>·</span>
            <span>{formatRelativeDate(new Date(commit.timestamp))}</span>
            <span className="ml-auto font-mono text-[10px]">{commit.short_hash}</span>
          </div>
        </button>
      ))}
    </div>
  );
}


