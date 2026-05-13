import { useEffect, useState, useMemo, useRef, useCallback } from "react";
import { createHighlighter, type Highlighter, type ThemedToken } from "shiki";
import type { FileDiff, DiffLine } from "../../../ipc/bindings";
import type { DiffViewMode } from "../store";

/** Singleton highlighter instance (loaded lazily). */
let highlighterPromise: Promise<Highlighter> | null = null;

function getHighlighter(): Promise<Highlighter> {
  if (!highlighterPromise) {
    highlighterPromise = createHighlighter({
      themes: ["github-light", "github-dark"],
      langs: [],
    });
  }
  return highlighterPromise;
}

/** Map file extensions to Shiki language identifiers. */
function langFromPath(filePath: string): string {
  const ext = filePath.split(".").pop()?.toLowerCase() ?? "";
  const map: Record<string, string> = {
    ts: "typescript",
    tsx: "tsx",
    js: "javascript",
    jsx: "jsx",
    rs: "rust",
    json: "json",
    toml: "toml",
    yaml: "yaml",
    yml: "yaml",
    md: "markdown",
    css: "css",
    html: "html",
    py: "python",
    sh: "bash",
    fish: "fish",
    sql: "sql",
    xml: "xml",
    svg: "xml",
    go: "go",
    java: "java",
    kt: "kotlin",
    c: "c",
    cpp: "cpp",
    h: "c",
    hpp: "cpp",
    cs: "csharp",
    rb: "ruby",
    php: "php",
    swift: "swift",
    zig: "zig",
    lua: "lua",
    dockerfile: "dockerfile",
    makefile: "makefile",
  };
  return map[ext] ?? "text";
}

function detectTheme(): string {
  const ds = document.documentElement.dataset.theme;
  if (ds === "dark" || ds === "high-contrast") return "github-dark";
  if (ds === "light") return "github-light";
  return window.matchMedia("(prefers-color-scheme: dark)").matches
    ? "github-dark"
    : "github-light";
}

export interface TokenizedLine {
  tokens: ThemedToken[];
}

/** Hook that tokenizes content via Shiki and returns the per-line tokens + bg. */
export function useHighlightedLines(
  filePath: string,
  content: string,
): { lines: TokenizedLine[] | null; bg: string | undefined } {
  const [lines, setLines] = useState<TokenizedLine[] | null>(null);
  const [bg, setBg] = useState<string | undefined>(undefined);

  useEffect(() => {
    let cancelled = false;

    async function highlight() {
      const hl = await getHighlighter();
      const lang = langFromPath(filePath);

      if (lang !== "text") {
        const loaded = hl.getLoadedLanguages();
        if (!loaded.includes(lang)) {
          try {
            await hl.loadLanguage(
              lang as Parameters<typeof hl.loadLanguage>[0],
            );
          } catch {
            // Fall back to plain text.
          }
        }
      }

      if (cancelled) return;

      const theme = detectTheme();
      const effectiveLang =
        lang !== "text" && hl.getLoadedLanguages().includes(lang)
          ? lang
          : "text";

      const result = hl.codeToTokens(content, {
        lang: effectiveLang as Parameters<typeof hl.codeToTokens>[1]["lang"],
        theme,
      });
      const themeBg = hl.getTheme(theme).bg;

      if (!cancelled) {
        setLines(result.tokens.map((tokens) => ({ tokens })));
        setBg(themeBg);
      }
    }

    highlight();
    return () => {
      cancelled = true;
    };
  }, [filePath, content]);

  return { lines, bg };
}

/** Render a single line of tokens. */
function TokenLine({ tokens }: { tokens: ThemedToken[] }) {
  if (tokens.length === 0) return <>{"\n"}</>;
  return (
    <>
      {tokens.map((token, j) => (
        <span key={j} style={{ color: token.color }}>
          {token.content}
        </span>
      ))}
    </>
  );
}

// ── Unified View ────────────────────────────────────────────────

/**
 * Build a flat array of display rows from the diff hunks.
 * Each row maps to a line in the unified view with old/new line numbers.
 */
