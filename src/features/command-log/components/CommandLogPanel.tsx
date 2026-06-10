import { useCommandLogStore } from "../store";
import { useMemo, useState, useCallback, useRef, useLayoutEffect, useEffect } from "react";
import type { CommandEntry } from "../../../ipc/bindings";

/** Fixed height (px) of a single command row. Used for windowed rendering. */
const ROW_HEIGHT = 29;
/** Extra rows rendered above/below the viewport to avoid blank flashes. */
const OVERSCAN = 8;

function CopyIcon() {
  return (
    <svg width="12" height="12" viewBox="0 0 16 16" fill="currentColor">
      <path d="M4 4v-2a2 2 0 0 1 2-2h6a2 2 0 0 1 2 2v6a2 2 0 0 1-2 2h-2v2a2 2 0 0 1-2 2H2a2 2 0 0 1-2-2V6a2 2 0 0 1 2-2h2zm2-2v2h2a2 2 0 0 1 2 2v2h2V2H6zM2 6v6h6V6H2z" />
    </svg>
  );
}

export function CommandLogPanel() {
  const entries = useCommandLogStore((s) => s.entries);
  const isOpen = useCommandLogStore((s) => s.isOpen);
  const toggle = useCommandLogStore((s) => s.toggle);
  const showBackground = useCommandLogStore((s) => s.showBackground);
  const toggleShowBackground = useCommandLogStore((s) => s.toggleShowBackground);
  const search = useCommandLogStore((s) => s.search);
  const setSearch = useCommandLogStore((s) => s.setSearch);
  const exportLog = useCommandLogStore((s) => s.exportLog);

  const [expandedId, setExpandedId] = useState<string | null>(null);
  const scrollRef = useRef<HTMLDivElement>(null);
  const prevCountRef = useRef(0);
  const [scrollTop, setScrollTop] = useState(0);
  const [viewportHeight, setViewportHeight] = useState(0);
  // Track whether the panel content should be in DOM (for close animation).
  const [visible, setVisible] = useState(isOpen);
  const [closing, setClosing] = useState(false);

  useEffect(() => {
    if (isOpen) {
      setVisible(true);
      setClosing(false);
    } else if (visible) {
      setClosing(true);
    }
  }, [isOpen]); // eslint-disable-line react-hooks/exhaustive-deps

  const handleAnimationEnd = useCallback(() => {
    if (closing) {
      setVisible(false);
      setClosing(false);
    }
  }, [closing]);

  const filteredEntries = useMemo(() => {
    const base = showBackground ? entries : entries.filter((e) => !e.is_background);
    const term = search.trim().toLowerCase();
    if (!term) return base;
    return base.filter(
      (e) =>
        e.command.toLowerCase().includes(term) ||
        e.cwd.toLowerCase().includes(term) ||
        e.stdout.toLowerCase().includes(term) ||
        e.stderr.toLowerCase().includes(term),
    );
  }, [entries, showBackground, search]);

  // Newest commands first.
  const orderedEntries = useMemo(() => [...filteredEntries].reverse(), [filteredEntries]);

  // When new entries arrive (prepended at the top), shift scrollTop down to
  // keep the user's current view stable.
  useLayoutEffect(() => {
    const el = scrollRef.current;
    if (!el) return;
    const count = orderedEntries.length;
    const added = count - prevCountRef.current;
    if (added > 0 && prevCountRef.current > 0 && el.scrollTop > 0) {
      el.scrollTop += added * ROW_HEIGHT;
    }
    prevCountRef.current = count;
  }, [orderedEntries]);

  // Measure the viewport once the panel mounts and keep it updated on resize.
  useLayoutEffect(() => {
    const el = scrollRef.current;
    if (!el) return;
    setViewportHeight(el.clientHeight);
    const observer = new ResizeObserver(() => setViewportHeight(el.clientHeight));
    observer.observe(el);
    return () => observer.disconnect();
  }, [visible]);

  const handleScroll = useCallback((e: React.UIEvent<HTMLDivElement>) => {
    setScrollTop(e.currentTarget.scrollTop);
  }, []);

  const handleToggleRow = useCallback((timestamp: string) => {
    setExpandedId((prev) => (prev === timestamp ? null : timestamp));
  }, []);

  // Windowed slice of rows actually rendered.
  const total = orderedEntries.length;
  const startIndex = Math.max(0, Math.floor(scrollTop / ROW_HEIGHT) - OVERSCAN);
  const visibleCount = Math.ceil((viewportHeight || 256) / ROW_HEIGHT) + OVERSCAN * 2;
  const endIndex = Math.min(total, startIndex + visibleCount);
  const topPad = startIndex * ROW_HEIGHT;
  const bottomPad = Math.max(0, (total - endIndex) * ROW_HEIGHT);
  const windowed = orderedEntries.slice(startIndex, endIndex);

  return (
    <div className="border-t border-border bg-bg-surface">
      <button
        onClick={toggle}
        data-testid="command-log-toggle"
        className="flex w-full items-center gap-2 px-3 py-1.5 text-xs text-fg-muted transition-colors hover:bg-bg-hover"
      >
        <span className={`transition-transform ${isOpen ? "rotate-180" : ""}`}>
          ▲
        </span>
        <span>Command Log</span>
        {filteredEntries.length > 0 && (
          <span data-testid="command-log-count" className="rounded bg-bg-hover px-1.5 py-0.5 text-[10px]">
            {filteredEntries.length}
          </span>
        )}
      </button>

      {visible && (
        <div
          data-testid="command-log-panel"
          style={{
            animation: closing
              ? "cmdlog-close 200ms ease-in forwards"
              : "cmdlog-open 200ms ease-out forwards",
          }}
          onAnimationEnd={handleAnimationEnd}
        >
          <div className="flex items-center gap-3 border-b border-border px-3 py-1">
            <input
              type="text"
              value={search}
              onChange={(e) => setSearch(e.target.value)}
              placeholder="Search commands…"
              data-testid="command-log-search"
              className="min-w-0 flex-1 rounded border border-border bg-bg px-2 py-0.5 text-[11px] text-fg placeholder:text-fg-muted focus:border-accent focus:outline-none"
            />
            <label className="flex shrink-0 items-center gap-1.5 text-[10px] text-fg-muted cursor-pointer select-none">
              <input
                type="checkbox"
                checked={showBackground}
                onChange={toggleShowBackground}
                className="rounded"
              />
              Show background commands
            </label>
            <button
              onClick={exportLog}
              data-testid="command-log-export"
              className="shrink-0 cursor-pointer rounded border border-border px-2 py-0.5 text-[10px] text-fg-muted transition-colors hover:bg-bg-hover"
              title="Export the full command log to a text file"
            >
              Export
            </button>
          </div>

          <div
            ref={scrollRef}
            onScroll={handleScroll}
            className="overflow-auto border-t border-border"
            style={{ maxHeight: "16rem" }}
          >
            {orderedEntries.length === 0 ? (
              <div className="px-3 py-4 text-center text-xs text-fg-muted">
                {search.trim()
                  ? "No commands match your search."
                  : "No commands recorded yet."}
              </div>
            ) : (
              <table className="w-full text-xs" style={{ tableLayout: "fixed" }}>
                <thead>
                  <tr className="sticky top-0 z-10 border-b border-border bg-bg-surface text-left text-fg-muted">
                    <th className="w-24 px-3 py-1.5 font-medium">Time</th>
                    <th className="px-3 py-1.5 font-medium">Command</th>
                    <th className="w-20 px-3 py-1.5 font-medium">Duration</th>
                    <th className="w-12 px-3 py-1.5 font-medium">Exit</th>
                  </tr>
                </thead>
                <tbody>
                  {topPad > 0 && (
                    <tr style={{ height: topPad }} aria-hidden>
                      <td colSpan={4} className="p-0" />
                    </tr>
                  )}
                  {windowed.map((entry) => (
                    <CommandRow
                      key={entry.timestamp}
                      entry={entry}
                      expanded={expandedId === entry.timestamp}
                      onToggle={handleToggleRow}
                    />
                  ))}
                  {bottomPad > 0 && (
                    <tr style={{ height: bottomPad }} aria-hidden>
                      <td colSpan={4} className="p-0" />
                    </tr>
                  )}
                </tbody>
              </table>
            )}
          </div>
        </div>
      )}
    </div>
  );
}

