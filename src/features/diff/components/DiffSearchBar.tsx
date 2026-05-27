import { useEffect, useRef } from "react";

export interface DiffSearchBarProps {
  query: string;
  onQueryChange: (query: string) => void;
  currentIndex: number;
  totalMatches: number;
  onNext: () => void;
  onPrevious: () => void;
  isSearching?: boolean;
  focusSignal?: number;
}

export function DiffSearchBar({
  query,
  onQueryChange,
  currentIndex,
  totalMatches,
  onNext,
  onPrevious,
  isSearching,
  focusSignal = 0,
}: DiffSearchBarProps) {
  const inputRef = useRef<HTMLInputElement>(null);

  useEffect(() => {
    if (!inputRef.current) return;
    inputRef.current.focus();
    inputRef.current.select();
  }, [focusSignal]);

  const handleChange = (e: React.ChangeEvent<HTMLInputElement>) => {
    const newValue = e.currentTarget.value;
    onQueryChange(newValue);
  };

  const handleKeyDown = (e: React.KeyboardEvent<HTMLInputElement>) => {
    if (e.key === "Escape") {
      e.preventDefault();
      onQueryChange("");
    } else if (e.key === "Enter") {
      e.preventDefault();
      if (e.shiftKey) {
        onPrevious();
      } else {
        onNext();
      }
    }
  };

  const statusText = !query
    ? ""
    : isSearching
      ? "Searching…"
      : totalMatches === 0
        ? "No matches"
        : `${currentIndex + 1} of ${totalMatches}`;

  return (
    <div className="flex items-center gap-1">
      <span
        aria-live="polite"
        className="w-20 text-right text-xs tabular-nums text-fg-muted"
      >
        {statusText || "\u00A0"}
      </span>

      <input
        ref={inputRef}
        data-testid="diff-search-input"
        autoFocus
        type="text"
        value={query}
        onChange={handleChange}
        onKeyDown={handleKeyDown}
        placeholder="Search diff…"
        className="w-40 shrink-0 rounded border border-border bg-bg px-2 py-0.5 text-xs text-fg placeholder:text-fg-muted focus:border-accent focus:outline-none"
      />

      <button
        onClick={onPrevious}
        title="Previous match (Shift+Return)"
        disabled={!query || totalMatches === 0 || !!isSearching}
        className="cursor-pointer rounded px-1.5 py-0.5 text-fg-muted hover:bg-bg-hover hover:text-fg disabled:cursor-not-allowed disabled:opacity-40 transition-colors"
      >
        ↑
      </button>
      <button
        onClick={onNext}
        title="Next match (Return)"
        disabled={!query || totalMatches === 0 || !!isSearching}
        className="cursor-pointer rounded px-1.5 py-0.5 text-fg-muted hover:bg-bg-hover hover:text-fg disabled:cursor-not-allowed disabled:opacity-40 transition-colors"
      >
        ↓
      </button>
    </div>
  );
}