interface UnifiedRow {
  kind: "context" | "addition" | "deletion" | "hunk-header";
  content: string;
  oldLineno: number | null;
  newLineno: number | null;
  /** Index into the tokenized lines array for the relevant content. */
  tokenLineIndex: number | null;
}

function buildUnifiedRows(
  diff: FileDiff,
): UnifiedRow[] {
  const rows: UnifiedRow[] = [];

  for (const hunk of diff.hunks) {
    rows.push({
      kind: "hunk-header",
      content: hunk.header,
      oldLineno: null,
      newLineno: null,
      tokenLineIndex: null,
    });

    for (const line of hunk.lines) {
      const tokenIdx = findTokenLineIndex(line);
      rows.push({
        kind: line.kind === "Addition"
          ? "addition"
          : line.kind === "Deletion"
            ? "deletion"
            : "context",
        content: line.content,
        oldLineno: line.old_lineno,
        newLineno: line.new_lineno,
        tokenLineIndex: tokenIdx,
      });
    }
  }

  return rows;
}

/** Map a diff line to its position in the current file's tokenized lines. */
function findTokenLineIndex(line: DiffLine): number | null {
  // For additions and context, new_lineno maps to the current file.
  if (line.new_lineno !== null) return line.new_lineno - 1;
  // For deletions, we don't have a matching line in the current file.
  return null;
}

function UnifiedDiffView({
  diff,
  tokenizedLines,
  bg,
}: {
  diff: FileDiff;
  tokenizedLines: TokenizedLine[];
  bg: string | undefined;
}) {
  const rows = useMemo(
    () => buildUnifiedRows(diff),
    [diff],
  );

  const maxOld = rows.reduce(
    (m, r) => Math.max(m, r.oldLineno ?? 0),
    0,
  );
  const maxNew = rows.reduce(
    (m, r) => Math.max(m, r.newLineno ?? 0),
    0,
  );
  const gutterW = Math.max(String(Math.max(maxOld, maxNew)).length, 2);

  return (
    <div
      className="flex-1 overflow-auto text-sm leading-relaxed"
      style={{ backgroundColor: bg }}
    >
      <table className="w-full border-collapse font-mono">
        <tbody>
          {rows.map((row, i) => {
            if (row.kind === "hunk-header") {
              return (
                <tr key={i} className="bg-accent/10 leading-relaxed">
                  <td
                    colSpan={3}
                    className="select-none px-3 py-0.5 text-xs text-fg-muted"
                  >
                    {row.content}
                  </td>
                </tr>
              );
            }

            const bgClass =
              row.kind === "addition"
                ? "bg-success/15"
                : row.kind === "deletion"
                  ? "bg-danger/15"
                  : "";

            const tokens =
              row.tokenLineIndex !== null &&
              row.tokenLineIndex < tokenizedLines.length
                ? tokenizedLines[row.tokenLineIndex]
                : null;

            return (
              <tr key={i} className={`${bgClass} leading-relaxed`}>
                <td
                  className="select-none px-1.5 text-right align-top text-fg-muted opacity-50"
                  style={{ width: `${gutterW + 1}ch` }}
                >
                  {row.oldLineno ?? ""}
                </td>
                <td
                  className="select-none px-1.5 text-right align-top text-fg-muted opacity-50"
                  style={{ width: `${gutterW + 1}ch` }}
                >
                  {row.newLineno ?? ""}
                </td>
                <td className="whitespace-pre pr-3">
                  {tokens ? (
                    <TokenLine tokens={tokens.tokens} />
                  ) : (
                    row.content
                  )}
                </td>
              </tr>
            );
          })}
        </tbody>
      </table>
    </div>
  );
}

// ── Split View ──────────────────────────────────────────────────

interface SplitRow {
  left: {
    kind: "context" | "deletion" | "empty" | "hunk-header";
    content: string;
    lineno: number | null;
    tokenLineIndex: number | null;
  };
  right: {
    kind: "context" | "addition" | "empty" | "hunk-header";
    content: string;
    lineno: number | null;
    tokenLineIndex: number | null;
  };
}

