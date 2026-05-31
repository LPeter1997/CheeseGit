import { useEffect, useRef, useState } from "react";
import { commands } from "../../../ipc/bindings";
import type { SyntaxToken as BackendToken } from "../../../ipc/bindings";

/** Maximum content size (chars) before we skip highlighting. */
const MAX_HIGHLIGHT_CHARS = 500_000;

/** Maximum line count before we skip highlighting. */
const MAX_HIGHLIGHT_LINES = 10_000;

/** A single token with content text and semantic category. */
export interface HighlightToken {
  content: string;
  category: string;
}

/** One line of syntax tokens. */
export interface TokenizedLine {
  tokens: HighlightToken[];
}

/** CSS variable lookup for each token category. */
const SYNTAX_COLOR: Record<string, string> = {
  keyword: "var(--cg-syntax-keyword)",
  string: "var(--cg-syntax-string)",
  comment: "var(--cg-syntax-comment)",
  number: "var(--cg-syntax-number)",
  operator: "var(--cg-syntax-operator)",
  function: "var(--cg-syntax-function)",
  type: "var(--cg-syntax-type)",
  variable: "var(--cg-syntax-variable)",
  punctuation: "var(--cg-syntax-punctuation)",
  tag: "var(--cg-syntax-tag)",
  attribute: "var(--cg-syntax-attribute)",
  meta: "var(--cg-syntax-meta)",
  plain: "inherit",
};

/** Resolve a token category to its CSS color value. */
export function syntaxColor(category: string): string {
  return SYNTAX_COLOR[category] ?? "inherit";
}

/**
 * Convert backend token lengths into frontend tokens with content substrings.
 */
function backendToFrontend(
  lineTexts: string[],
  backendLines: BackendToken[][],
): TokenizedLine[] {
  return backendLines.map((lineTokens, i) => {
    const text = lineTexts[i] ?? "";
    let pos = 0;
    const tokens: HighlightToken[] = lineTokens.map((t) => {
      const end = pos + t.length;
      const content = text.substring(pos, end);
      pos = end;
      return { content, category: t.category };
    });
    // If there's remaining text (safety), add as plain.
    if (pos < text.length) {
      tokens.push({ content: text.substring(pos), category: "plain" });
    }
    return { tokens };
  });
}

/**
 * Hook that tokenizes content via the Rust backend and returns per-line tokens.
 *
 * Theme changes are instant: the backend returns semantic categories and
 * the frontend maps them to CSS variables that update automatically.
 */
export function useHighlightedLines(
  filePath: string,
  content: string,
  neededLines?: number[] | null,
): { lines: TokenizedLine[] | null } {
  const [lines, setLines] = useState<TokenizedLine[] | null>(null);
  const requestId = useRef(0);

  // Stable serialized key so the effect doesn't re-fire on every render
  // when the caller passes a new array reference with the same contents.
  const neededKey = neededLines ? neededLines.join(",") : "";

  useEffect(() => {
    // Fast path for empty content.
    if (!content) {
      setLines([{ tokens: [] }]);
      return;
    }

    // Skip highlighting for very large files.
    if (content.length > MAX_HIGHLIGHT_CHARS) {
      const plainLines = content.split("\n").map((line) => ({
        tokens: line.length > 0
          ? [{ content: line, category: "plain" as const }]
          : [],
      }));
      setLines(plainLines);
      return;
    }

    const lineTexts = content.split("\n");

    // Skip highlighting for files with too many lines.
    if (lineTexts.length > MAX_HIGHLIGHT_LINES) {
      setLines(lineTexts.map((line) => ({
        tokens: line.length > 0
          ? [{ content: line, category: "plain" as const }]
          : [],
      })));
      return;
    }

    const id = ++requestId.current;

    // Build plain tokens for every line so content is visible immediately
    // while we wait for syntax highlighting in the background.
    const plain: TokenizedLine[] = lineTexts.map((line) => ({
      tokens: line.length > 0
        ? [{ content: line, category: "plain" as const }]
        : [],
    }));
    setLines(plain);

    // When the caller tells us which lines the diff needs, send only those
    // lines for tokenization.  This turns a 150 KB payload into a few KB.
    const sparseMode = neededLines && neededLines.length > 0;
    const contentToSend = sparseMode
      ? neededLines.map((i) => lineTexts[i] ?? "").join("\n")
      : content;

    commands.tokenizeContent(filePath, contentToSend, null).then((result) => {
      // Discard stale responses.
      if (id !== requestId.current) return;

      if (result.status === "ok") {
        if (sparseMode) {
          // Map the compact result back into the full-size array.
          const subsetTexts = neededLines.map((i) => lineTexts[i] ?? "");
          const highlighted = backendToFrontend(subsetTexts, result.data);
          // Clone the plain array and patch in highlighted lines.
          const merged = plain.slice();
          for (let j = 0; j < neededLines.length; j++) {
            merged[neededLines[j]] = highlighted[j];
          }
          setLines(merged);
        } else {
          setLines(backendToFrontend(lineTexts, result.data));
        }
      }
      // On error the plain tokens set above are already showing — nothing to do.
    });

    return () => {
      // Bump the request ID so in-flight responses are discarded.
      requestId.current++;
    };
  }, [filePath, content, neededKey]);

  return { lines };
}
