import { useEffect, useState, useCallback, useRef } from "react";
import type { FileDiff } from "../../../ipc/bindings";

export interface DiffSearchMatch {
  /** Line index in the flattened content (0-based) */
  lineIndex: number;
  /** Character offset within the line where the match starts */
  charOffset: number;
  /** Length of the match */
  length: number;
  /** Whether this is an added, deleted, or context line */
  kind: "addition" | "deletion" | "context";
}

/**
 * Extract searchable content from a FileDiff, excluding hunk headers.
 * Returns lines with their kind (added/deleted/context).
 */
export function extractDiffContent(diff: FileDiff | null): Array<{ content: string; kind: "addition" | "deletion" | "context" }> {
  if (!diff) return [];

  const lines: Array<{ content: string; kind: "addition" | "deletion" | "context" }> = [];

  for (const hunk of diff.hunks) {
    for (const line of hunk.lines) {
      const kind =
        line.kind === "Addition"
          ? "addition"
          : line.kind === "Deletion"
            ? "deletion"
            : "context";
      lines.push({ content: line.content, kind });
    }
  }

  return lines;
}

/**
 * Find all matches of a search query in the diff content.
 * Case-insensitive, returns all matches sorted by position.
 * Non-blocking: uses setTimeout to yield to the browser.
 */
export async function findDiffMatches(
  diff: FileDiff | null,
  query: string,
): Promise<DiffSearchMatch[]> {
  if (!query || !diff) return [];

  const lines = extractDiffContent(diff);
  const matches: DiffSearchMatch[] = [];
  const lowerQuery = query.toLowerCase();

  // Process in chunks to avoid blocking the UI
  const chunkSize = 50;
  for (let i = 0; i < lines.length; i += chunkSize) {
    // Yield to browser
    await new Promise((resolve) => setTimeout(resolve, 0));

    const end = Math.min(i + chunkSize, lines.length);
    for (let lineIdx = i; lineIdx < end; lineIdx++) {
      const line = lines[lineIdx];
      const lowerContent = line.content.toLowerCase();
      let searchPos = 0;

      while (true) {
        const pos = lowerContent.indexOf(lowerQuery, searchPos);
        if (pos === -1) break;

        matches.push({
          lineIndex: lineIdx,
          charOffset: pos,
          length: query.length,
          kind: line.kind,
        });

        searchPos = pos + 1;
      }
    }
  }

  return matches;
}

/**
 * Hook to manage diff search state and navigation.
 * Handles finding matches, managing current index, and updating when diff changes.
 */
export function useDiffSearch(diff: FileDiff | null) {
  const [query, setQuery] = useState("");
  const [matches, setMatches] = useState<DiffSearchMatch[]>([]);
  const [currentIndex, setCurrentIndex] = useState(0);
  const [isSearching, setIsSearching] = useState(false);
  const searchAbortRef = useRef<AbortController | null>(null);

  // Find matches whenever diff or query changes
  useEffect(() => {
    const controller = new AbortController();
    searchAbortRef.current = controller;

    if (!query) {
      setMatches([]);
      setCurrentIndex(0);
      return;
    }

    setIsSearching(true);
    findDiffMatches(diff, query)
      .then((found) => {
        if (!controller.signal.aborted) {
          setMatches(found);
          setCurrentIndex(0);
          setIsSearching(false);
        }
      })
      .catch(() => {
        if (!controller.signal.aborted) {
          setIsSearching(false);
        }
      });

    return () => {
      controller.abort();
    };
  }, [diff, query]);

  const goToNext = useCallback(() => {
    if (matches.length === 0) return;
    setCurrentIndex((prev) => (prev + 1) % matches.length);
  }, [matches.length]);

  const goToPrevious = useCallback(() => {
    if (matches.length === 0) return;
    setCurrentIndex((prev) => (prev - 1 + matches.length) % matches.length);
  }, [matches.length]);

  const clearSearch = useCallback(() => {
    setQuery("");
    setMatches([]);
    setCurrentIndex(0);
  }, []);

  const currentMatch = matches[currentIndex] ?? null;

  return {
    query,
    setQuery,
    matches,
    currentIndex,
    currentMatch,
    goToNext,
    goToPrevious,
    clearSearch,
    isSearching,
  };
}