function buildSplitRows(
  diff: FileDiff,
): SplitRow[] {
  const rows: SplitRow[] = [];

  for (const hunk of diff.hunks) {
    rows.push({
      left: {
        kind: "hunk-header",
        content: hunk.header,
        lineno: null,
        tokenLineIndex: null,
      },
      right: {
        kind: "hunk-header",
        content: hunk.header,
        lineno: null,
        tokenLineIndex: null,
      },
    });

    // Collect consecutive deletion/addition blocks to pair them side-by-side.
    const lines = hunk.lines;
    let li = 0;

    while (li < lines.length) {
      const line = lines[li];

      if (line.kind === "Context") {
        const tokenIdx = line.new_lineno !== null ? line.new_lineno - 1 : null;
        rows.push({
          left: {
            kind: "context",
            content: line.content,
            lineno: line.old_lineno,
            tokenLineIndex: tokenIdx,
          },
          right: {
            kind: "context",
            content: line.content,
            lineno: line.new_lineno,
            tokenLineIndex: tokenIdx,
          },
        });
        li++;
        continue;
      }

      // Gather consecutive deletions, then additions.
      const deletions: DiffLine[] = [];
      const additions: DiffLine[] = [];

      while (li < lines.length && lines[li].kind === "Deletion") {
        deletions.push(lines[li]);
        li++;
      }
      while (li < lines.length && lines[li].kind === "Addition") {
        additions.push(lines[li]);
        li++;
      }

      const maxLen = Math.max(deletions.length, additions.length);
      for (let j = 0; j < maxLen; j++) {
        const del = j < deletions.length ? deletions[j] : null;
        const add = j < additions.length ? additions[j] : null;

        rows.push({
          left: del
            ? {
                kind: "deletion",
                content: del.content,
                lineno: del.old_lineno,
                tokenLineIndex: null, // old content not in current file
              }
            : {
                kind: "empty",
                content: "",
                lineno: null,
                tokenLineIndex: null,
              },
          right: add
            ? {
                kind: "addition",
                content: add.content,
                lineno: add.new_lineno,
                tokenLineIndex:
                  add.new_lineno !== null ? add.new_lineno - 1 : null,
              }
            : {
                kind: "empty",
                content: "",
                lineno: null,
                tokenLineIndex: null,
              },
        });
      }
    }
  }

  return rows;
}

function SplitDiffView({
  diff,
  tokenizedLines,
  bg,
}: {
  diff: FileDiff;
  tokenizedLines: TokenizedLine[];
  bg: string | undefined;
}) {
  const rows = useMemo(
    () => buildSplitRows(diff),
    [diff],
  );

  const maxLineno = rows.reduce(
    (m, r) =>
      Math.max(m, r.left.lineno ?? 0, r.right.lineno ?? 0),
    0,
  );
  const gutterW = `${Math.max(String(maxLineno).length, 2) + 1}ch`;

  const leftRef = useRef<HTMLDivElement | null>(null);
  const rightRef = useRef<HTMLDivElement | null>(null);
  const syncing = useRef(false);

  const handleScroll = useCallback(
    (source: HTMLDivElement | null, target: HTMLDivElement | null) => {
      if (syncing.current || !source || !target) return;
      syncing.current = true;
      target.scrollTop = source.scrollTop;
      target.scrollLeft = source.scrollLeft;
      requestAnimationFrame(() => {
        syncing.current = false;
      });
    },
    [],
  );

  function renderSide(
    side: "left" | "right",
    tokens: TokenizedLine[],
  ) {
    return (
      <table className="border-collapse font-mono">
        <colgroup>
          <col style={{ width: gutterW }} />
          <col />
        </colgroup>
        <tbody>
          {rows.map((row, i) => {
            const cell = side === "left" ? row.left : row.right;
            return (
              <tr key={i} className="leading-relaxed">
                {renderCell(cell, tokens, gutterW)}
              </tr>
            );
          })}
        </tbody>
      </table>
    );
  }

  return (
    <div
      className="flex flex-1 overflow-hidden text-sm leading-relaxed"
      style={{ backgroundColor: bg }}
    >
      <div
        ref={leftRef}
        className="flex-1 min-w-0 overflow-scroll"
        onScroll={() => handleScroll(leftRef.current, rightRef.current)}
      >
        {renderSide("left", tokenizedLines)}
      </div>
      <div className="w-px flex-shrink-0 bg-border" />
      <div
        ref={rightRef}
        className="flex-1 min-w-0 overflow-scroll"
        onScroll={() => handleScroll(rightRef.current, leftRef.current)}
      >
        {renderSide("right", tokenizedLines)}
      </div>
    </div>
  );
}

