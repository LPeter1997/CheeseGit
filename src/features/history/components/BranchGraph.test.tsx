import { describe, it, expect, vi } from "vitest";
import { render, fireEvent } from "@testing-library/react";
import { GraphOverlay } from "./BranchGraph";
import type { GraphLayout } from "../graph/layout";

function makeLayout(nodeCount: number): GraphLayout {
  const nodes = Array.from({ length: nodeCount }, (_, i) => ({
    row: i,
    column: 0,
    branch: "main",
    color: "#ff0000",
    isLocalOnly: false,
    hash: `hash${i}`,
  }));

  return {
    nodes,
    edges: [],
    lanes: [{ branch: "main", column: 0, color: "#ff0000" }],
    columnCount: 1,
    rowHeight: 50,
    commits: nodes.map((n) => ({
      hash: n.hash,
      short_hash: n.hash.slice(0, 7),
      summary: `commit ${n.row}`,
      author: "Test",
      timestamp: "2026-01-01T00:00:00Z",
      parents: [],
      refs: [],
      insertions: null,
      deletions: null,
    })),
  };
}

describe("GraphOverlay", () => {
  it("renders commit dots as circles", () => {
    const layout = makeLayout(3);
    const { container } = render(
      <svg>
        <GraphOverlay
          layout={layout}
          height={150}
          scrollTop={0}
          viewportHeight={150}
          hoveredBranch={null}
          onHoverBranch={() => {}}
          headHash="hash0"
        />
      </svg>,
    );

    // Should have circles for nodes + hit areas
    const circles = container.querySelectorAll("circle");
    expect(circles.length).toBeGreaterThan(0);
  });

  it("shows head marker ring on HEAD commit", () => {
    const layout = makeLayout(3);
    const { container } = render(
      <svg>
        <GraphOverlay
          layout={layout}
          height={150}
          scrollTop={0}
          viewportHeight={150}
          hoveredBranch={null}
          onHoverBranch={() => {}}
          headHash="hash0"
        />
      </svg>,
    );

    // The head marker ring uses stroke="var(--color-head-marker)" with strokeWidth={2}
    const headRings = container.querySelectorAll('circle[stroke="var(--color-head-marker)"][stroke-width="2"]');
    expect(headRings.length).toBe(1);
  });

  it("calls onClickCommit when a commit dot is clicked", () => {
    const layout = makeLayout(3);
    const onClickCommit = vi.fn();
    const { container } = render(
      <svg>
        <GraphOverlay
          layout={layout}
          height={150}
          scrollTop={0}
          viewportHeight={150}
          hoveredBranch={null}
          onHoverBranch={() => {}}
          headHash="hash0"
          onClickCommit={onClickCommit}
        />
      </svg>,
    );

    // Hit areas are transparent circles with pointer-events="all"
    const hitAreas = container.querySelectorAll('circle[pointer-events="all"]');
    expect(hitAreas.length).toBe(3);

    // Click the second commit dot
    fireEvent.click(hitAreas[1]);
    expect(onClickCommit).toHaveBeenCalledWith("hash1");
  });

  it("shows hover ring when hovering a non-HEAD commit", () => {
    const layout = makeLayout(3);
    const { container } = render(
      <svg>
        <GraphOverlay
          layout={layout}
          height={150}
          scrollTop={0}
          viewportHeight={150}
          hoveredBranch={null}
          onHoverBranch={() => {}}
          headHash="hash0"
          onClickCommit={() => {}}
        />
      </svg>,
    );

    // Before hover: no hover ring (opacity=0.4)
    let hoverRings = container.querySelectorAll('circle[opacity="0.4"]');
    expect(hoverRings.length).toBe(0);

    // Hover over the second commit hit area
    const hitAreas = container.querySelectorAll('circle[pointer-events="all"]');
    fireEvent.mouseEnter(hitAreas[1]);

    // After hover: should have a hover ring on non-HEAD commit
    hoverRings = container.querySelectorAll('circle[opacity="0.4"]');
    expect(hoverRings.length).toBe(1);
  });

  it("does not show hover ring on HEAD commit", () => {
    const layout = makeLayout(3);
    const { container } = render(
      <svg>
        <GraphOverlay
          layout={layout}
          height={150}
          scrollTop={0}
          viewportHeight={150}
          hoveredBranch={null}
          onHoverBranch={() => {}}
          headHash="hash0"
          onClickCommit={() => {}}
        />
      </svg>,
    );

    // Hover over the HEAD commit
    const hitAreas = container.querySelectorAll('circle[pointer-events="all"]');
    fireEvent.mouseEnter(hitAreas[0]);

    // Should NOT show hover ring on HEAD (isHovered checks !isHead)
    const hoverRings = container.querySelectorAll('circle[opacity="0.4"]');
    expect(hoverRings.length).toBe(0);
  });

  it("clears hover ring on mouse leave", () => {
    const layout = makeLayout(3);
    const { container } = render(
      <svg>
        <GraphOverlay
          layout={layout}
          height={150}
          scrollTop={0}
          viewportHeight={150}
          hoveredBranch={null}
          onHoverBranch={() => {}}
          headHash="hash0"
          onClickCommit={() => {}}
        />
      </svg>,
    );

    const hitAreas = container.querySelectorAll('circle[pointer-events="all"]');
    fireEvent.mouseEnter(hitAreas[1]);

    let hoverRings = container.querySelectorAll('circle[opacity="0.4"]');
    expect(hoverRings.length).toBe(1);

    fireEvent.mouseLeave(hitAreas[1]);

    hoverRings = container.querySelectorAll('circle[opacity="0.4"]');
    expect(hoverRings.length).toBe(0);
  });
});
