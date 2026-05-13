import { useEffect, useState } from "react";
import { createHighlighter, type Highlighter, type ThemedToken } from "shiki";

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

interface TokenizedLine {
  tokens: ThemedToken[];
}

interface FileViewerProps {
  filePath: string;
  content: string;
}

export function FileViewer({ filePath, content }: FileViewerProps) {
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
            await hl.loadLanguage(lang as Parameters<typeof hl.loadLanguage>[0]);
          } catch {
            // Fall back to plain text if grammar isn't bundled.
          }
        }
      }

      if (cancelled) return;

      const theme = detectTheme();
      const effectiveLang =
        lang !== "text" && hl.getLoadedLanguages().includes(lang) ? lang : "text";

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

  const lineCount = lines?.length ?? 0;
  const gutterWidth = Math.max(String(lineCount).length, 2);

  return (
    <div className="flex h-full flex-col overflow-hidden">
      <div className="flex-shrink-0 border-b border-border bg-bg-surface px-3 py-1.5 text-xs text-fg-muted">
        {filePath}
      </div>
      <div
        className="flex-1 overflow-auto text-sm leading-relaxed"
        style={{ backgroundColor: bg }}
      >
        {lines === null ? (
          <div className="p-3 text-fg-muted">Highlighting…</div>
        ) : (
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
                    {line.tokens.length === 0 ? (
                      "\n"
                    ) : (
                      line.tokens.map((token, j) => (
                        <span key={j} style={{ color: token.color }}>
                          {token.content}
                        </span>
                      ))
                    )}
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        )}
      </div>
    </div>
  );
}
