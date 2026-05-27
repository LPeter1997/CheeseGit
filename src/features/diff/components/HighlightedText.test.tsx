import { describe, it, expect } from "vitest";
import { render, screen } from "@testing-library/react";
import { HighlightedText } from "./HighlightedText";
import type { DiffSearchMatch } from "../hooks/useDiffSearch";

describe("HighlightedText", () => {
  it("renders plain text when no matches", () => {
    render(
      <HighlightedText
        content="Hello world"
        matches={[]}
      />,
    );

    expect(screen.getByText("Hello world")).toBeInTheDocument();
  });

  it("highlights all matches", () => {
    const matches: DiffSearchMatch[] = [
      { lineIndex: 0, charOffset: 0, length: 5, kind: "addition" },
      { lineIndex: 0, charOffset: 6, length: 5, kind: "addition" },
    ];

    const { container } = render(
      <HighlightedText
        content="Hello world"
        matches={matches}
        currentMatchLineIndex={-1}
      />,
    );

    // Should have 2 highlighted spans for "Hello" and "world"
    const highlights = container.querySelectorAll("span.bg-accent\\\/15");
    expect(highlights).toHaveLength(2);
    expect(highlights[0]).toHaveTextContent("Hello");
    expect(highlights[1]).toHaveTextContent("world");
  });

  it("highlights current match prominently", () => {
    const matches: DiffSearchMatch[] = [
      { lineIndex: 0, charOffset: 0, length: 5, kind: "addition" },
      { lineIndex: 0, charOffset: 6, length: 5, kind: "addition" },
    ];

    const { container } = render(
      <HighlightedText
        content="Hello world"
        matches={matches}
        currentMatchLineIndex={0}
        currentMatchCharOffset={6}
      />,
    );

    // First match should have faint highlight
    const faintHighlights = container.querySelectorAll("span.bg-accent\\\/15");
    expect(faintHighlights).toHaveLength(1);
    expect(faintHighlights[0]).toHaveTextContent("Hello");

    // Second match should have prominent highlight
    const prominentHighlights = container.querySelectorAll("span.bg-accent\\\/50");
    expect(prominentHighlights).toHaveLength(1);
    expect(prominentHighlights[0]).toHaveTextContent("world");
  });

  it("handles multiple matches on the same line", () => {
    const matches: DiffSearchMatch[] = [
      { lineIndex: 0, charOffset: 0, length: 3, kind: "addition" },
      { lineIndex: 0, charOffset: 4, length: 3, kind: "addition" },
      { lineIndex: 0, charOffset: 8, length: 3, kind: "addition" },
    ];

    const { container } = render(
      <HighlightedText
        content="foo bar baz"
        matches={matches}
        currentMatchLineIndex={0}
        currentMatchCharOffset={4}
      />,
    );

    const highlights = container.querySelectorAll("span");
    expect(highlights).toHaveLength(3);
  });

  it("preserves text before and after matches", () => {
    const matches: DiffSearchMatch[] = [
      { lineIndex: 0, charOffset: 6, length: 5, kind: "addition" },
    ];

    const { container } = render(
      <HighlightedText
        content="Hello world! How are you?"
        matches={matches}
        currentMatchLineIndex={0}
        currentMatchCharOffset={6}
      />,
    );

    const text = container.textContent;
    expect(text).toBe("Hello world! How are you?");
  });

  it("handles matches at the beginning", () => {
    const matches: DiffSearchMatch[] = [
      { lineIndex: 0, charOffset: 0, length: 5, kind: "addition" },
    ];

    const { container } = render(
      <HighlightedText
        content="Hello world"
        matches={matches}
      />,
    );

    const highlight = container.querySelector("span");
    expect(highlight).toHaveTextContent("Hello");
  });

  it("handles matches at the end", () => {
    const matches: DiffSearchMatch[] = [
      { lineIndex: 0, charOffset: 6, length: 5, kind: "addition" },
    ];

    const { container } = render(
      <HighlightedText
        content="Hello world"
        matches={matches}
      />,
    );

    const highlights = container.querySelectorAll("span");
    expect(highlights[highlights.length - 1]).toHaveTextContent("world");
  });

  it("handles consecutive matches", () => {
    const matches: DiffSearchMatch[] = [
      { lineIndex: 0, charOffset: 0, length: 3, kind: "addition" },
      { lineIndex: 0, charOffset: 3, length: 3, kind: "addition" },
    ];

    const { container } = render(
      <HighlightedText
        content="foofoo test"
        matches={matches}
        currentMatchLineIndex={0}
        currentMatchCharOffset={0}
      />,
    );

    // Should still render without crashing
    expect(container.textContent).toContain("foofoo test");
  });

  it("doesn't highlight when line index doesn't match current", () => {
    const matches: DiffSearchMatch[] = [
      { lineIndex: 0, charOffset: 0, length: 5, kind: "addition" },
    ];

    const { container } = render(
      <HighlightedText
        content="Hello world"
        matches={matches}
        currentMatchLineIndex={1} // Different line
        currentMatchCharOffset={0}
      />,
    );

    // Should show faint highlight since it's not the current match
    const faintHighlights = container.querySelectorAll("span.bg-accent\\\/15");
    expect(faintHighlights).toHaveLength(1);
  });
});
