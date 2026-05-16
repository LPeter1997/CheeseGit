import type { GraphLayout, GraphNode } from "../graph/layout";

/** Geometry constants for the graph rendering. */
const NODE_RADIUS = 4;
const LANE_WIDTH = 16;
const LANE_PAD_LEFT = 12;

const TRANSITION_STYLE = { transition: "opacity 150ms ease" };

/** Convert column index to x pixel coordinate. */
function cx(column: number) {
  return LANE_PAD_LEFT + column * LANE_WIDTH + LANE_WIDTH / 2;
}

/** Total pixel width for the graph given a column count. */
export function graphWidth(columnCount: number) {
  return LANE_PAD_LEFT + columnCount * LANE_WIDTH;
}

// ── Full graph overlay (edges + dots in one SVG, grouped per branch) ─

interface GraphOverlayProps {
  layout: GraphLayout;
  /** Total pixel height of the scrollable list. */
  height: number;
  hoveredBranch: string | null;
  onHoverBranch: (branch: string | null) => void;
}

export function GraphOverlay({ layout, height, hoveredBranch, onHoverBranch }: GraphOverlayProps) {
  const width = graphWidth(layout.columnCount);

  // ── Build one continuous path string per branch ───────────────────
  const branchEdges = new Map<string, typeof layout.edges>();
  for (const edge of layout.edges) {
    const branch = edgeBranch(edge, layout);
    let list = branchEdges.get(branch);
    if (!list) {
      list = [];
      branchEdges.set(branch, list);
    }
    list.push(edge);
  }

  const branchPaths = new Map<string, { d: string; color: string }>();
  for (const [branch, edges] of branchEdges) {
    edges.sort((a, b) => a.fromRow - b.fromRow);

    let d = "";
    let lastX: number | null = null;
    let lastY: number | null = null;

    for (const edge of edges) {
      const x1 = cx(edge.fromColumn);
      const x2 = cx(edge.toColumn);
      const y1 = edge.fromY;
      const y2 = edge.toY;
      const cont = lastX === x1 && lastY === y1;

      if (edge.fromColumn === edge.toColumn) {
        d += cont ? ` L ${x2} ${y2}` : ` M ${x1} ${y1} L ${x2} ${y2}`;
      } else {
        const dy = y2 - y1;
        const curveH = Math.min(layout.rowHeight, dy);

        if (edge.fromColumn > edge.toColumn) {
          const cy = y2 - curveH;
          if (!cont) d += ` M ${x1} ${y1}`;
          if (cy > y1) d += ` L ${x1} ${cy}`;
          d += ` C ${x1} ${cy + curveH * 2 / 3}, ${x2} ${y2 - curveH * 2 / 3}, ${x2} ${y2}`;
        } else {
          if (!cont) d += ` M ${x1} ${y1}`;
          const cy = y1 + curveH;
          d += ` C ${x1} ${y1 + curveH * 2 / 3}, ${x2} ${cy - curveH * 2 / 3}, ${x2} ${cy}`;
          if (cy < y2) d += ` L ${x2} ${y2}`;
        }
      }

      lastX = x2;
      lastY = y2;
    }

    branchPaths.set(branch, { d: d.trimStart(), color: edges[0].color });
  }

  // ── Group nodes by branch ─────────────────────────────────────────
  const branchNodes = new Map<string, GraphNode[]>();
  for (const node of layout.nodes) {
    let list = branchNodes.get(node.branch);
    if (!list) {
      list = [];
      branchNodes.set(node.branch, list);
    }
    list.push(node);
  }

  // Collect all branches that have edges or nodes.
  const allBranches = new Set([...branchPaths.keys(), ...branchNodes.keys()]);

  return (
    <svg
      width={width}
      height={height}
      className="absolute left-0 top-0 pointer-events-none z-[1]"
      style={{ minWidth: width }}
    >
      {/* Per-branch groups: <g opacity> composites children at full opacity
          first, then applies the group opacity — no alpha accumulation
          between edges and dots of the same branch. */}
      {[...allBranches].map((branch) => {
        const isFaded = hoveredBranch !== null && hoveredBranch !== branch;
        const pathInfo = branchPaths.get(branch);
        const nodes = branchNodes.get(branch) ?? [];
        return (
          <g key={branch} opacity={isFaded ? 0.1 : 1} style={TRANSITION_STYLE}>
            {pathInfo && (
              <path
                d={pathInfo.d}
                fill="none"
                stroke={pathInfo.color}
                strokeWidth={2}
              />
            )}
            {nodes.map((node) => {
              const x = cx(node.column);
              const y = node.row * layout.rowHeight + layout.rowHeight / 2;
              return node.isLocalOnly ? (
                <circle
                  key={node.hash}
                  cx={x}
                  cy={y}
                  r={NODE_RADIUS}
                  fill="var(--color-bg)"
                  stroke={node.color}
                  strokeWidth={1.5}
                  strokeDasharray="2 2"
                />
              ) : (
                <circle
                  key={node.hash}
                  cx={x}
                  cy={y}
                  r={NODE_RADIUS}
                  fill={node.color}
                  stroke={node.color}
                  strokeWidth={1.5}
                />
              );
            })}
          </g>
        );
      })}

      {/* Invisible hit areas for hover detection (above everything). */}
      {layout.nodes.map((node) => {
        const x = cx(node.column);
        const y = node.row * layout.rowHeight + layout.rowHeight / 2;
        return (
          <circle
            key={`hit-${node.hash}`}
            cx={x}
            cy={y}
            r={NODE_RADIUS + 4}
            fill="transparent"
            pointerEvents="all"
            style={{ cursor: "pointer" }}
            onMouseEnter={() => onHoverBranch(node.branch)}
            onMouseLeave={() => onHoverBranch(null)}
          >
            <title>{node.branch}</title>
          </circle>
        );
      })}
    </svg>
  );
}

/** Determine which branch an edge visually belongs to (for grouping/fading). */
function edgeBranch(
  edge: { fromRow: number; toRow: number; color: string },
  layout: GraphLayout,
): string {
  const fromNode = layout.nodes[edge.fromRow];
  const toNode = layout.nodes[edge.toRow];
  if (fromNode?.branch === toNode?.branch) return fromNode.branch;
  const lane = layout.lanes.find((l) => l.color === edge.color);
  return lane?.branch ?? fromNode?.branch ?? "unknown";
}
