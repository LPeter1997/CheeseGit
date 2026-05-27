import { useMemo } from "react";
import type { DiffSearchMatch } from "../hooks/useDiffSearch";

interface HighlightedTextProps {
  content: string;
  matches?: DiffSearchMatch[];
  currentMatchLineIndex?: number;
  currentMatchCharOffset?: number;
}

/**
 * Renders text with highlighting for search matches.
 * Highlights all matches faintly, and the current match prominently.
 */
export function HighlightedText({
  content,
  matches = [],
  currentMatchLineIndex,
  currentMatchCharOffset,
}: HighlightedTextProps) {
  // Sort matches by position without mutating incoming props.
  const sortedMatches = useMemo(
    () => [...matches].sort((a, b) => a.charOffset - b.charOffset),
    [matches],
  );

  // This component should only receive matches that belong to this line
  // If there are no matches on this line, just render the content as-is
  if (matches.length === 0) {
    return <>{content}</>;
  }

  const parts: React.ReactNode[] = [];
  let lastEnd = 0;

  for (const match of sortedMatches) {
    // Add text before match
    if (match.charOffset > lastEnd) {
      parts.push(content.substring(lastEnd, match.charOffset));
    }

    // Add highlighted match
    const isCurrentMatch =
      currentMatchLineIndex === match.lineIndex &&
      currentMatchCharOffset === match.charOffset;

    const highlightClass = isCurrentMatch
      ? "bg-accent/50 text-accent-fg font-semibold"
      : "bg-accent/15";

    parts.push(
      <span key={`${match.lineIndex}-${match.charOffset}`} className={highlightClass}>
        {content.substring(match.charOffset, match.charOffset + match.length)}
      </span>,
    );

    lastEnd = match.charOffset + match.length;
  }

  // Add remaining text
  if (lastEnd < content.length) {
    parts.push(content.substring(lastEnd));
  }

  return <>{parts}</>;
}
