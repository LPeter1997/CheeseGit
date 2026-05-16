import { describe, it, expect } from "vitest";
import type { GraphCommit } from "../../../ipc/bindings";
import { computeGraphLayout, computeRequiredBranches } from "./layout";

/** Helper: create a minimal GraphCommit. */
function commit(
  hash: string,
  parents: string[],
  refs: string[] = [],
  summary = "",
): GraphCommit {
  return {
    hash,
    short_hash: hash.slice(0, 7),
    summary: summary || `commit ${hash}`,
    author: "Test",
    timestamp: "2026-01-01T00:00:00Z",
    parents,
    refs,
  };
}

// A simple linear history: c1 ← c2 ← c3 (c1 is newest)
function linearHistory(): GraphCommit[] {
  return [
    commit("c1", ["c2"], ["main"]),
    commit("c2", ["c3"]),
    commit("c3", []),
  ];
}

// A branched history:
//   c1 (main) ← c2 ← c4 (root)
//   c3 (feature) ←───┘
function branchedHistory(): GraphCommit[] {
  return [
    commit("c1", ["c2"], ["main"]),
    commit("c2", ["c4"]),
    commit("c3", ["c4"], ["feature"]),
    commit("c4", []),
  ];
}

describe("computeGraphLayout", () => {
  it("returns empty layout for empty input", () => {
    const layout = computeGraphLayout([], [], new Set(), "main", 50);
    expect(layout.nodes).toEqual([]);
    expect(layout.edges).toEqual([]);
    expect(layout.lanes).toEqual([]);
    expect(layout.columnCount).toBe(0);
  });

  it("returns empty layout when no branches provided", () => {
    const layout = computeGraphLayout(linearHistory(), [], new Set(), "main", 50);
    expect(layout.nodes).toEqual([]);
  });

  it("creates nodes for a linear history", () => {
    const layout = computeGraphLayout(linearHistory(), ["main"], new Set(), "main", 50);

    expect(layout.nodes).toHaveLength(3);
    // All nodes should be in column 0 (main branch).
    for (const node of layout.nodes) {
      expect(node.column).toBe(0);
      expect(node.branch).toBe("main");
    }
  });

  it("creates edges connecting parent-child nodes", () => {
    const layout = computeGraphLayout(linearHistory(), ["main"], new Set(), "main", 50);

    expect(layout.edges).toHaveLength(2); // c1→c2, c2→c3
    expect(layout.edges[0].fromRow).toBe(0);
    expect(layout.edges[0].toRow).toBe(1);
    expect(layout.edges[1].fromRow).toBe(1);
    expect(layout.edges[1].toRow).toBe(2);
  });

  it("assigns separate columns for side branches", () => {
    const layout = computeGraphLayout(
      branchedHistory(),
      ["main", "feature"],
      new Set(),
      "main",
      50,
    );

    // main should be column 0, feature should be column 1.
    const mainNodes = layout.nodes.filter((n) => n.branch === "main");
    const featureNodes = layout.nodes.filter((n) => n.branch === "feature");
    expect(mainNodes.length).toBeGreaterThan(0);
    expect(featureNodes.length).toBeGreaterThan(0);
    for (const n of mainNodes) expect(n.column).toBe(0);
    for (const n of featureNodes) expect(n.column).toBe(1);
  });

  it("computes correct Y coordinates based on rowHeight", () => {
    const rowHeight = 40;
    const layout = computeGraphLayout(linearHistory(), ["main"], new Set(), "main", rowHeight);

    // Node at row 0 should have y = 0 * 40 + 20 = 20 (via edge fromY).
    expect(layout.edges[0].fromY).toBe(20);
    // Node at row 1 should have y = 1 * 40 + 20 = 60.
    expect(layout.edges[0].toY).toBe(60);
  });

  it("marks local-only commits", () => {
    const localOnly = new Set(["c1"]);
    const layout = computeGraphLayout(linearHistory(), ["main"], localOnly, "main", 50);

    const c1Node = layout.nodes.find((n) => n.hash === "c1");
    const c2Node = layout.nodes.find((n) => n.hash === "c2");
    expect(c1Node?.isLocalOnly).toBe(true);
    expect(c2Node?.isLocalOnly).toBe(false);
  });

  it("filters commits to only visible branches", () => {
    const layout = computeGraphLayout(
      branchedHistory(),
      ["main"], // only main visible, feature hidden
      new Set(),
      "main",
      50,
    );

    // Should only include commits on main's first-parent chain (c1, c2, c4).
    const hashes = layout.commits.map((c) => c.hash);
    expect(hashes).toContain("c1");
    expect(hashes).toContain("c2");
    expect(hashes).not.toContain("c3"); // feature-only commit
  });

  it("stores rowHeight in layout", () => {
    const layout = computeGraphLayout(linearHistory(), ["main"], new Set(), "main", 42);
    expect(layout.rowHeight).toBe(42);
  });

  it("columnCount matches the number of used columns", () => {
    const layout = computeGraphLayout(
      branchedHistory(),
      ["main", "feature"],
      new Set(),
      "main",
      50,
    );
    expect(layout.columnCount).toBe(2);
  });
});

describe("computeRequiredBranches", () => {
  it("always includes the current branch", () => {
    const result = computeRequiredBranches(linearHistory(), ["main"], "main");
    expect(result).toContain("main");
  });

  it("includes waterfall ancestor branches", () => {
    // feature's tip sits on main's first-parent chain, so it's required.
    const commits = [
      commit("c1", ["c2"], ["main"]),
      commit("c2", ["c3"], ["develop"]),
      commit("c3", []),
    ];
    const result = computeRequiredBranches(commits, ["main", "develop"], "main");
    expect(result).toContain("main");
    expect(result).toContain("develop");
  });

  it("excludes branches not on the waterfall chain", () => {
    const result = computeRequiredBranches(
      branchedHistory(),
      ["main", "feature"],
      "main",
    );
    expect(result).toContain("main");
    // feature's tip (c3) is NOT on main's first-parent chain (c1→c2→c4).
    expect(result).not.toContain("feature");
  });

  it("excludes remote branches from required set", () => {
    const commits = [
      commit("c1", ["c2"], ["main", "origin/main"]),
      commit("c2", []),
    ];
    const result = computeRequiredBranches(
      commits,
      ["main", "origin/main"],
      "main",
      "origin",
    );
    expect(result).toContain("main");
    expect(result).not.toContain("origin/main");
  });
});
