import { useEffect, useState } from "react";
import { createHighlighter, type Highlighter, type ThemedToken } from "shiki";

/** Singleton highlighter instance (loaded lazily). */
let highlighterPromise: Promise<Highlighter> | null = null;

function getHighlighterInstance(): Promise<Highlighter> {
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
      const hl = await getHighlighterInstance();
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