function renderCell(
  cell: SplitRow["left"] | SplitRow["right"],
  tokens: TokenizedLine[],
  gutterW: string,
) {
  if (cell.kind === "hunk-header") {
    return (
      <>
        <td
          className="select-none px-1.5 text-right align-top text-fg-muted opacity-50 bg-accent/10"
          style={{ width: gutterW }}
        />
        <td className="bg-accent/10 px-3 py-0.5 text-xs text-fg-muted">
          <div className="whitespace-pre">
            {cell.content}
          </div>
        </td>
      </>
    );
  }

  const bgClass =
    cell.kind === "addition"
      ? "bg-success/15"
      : cell.kind === "deletion"
        ? "bg-danger/15"
        : cell.kind === "empty"
          ? "bg-bg-surface/50"
          : "";

  const tokenLine =
    cell.tokenLineIndex !== null &&
    cell.tokenLineIndex < tokens.length
      ? tokens[cell.tokenLineIndex]
      : null;

  return (
    <>
      <td
        className={`select-none px-1.5 text-right align-top text-fg-muted opacity-50 ${bgClass}`}
        style={{ width: gutterW }}
      >
        {cell.lineno ?? ""}
      </td>
      <td className={`whitespace-pre pr-3 ${bgClass}`}>
        {tokenLine ? (
          <TokenLine tokens={tokenLine.tokens} />
        ) : (
          cell.content || "\u00a0"
        )}
      </td>
    </>
  );
}

// ── Plain file view (no diff) ───────────────────────────────────

function PlainFileView({
  lines,
  bg,
}: {
  lines: TokenizedLine[];
  bg: string | undefined;
}) {
  const lineCount = lines.length;
  const gutterWidth = Math.max(String(lineCount).length, 2);

  return (
    <div
      className="flex-1 overflow-auto text-sm leading-relaxed"
      style={{ backgroundColor: bg }}
    >
      <table className="w-full border-collapse font-mono">
        <tbody>
          {lines.map((line, i) => (
            <tr key={i} className="leading-relaxed">
              <td
                className="select-none px-3 text-right align-top text-fg-muted opacity-50"
                style={{ width: `${gutterWidth + 2}ch` }}
              >
                {i + 1}
              </td>
              <td className="whitespace-pre pr-3">
                <TokenLine tokens={line.tokens} />
              </td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}

// ── FileViewer (main export) ────────────────────────────────────

interface FileViewerProps {
  filePath: string;
  content: string;
  diff?: FileDiff | null;
  viewMode?: DiffViewMode;
}

export function FileViewer({
  filePath,
  content,
  diff,
  viewMode = "unified",
}: FileViewerProps) {
  const { lines, bg } = useHighlightedLines(filePath, content);

  const hasDiff = diff && diff.hunks.length > 0;

  return (
    <div className="flex h-full flex-col overflow-hidden">
      <div className="flex-shrink-0 border-b border-border bg-bg-surface px-3 py-1.5 text-xs text-fg-muted">
        {filePath}
      </div>
      {lines === null ? (
        <div className="flex-1 p-3 text-fg-muted">Highlighting…</div>
      ) : hasDiff ? (
        viewMode === "split" ? (
          <SplitDiffView
            diff={diff}
            tokenizedLines={lines}
            bg={bg}
          />
        ) : (
          <UnifiedDiffView
            diff={diff}
            tokenizedLines={lines}
            bg={bg}
          />
        )
      ) : (
        <PlainFileView lines={lines} bg={bg} />
      )}
    </div>
  );
}