function formatCommandText(entry: CommandEntry): string {
  let text = `$ ${entry.command}\n`;
  text += `cwd: ${entry.cwd}\n`;
  text += `exit code: ${entry.exit_code}  (${entry.elapsed_ms} ms)\n`;
  if (entry.stdout) text += `\n--- stdout ---\n${entry.stdout}`;
  if (entry.stderr) text += `\n--- stderr ---\n${entry.stderr}`;
  return text;
}

function CommandRow({
  entry,
  expanded,
  onToggle,
}: {
  entry: CommandEntry;
  expanded: boolean;
  onToggle: (timestamp: string) => void;
}) {
  const [copied, setCopied] = useState(false);
  const time = new Date(entry.timestamp).toLocaleTimeString();
  const isError = entry.exit_code !== 0;

  const handleCopy = useCallback(
    (e: React.MouseEvent) => {
      e.stopPropagation();
      navigator.clipboard.writeText(formatCommandText(entry));
      setCopied(true);
      setTimeout(() => setCopied(false), 1500);
    },
    [entry],
  );

  return (
    <>
      <tr
        onClick={() => onToggle(entry.timestamp)}
        className={`cursor-pointer border-b border-border transition-colors hover:bg-bg-hover ${
          isError ? "text-danger" : "text-fg"
        } ${entry.is_background ? "opacity-60" : ""}`}
      >
        <td className="whitespace-nowrap px-3 py-1.5 text-fg-muted">{time}</td>
        <td className="truncate px-3 py-1.5 font-mono">{entry.command}</td>
        <td className="whitespace-nowrap px-3 py-1.5 text-fg-muted">{entry.elapsed_ms} ms</td>
        <td className="px-3 py-1.5">{entry.exit_code}</td>
      </tr>
      {expanded && (
        <tr className="border-b border-border">
          <td colSpan={4} className="px-3 py-2">
            <div className="mb-1 flex items-center justify-between">
              <span className="text-[10px] text-fg-muted">
                cwd: {entry.cwd}
              </span>
              <button
                onClick={handleCopy}
                className="flex items-center gap-1 cursor-pointer rounded px-1.5 py-0.5 text-[10px] text-fg-muted opacity-70 transition-opacity hover:opacity-100"
                title="Copy command details"
              >
                <CopyIcon />
                {copied ? (
                  <span
                    className="text-fg"
                    style={{ animation: "alert-copied-fade 1.5s ease-out forwards" }}
                  >
                    Copied!
                  </span>
                ) : (
                  <span>Copy</span>
                )}
              </button>
            </div>
            {entry.stdout && (
              <div className="mb-1">
                <div className="text-[10px] font-medium text-fg-muted">
                  stdout
                </div>
                <pre className="mt-0.5 max-h-32 overflow-auto whitespace-pre-wrap rounded bg-bg px-2 py-1 font-mono text-fg">
                  {entry.stdout}
                </pre>
              </div>
            )}
            {entry.stderr && (
              <div>
                <div className="text-[10px] font-medium text-fg-muted">
                  stderr
                </div>
                <pre className="mt-0.5 max-h-32 overflow-auto whitespace-pre-wrap rounded bg-bg px-2 py-1 font-mono text-danger">
                  {entry.stderr}
                </pre>
              </div>
            )}
            {!entry.stdout && !entry.stderr && (
              <span className="text-fg-muted">No output</span>
            )}
          </td>
        </tr>
      )}
    </>
  );
}
