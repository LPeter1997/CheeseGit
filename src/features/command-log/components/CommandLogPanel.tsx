import { useCommandLogStore } from "../store";

export function CommandLogPanel() {
  const entries = useCommandLogStore((s) => s.entries);
  const isOpen = useCommandLogStore((s) => s.isOpen);
  const toggle = useCommandLogStore((s) => s.toggle);

  return (
    <div className="border-t border-border bg-bg-surface">
      <button
        onClick={toggle}
        className="flex w-full items-center gap-2 px-3 py-1.5 text-xs text-fg-muted transition-colors hover:bg-bg-hover"
      >
        <span className={`transition-transform ${isOpen ? "rotate-180" : ""}`}>
          ▲
        </span>
        <span>Command Log</span>
        {entries.length > 0 && (
          <span className="rounded bg-bg-hover px-1.5 py-0.5 text-[10px]">
            {entries.length}
          </span>
        )}
      </button>

      {isOpen && (
        <div className="max-h-64 overflow-auto border-t border-border">
          {entries.length === 0 ? (
            <div className="px-3 py-4 text-center text-xs text-fg-muted">
              No commands recorded yet.
            </div>
          ) : (
            <table className="w-full text-xs">
              <thead>
                <tr className="sticky top-0 border-b border-border bg-bg-surface text-left text-fg-muted">
                  <th className="px-3 py-1.5 font-medium">Time</th>
                  <th className="px-3 py-1.5 font-medium">Command</th>
                  <th className="px-3 py-1.5 font-medium">CWD</th>
                  <th className="px-3 py-1.5 font-medium">Exit</th>
                </tr>
              </thead>
              <tbody>
                {[...entries].reverse().map((entry, i) => (
                  <CommandRow key={i} entry={entry} />
                ))}
              </tbody>
            </table>
          )}
        </div>
      )}
    </div>
  );
}

import { useState } from "react";
import type { CommandEntry } from "../../../ipc/bindings";

function CommandRow({ entry }: { entry: CommandEntry }) {
  const [expanded, setExpanded] = useState(false);
  const time = new Date(entry.timestamp).toLocaleTimeString();
  const isError = entry.exit_code !== 0;

  return (
    <>
      <tr
        onClick={() => setExpanded(!expanded)}
        className={`cursor-pointer border-b border-border transition-colors hover:bg-bg-hover ${
          isError ? "text-danger" : "text-fg"
        }`}
      >
        <td className="whitespace-nowrap px-3 py-1.5 text-fg-muted">{time}</td>
        <td className="px-3 py-1.5 font-mono">{entry.command}</td>
        <td className="max-w-48 truncate px-3 py-1.5 text-fg-muted">{entry.cwd}</td>
        <td className="px-3 py-1.5">{entry.exit_code}</td>
      </tr>
      {expanded && (
        <tr className="border-b border-border">
          <td colSpan={4} className="px-3 py-2">
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
