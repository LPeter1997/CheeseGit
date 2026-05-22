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
    insertions: null,
    deletions: null,
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

describe("computeGraphLayout — large datasets", () => {
  it("handles 1000+ commits without errors", () => {
    const commits: GraphCommit[] = [];
    for (let i = 0; i < 1500; i++) {
      const hash = `h${String(i).padStart(4, "0")}`;
      const parent = i < 1499 ? `h${String(i + 1).padStart(4, "0")}` : undefined;
      commits.push(
        commit(hash, parent ? [parent] : [], i === 0 ? ["main"] : []),
      );
    }

    const layout = computeGraphLayout(commits, ["main"], new Set(), "main", 50);

    expect(layout.nodes).toHaveLength(1500);
    expect(layout.edges).toHaveLength(1499);
    expect(layout.columnCount).toBe(1);
    // Y coordinates should span the full range.
    const lastNode = layout.nodes[layout.nodes.length - 1];
    expect(lastNode.row).toBe(1499);
  });

  it("handles 2000 commits with multiple branches", () => {
    const commits: GraphCommit[] = [];
    // Main chain: 1000 commits
    for (let i = 0; i < 1000; i++) {
      const hash = `m${String(i).padStart(4, "0")}`;
      const parent = i < 999 ? `m${String(i + 1).padStart(4, "0")}` : undefined;
      commits.push(
        commit(hash, parent ? [parent] : [], i === 0 ? ["main"] : []),
      );
    }
    // Feature branch: forks from m0500, 200 commits
    for (let i = 0; i < 200; i++) {
      const hash = `f${String(i).padStart(4, "0")}`;
      const parent = i < 199
        ? `f${String(i + 1).padStart(4, "0")}`
        : "m0500"; // connects back to main
      commits.push(
        commit(hash, [parent], i === 0 ? ["feature"] : []),
      );
    }

    const layout = computeGraphLayout(
      commits,
      ["main", "feature"],
      new Set(),
      "main",
      50,
    );

    // All 1200 commits should be included (1000 main + 200 feature).
    expect(layout.nodes).toHaveLength(1200);
    expect(layout.columnCount).toBe(2); // main in col 0, feature in col 1
    // Feature nodes should be in column 1.
    const featureNodes = layout.nodes.filter((n) => n.branch === "feature");
    expect(featureNodes).toHaveLength(200);
    expect(featureNodes[0].column).toBe(1);
  });

  it("edges span correctly across large row gaps", () => {
    // Sparse graph: commit at row 0 has parent at row 999
    const commits: GraphCommit[] = [];
    for (let i = 0; i < 1000; i++) {
      const hash = `c${String(i).padStart(4, "0")}`;
      const parent = i < 999 ? `c${String(i + 1).padStart(4, "0")}` : undefined;
      commits.push(
        commit(hash, parent ? [parent] : [], i === 0 ? ["main"] : []),
      );
    }

    const layout = computeGraphLayout(commits, ["main"], new Set(), "main", 50);

    // Find the edge spanning from row 0 to row 1
    const firstEdge = layout.edges.find((e) => e.fromRow === 0);
    expect(firstEdge).toBeDefined();
    expect(firstEdge!.toRow).toBe(1);
    expect(firstEdge!.fromY).toBe(25); // 0 * 50 + 25
    expect(firstEdge!.toY).toBe(75);  // 1 * 50 + 25

    // Check last edge
    const lastEdge = layout.edges.find((e) => e.fromRow === 998);
    expect(lastEdge).toBeDefined();
    expect(lastEdge!.toRow).toBe(999);
  });
});

describe("computeGraphLayout — branch ownership correctness", () => {
  it("dead branch does not steal commits from remote default branch", () => {
    // Scenario: user is on feature/SBOF-9574 (branched from master at E).
    // master has moved forward (tip at F). di-simplification is a dead branch
    // that also forked from master (at C). The waterfall goes through the
    // feature's first-parent chain which goes E → D → C → B → A.
    // origin/master tip is at F (one commit ahead of where feature branched).
    //
    // Without the fix, "di-simplification" (alphabetically before "origin/master")
    // would claim commits on origin/master's unique lineage (F) since the
    // waterfall skipped it. But more importantly, if di-simplification's
    // first-parent chain goes through commits NOT on the waterfall (e.g. via
    // a merge's second parent), it can steal shared commits.
    //
    // Graph:
    //   feature tip: feat1 → E → D → M → B → A
    //   M is a merge: first parent = B, second parent = C
    //   di-simplification tip: dead1 → dead2 → C → (C's parent is X)
    //   origin/master tip: F → E → D → M → B → A
    //
    // The commit C (second parent of merge M) should NOT be attributed to
    // di-simplification — it's part of the master lineage.
    const commits: GraphCommit[] = [
      commit("feat1", ["E"], ["feature/SBOF-9574"]),
      commit("F", ["E"], ["origin/master"]),
      commit("E", ["D"]),
      commit("D", ["M"]),
      commit("M", ["B", "C"]), // merge: first parent B, second parent C
      commit("dead1", ["dead2"], ["di-simplification"]),
      commit("dead2", ["C"]),
      commit("C", ["X"]),
      commit("B", ["A"]),
      commit("X", ["A"]),
      commit("A", []),
    ];

    const layout = computeGraphLayout(
      commits,
      ["feature/SBOF-9574", "di-simplification", "origin/master"],
      new Set(),
      "feature/SBOF-9574",
      50,
      "origin",
    );

    // Find which branch commit C is attributed to.
    const nodeC = layout.nodes.find((n) => n.hash === "C");
    expect(nodeC).toBeDefined();
    // C should be on origin/master (the main lineage), NOT di-simplification.
    expect(nodeC!.branch).not.toBe("di-simplification");

    // X should also not be on di-simplification.
    const nodeX = layout.nodes.find((n) => n.hash === "X");
    if (nodeX) {
      expect(nodeX.branch).not.toBe("di-simplification");
    }

    // di-simplification should only own its unique commits.
    const diNodes = layout.nodes.filter((n) => n.branch === "di-simplification");
    const diHashes = diNodes.map((n) => n.hash).sort();
    expect(diHashes).toContain("dead1");
    expect(diHashes).toContain("dead2");
    expect(diHashes).not.toContain("C");
    expect(diHashes).not.toContain("X");
    expect(diHashes).not.toContain("A");
  });

  it("remote default branch claims its unique first-parent chain before side branches", () => {
    // Simpler scenario: current branch forked from master, master moved ahead.
    // A dead branch also forked from master. Without priority ordering,
    // the dead branch (alphabetically first) steals master's unique commits.
    //
    // Graph (topo order):
    //   feat1 (feature/z-branch tip) → base
    //   m2 (origin/master tip) → m1 → base
    //   dead1 (alpha-branch tip) → m1 → base
    //
    // "alpha-branch" < "origin/master" alphabetically, so without fix it
    // claims m1 before origin/master gets a chance.
    const commits: GraphCommit[] = [
      commit("feat1", ["base"], ["feature/z-branch"]),
      commit("m2", ["m1"], ["origin/master"]),
      commit("dead1", ["m1"], ["alpha-branch"]),
      commit("m1", ["base"]),
      commit("base", []),
    ];

    const layout = computeGraphLayout(
      commits,
      ["feature/z-branch", "alpha-branch", "origin/master"],
      new Set(),
      "feature/z-branch",
      50,
      "origin",
    );

    // m1 should be attributed to origin/master, not alpha-branch.
    const nodeM1 = layout.nodes.find((n) => n.hash === "m1");
    expect(nodeM1).toBeDefined();
    expect(nodeM1!.branch).toBe("origin/master");
  });

  it("stale branch tip on waterfall does not steal ownership from master", () => {
    // Real-world scenario: user is on feature/SBOF-9574.
    // The waterfall's first-parent chain is: feat1 → feat2 → master_tip → m3 → stale_tip → m5 → m6
    // master's tip is at master_tip (has remote origin/master).
    // di-simplification's tip is at stale_tip (NO remote tracking — stale branch).
    //
    // Without the fix, localTipOwner would transfer ownership to
    // di-simplification at stale_tip, making all commits below it attributed
    // to di-simplification instead of master.
    const commits: GraphCommit[] = [
      commit("feat1", ["feat2"], ["feature/SBOF-9574"]),
      commit("feat2", ["master_tip"]),
      commit("master_tip", ["m3"], ["master", "origin/master"]),
      commit("m3", ["stale_tip"]),
      commit("stale_tip", ["m5"], ["di-simplification"]),
      commit("m5", ["m6"]),
      commit("m6", []),
    ];

    const layout = computeGraphLayout(
      commits,
      ["feature/SBOF-9574", "master", "di-simplification", "origin/master"],
      new Set(),
      "feature/SBOF-9574",
      50,
      "origin",
    );

    // master_tip should be attributed to master (ownership transfer).
    const nodeMasterTip = layout.nodes.find((n) => n.hash === "master_tip");
    expect(nodeMasterTip).toBeDefined();
    expect(nodeMasterTip!.branch).toBe("master");

    // stale_tip should NOT be attributed to di-simplification.
    // It should remain as master (since di-simplification has no remote tracking).
    const nodeStaleTip = layout.nodes.find((n) => n.hash === "stale_tip");
    expect(nodeStaleTip).toBeDefined();
    expect(nodeStaleTip!.branch).toBe("master");

    // Commits below stale_tip should also be master, not di-simplification.
    const nodeM5 = layout.nodes.find((n) => n.hash === "m5");
    expect(nodeM5).toBeDefined();
    expect(nodeM5!.branch).toBe("master");

    // di-simplification should own NO commits (it's a stale pointer on the waterfall).
    const diNodes = layout.nodes.filter((n) => n.branch === "di-simplification");
    expect(diNodes).toHaveLength(0);
  });

  it("stale branch with remote tracking still cannot steal waterfall ownership", () => {
    // Same scenario as above, but di-simplification has a remote (origin/di-simplification).
    // Only the remote DEFAULT branch's local equivalent should get ownership transfer.
    const commits: GraphCommit[] = [
      commit("feat1", ["feat2"], ["feature/SBOF-9574"]),
      commit("feat2", ["master_tip"]),
      commit("master_tip", ["m3"], ["master", "origin/master"]),
      commit("m3", ["stale_tip"]),
      commit("stale_tip", ["m5"], ["di-simplification", "origin/di-simplification"]),
      commit("m5", ["m6"]),
      commit("m6", []),
    ];

    const layout = computeGraphLayout(
      commits,
      ["feature/SBOF-9574", "master", "di-simplification", "origin/master", "origin/di-simplification"],
      new Set(),
      "feature/SBOF-9574",
      50,
      "origin",
    );

    // stale_tip should still be master, not di-simplification.
    const nodeStaleTip = layout.nodes.find((n) => n.hash === "stale_tip");
    expect(nodeStaleTip).toBeDefined();
    expect(nodeStaleTip!.branch).toBe("master");

    // Everything below master_tip should be master.
    const nodeM5 = layout.nodes.find((n) => n.hash === "m5");
    expect(nodeM5).toBeDefined();
    expect(nodeM5!.branch).toBe("master");
  });

  it("only remote default local name gets ownership — not other tracked branches", () => {
    // develop has a remote (origin/develop) but is NOT the remote default.
    // It should NOT get ownership transfer even though it has a remote.
    const commits: GraphCommit[] = [
      commit("feat1", ["dev_tip"], ["feature/x"]),
      commit("dev_tip", ["m1"], ["develop", "origin/develop"]),
      commit("m1", ["m2"], ["master", "origin/master"]),
      commit("m2", []),
    ];

    const layout = computeGraphLayout(
      commits,
      ["feature/x", "develop", "master", "origin/master", "origin/develop"],
      new Set(),
      "feature/x",
      50,
      "origin",
    );

    // master should get ownership (it's the remote default's local name).
    const nodeM1 = layout.nodes.find((n) => n.hash === "m1");
    expect(nodeM1!.branch).toBe("master");

    // develop should NOT get ownership via localTipOwner — its tip is on
    // the waterfall but it's not the remote default.
    const nodeDevTip = layout.nodes.find((n) => n.hash === "dev_tip");
    expect(nodeDevTip!.branch).toBe("feature/x");
  });
});

describe("computeGraphLayout — cross-column edge deduplication", () => {
  it("side branch with multiple merges from master only has fork-point edge", () => {
    // Scenario: hotfix branch merges from master twice (common pattern:
    // "merge master into hotfix" done multiple times during development).
    // Only the fork-point (bottommost) cross-column edge should be emitted.
    // The intermediate merges-from-main are deduplicated.
    //
    // hotfix: h_tip (merge: h2 + m1) → h2 (merge: h3 + m3) → h3 → fork_point (on waterfall)
    // waterfall: feat1 → m1 → m2 → m3 → m4 → m5
    const commits: GraphCommit[] = [
      commit("feat1", ["m1"], ["feature/x"]),
      commit("h_tip", ["h2", "m1"], ["hotfix/y"]), // merge from master
      commit("m1", ["m2"], ["master", "origin/master"]),
      commit("h2", ["h3", "m3"]),                   // merge from master again
      commit("m2", ["m3"]),
      commit("h3", ["m4"]),                          // fork point
      commit("m3", ["m4"]),
      commit("m4", ["m5"]),
      commit("m5", []),
    ];

    const layout = computeGraphLayout(
      commits,
      ["feature/x", "hotfix/y", "master", "origin/master"],
      new Set(),
      "feature/x",
      50,
      "origin",
    );

    // hotfix should be on a side column (not 0).
    const hotfixNodes = layout.nodes.filter((n) => n.branch === "hotfix/y");
    expect(hotfixNodes.length).toBeGreaterThan(0);
    expect(hotfixNodes[0].column).toBeGreaterThan(0);

    // Count cross-column edges involving the hotfix column.
    const hotfixCol = hotfixNodes[0].column;
    const crossEdges = layout.edges.filter(
      (e) =>
        (e.fromColumn === hotfixCol && e.toColumn !== hotfixCol) ||
        (e.toColumn === hotfixCol && e.fromColumn !== hotfixCol),
    );

    // Should be exactly 1: just the fork point (h3 → m4).
    // The intermediate merges-from-main (h_tip→m1, h2→m3) are deduplicated.
    expect(crossEdges).toHaveLength(1);
    expect(crossEdges[0].fromRow).toBe(
      layout.nodes.findIndex((n) => n.hash === "h3"),
    );
  });

  it("unmerged branch still gets its single fork-point edge", () => {
    // A branch that was never merged into master still needs its one
    // cross-column edge at the fork point.
    const commits: GraphCommit[] = [
      commit("feat1", ["m1"], ["feature/x"]),
      commit("b1", ["b2"], ["side-branch"]),
      commit("m1", ["m2"], ["master", "origin/master"]),
      commit("b2", ["m2"]),  // fork point
      commit("m2", []),
    ];

    const layout = computeGraphLayout(
      commits,
      ["feature/x", "side-branch", "master", "origin/master"],
      new Set(),
      "feature/x",
      50,
      "origin",
    );

    const sideNodes = layout.nodes.filter((n) => n.branch === "side-branch");
    expect(sideNodes.length).toBe(2); // b1 and b2
    const sideCol = sideNodes[0].column;

    // Should have exactly 1 cross-column edge (b2 → m2, the fork point).
    const crossEdges = layout.edges.filter(
      (e) =>
        (e.fromColumn === sideCol && e.toColumn !== sideCol) ||
        (e.toColumn === sideCol && e.fromColumn !== sideCol),
    );
    expect(crossEdges).toHaveLength(1);
  });

  it("multiple side branches each get their own cross-column edge", () => {
    // Two independent side branches should each get one edge.
    const commits: GraphCommit[] = [
      commit("feat1", ["m1"], ["feature/x"]),
      commit("a1", ["m1"], ["branch-a"]),
      commit("b1", ["m2"], ["branch-b"]),
      commit("m1", ["m2"], ["master", "origin/master"]),
      commit("m2", []),
    ];

    const layout = computeGraphLayout(
      commits,
      ["feature/x", "branch-a", "branch-b", "master", "origin/master"],
      new Set(),
      "feature/x",
      50,
      "origin",
    );

    const colA = layout.nodes.find((n) => n.hash === "a1")!.column;
    const colB = layout.nodes.find((n) => n.hash === "b1")!.column;
    expect(colA).not.toBe(0);
    expect(colB).not.toBe(0);
    expect(colA).not.toBe(colB);

    // Each branch gets exactly one cross-column edge.
    const crossA = layout.edges.filter(
      (e) => e.fromColumn === colA || e.toColumn === colA,
    ).filter((e) => e.fromColumn !== e.toColumn);
    const crossB = layout.edges.filter(
      (e) => e.fromColumn === colB || e.toColumn === colB,
    ).filter((e) => e.fromColumn !== e.toColumn);
    expect(crossA).toHaveLength(1);
    expect(crossB).toHaveLength(1);
  });
});

describe("computeGraphLayout — merged feature branch visibility", () => {
  it("merged feature branch is not flattened into main", () => {
    // Scenario matching the user's RepoTest repository:
    // main has a merge commit that merges feature/auth.
    // feature/auth's tip IS the second parent of the merge.
    // When all branches are visible, feature/auth must appear on its own lane.
    //
    // Graph (topological order, newest first):
    //   main: c_log → c_cache → c_xss → c_util → merge → c_styles → c_api → c_pkg → c_entry → c_init
    //   merge parents: [c_styles, c_reg]  (first-parent is main lineage)
    //   feature/auth: c_reg → c_logout → c_login → c_pkg (fork point)
    const commits: GraphCommit[] = [
      commit("c_log", ["c_cache"], ["main"], "Add logging module"),
      commit("c_cache", ["c_xss"], [], "Add caching layer"),
      commit("c_xss", ["c_util"], ["origin/main"], "fix: patch XSS vulnerability"),
      commit("c_util", ["merge"], [], "Add utility functions"),
      commit("merge", ["c_styles", "c_reg"], [], "Merge feature/auth into main"),
      commit("c_reg", ["c_logout"], ["feature/auth"], "feat: add registration"),
      commit("c_styles", ["c_api"], [], "Add base styles"),
      commit("c_logout", ["c_login"], [], "feat: add logout function"),
      commit("c_api", ["c_pkg"], [], "Add API module"),
      commit("c_login", ["c_pkg"], [], "feat: add login function"),
      commit("c_pkg", ["c_entry"], [], "Add package.json"),
      commit("c_entry", ["c_init"], [], "Add entry point"),
      commit("c_init", [], [], "Initial commit"),
    ];

    const layout = computeGraphLayout(
      commits,
      ["main", "feature/auth", "origin/main"],
      new Set(["c_log", "c_cache"]),
      "main",
      50,
      "origin",
    );

    // feature/auth should have its own column (not 0).
    const authNodes = layout.nodes.filter((n) => n.branch === "feature/auth");
    expect(authNodes).toHaveLength(3); // c_reg, c_logout, c_login
    expect(authNodes[0].column).toBeGreaterThan(0);

    // Verify the correct commits are on feature/auth.
    const authHashes = authNodes.map((n) => n.hash).sort();
    expect(authHashes).toContain("c_reg");
    expect(authHashes).toContain("c_logout");
    expect(authHashes).toContain("c_login");

    // main should own its first-parent chain commits.
    const mainNodes = layout.nodes.filter((n) => n.branch === "main");
    const mainHashes = mainNodes.map((n) => n.hash);
    expect(mainHashes).toContain("c_log");
    expect(mainHashes).toContain("merge");
    expect(mainHashes).toContain("c_styles");
    expect(mainHashes).toContain("c_pkg");

    // All main nodes should be on column 0.
    for (const n of mainNodes) {
      expect(n.column).toBe(0);
    }
  });

  it("merged branch gets both merge-back and fork cross-column edges", () => {
    // Same structure as above. We need two cross-column edges for feature/auth:
    // 1. The merge-back edge: merge commit (main) → c_reg (feature/auth)
    // 2. The fork edge: c_login (feature/auth) → c_pkg (main)
    const commits: GraphCommit[] = [
      commit("c_log", ["c_cache"], ["main"], "Add logging module"),
      commit("c_cache", ["merge"], [], "Add caching layer"),
      commit("merge", ["c_styles", "c_reg"], [], "Merge feature/auth into main"),
      commit("c_reg", ["c_logout"], ["feature/auth"], "feat: add registration"),
      commit("c_styles", ["c_pkg"], [], "Add base styles"),
      commit("c_logout", ["c_login"], [], "feat: add logout function"),
      commit("c_login", ["c_pkg"], [], "feat: add login function"),
      commit("c_pkg", ["c_init"], [], "Add package.json"),
      commit("c_init", [], [], "Initial commit"),
    ];

    const layout = computeGraphLayout(
      commits,
      ["main", "feature/auth"],
      new Set(),
      "main",
      50,
    );

    const authCol = layout.nodes.find((n) => n.branch === "feature/auth")!.column;
    expect(authCol).toBeGreaterThan(0);

    // Find cross-column edges involving the feature/auth column.
    const crossEdges = layout.edges.filter(
      (e) =>
        (e.fromColumn === authCol && e.toColumn !== authCol) ||
        (e.toColumn === authCol && e.fromColumn !== authCol),
    );

    // Should have 2 edges: merge-back + fork.
    expect(crossEdges).toHaveLength(2);

    // The merge-back edge: from main (col 0) to feature/auth (authCol).
    const mergeBackEdge = crossEdges.find((e) => e.fromColumn === 0 && e.toColumn === authCol);
    expect(mergeBackEdge).toBeDefined();
    expect(mergeBackEdge!.fromRow).toBe(layout.nodes.findIndex((n) => n.hash === "merge"));
    expect(mergeBackEdge!.toRow).toBe(layout.nodes.findIndex((n) => n.hash === "c_reg"));

    // The fork edge: from feature/auth (authCol) to main (col 0).
    const forkEdge = crossEdges.find((e) => e.fromColumn === authCol && e.toColumn === 0);
    expect(forkEdge).toBeDefined();
    expect(forkEdge!.fromRow).toBe(layout.nodes.findIndex((n) => n.hash === "c_login"));
    expect(forkEdge!.toRow).toBe(layout.nodes.findIndex((n) => n.hash === "c_pkg"));
  });

  it("feature branch with additional unmerged branch both show correctly", () => {
    // Scenario: main merged feature/auth, and there's also feature/dashboard
    // that forked from main after the merge point. Both should show on their
    // own lanes.
    const commits: GraphCommit[] = [
      commit("d4", ["d3"], ["feature/dashboard"], "feat: add sidebar"),
      commit("d3", ["d2"], [], "feat: add table"),
      commit("d2", ["d1"], [], "feat: add chart"),
      commit("d1", ["c_util"], [], "feat: scaffold dashboard"),
      commit("c_log", ["c_cache"], ["main"], "Add logging module"),
      commit("c_cache", ["c_xss"], [], "Add caching layer"),
      commit("c_xss", ["c_util"], ["origin/main"], "fix: patch XSS"),
      commit("c_util", ["merge"], [], "Add utility functions"),
      commit("merge", ["c_styles", "c_reg"], [], "Merge feature/auth into main"),
      commit("c_reg", ["c_logout"], ["feature/auth"], "feat: add registration"),
      commit("c_styles", ["c_api"], [], "Add base styles"),
      commit("c_logout", ["c_login"], [], "feat: add logout function"),
      commit("c_api", ["c_pkg"], [], "Add API module"),
      commit("c_login", ["c_pkg"], [], "feat: add login function"),
      commit("c_pkg", ["c_entry"], [], "Add package.json"),
      commit("c_entry", ["c_init"], [], "Add entry point"),
      commit("c_init", [], [], "Initial commit"),
    ];

    const layout = computeGraphLayout(
      commits,
      ["main", "feature/auth", "feature/dashboard", "origin/main"],
      new Set(["c_log", "c_cache"]),
      "main",
      50,
      "origin",
    );

    // Three distinct column groups.
    const mainNodes = layout.nodes.filter((n) => n.branch === "main");
    const authNodes = layout.nodes.filter((n) => n.branch === "feature/auth");
    const dashNodes = layout.nodes.filter((n) => n.branch === "feature/dashboard");

    expect(mainNodes.length).toBeGreaterThan(0);
    expect(authNodes).toHaveLength(3);
    expect(dashNodes).toHaveLength(4);

    // Each on their own column.
    const mainCol = mainNodes[0].column;
    const authCol = authNodes[0].column;
    const dashCol = dashNodes[0].column;
    expect(mainCol).toBe(0);
    expect(authCol).toBeGreaterThan(0);
    expect(dashCol).toBeGreaterThan(0);
    expect(authCol).not.toBe(dashCol);
  });

  it("feature branch whose tip is NOT a merge parent is unaffected by Phase 1.5", () => {
    // If a dead branch shares ancestors with a merge's second parent,
    // Phase 1.5 should still claim those shared ancestors (the dead branch
    // doesn't have its tip as the merge parent).
    const commits: GraphCommit[] = [
      commit("feat1", ["m1"], ["main"]),
      commit("m1", ["merge"], [], "main commit"),
      commit("merge", ["m2", "f2"], [], "merge old-feature"),
      commit("dead1", ["dead2"], ["dead-branch"]),
      commit("dead2", ["f2"]),
      commit("f2", ["f1"], [], "feature commit 2"), // second parent of merge, NO branch tip here
      commit("m2", ["base"]),
      commit("f1", ["base"], [], "feature commit 1"),
      commit("base", []),
    ];

    const layout = computeGraphLayout(
      commits,
      ["main", "dead-branch"],
      new Set(),
      "main",
      50,
    );

    // f2 and f1 should be claimed by main (via Phase 1.5) since no visible
    // branch tip points directly to f2.
    const nodeF2 = layout.nodes.find((n) => n.hash === "f2");
    expect(nodeF2).toBeDefined();
    expect(nodeF2!.branch).toBe("main");

    // dead-branch should only own its unique commits.
    const deadNodes = layout.nodes.filter((n) => n.branch === "dead-branch");
    const deadHashes = deadNodes.map((n) => n.hash).sort();
    expect(deadHashes).toContain("dead1");
    expect(deadHashes).toContain("dead2");
    expect(deadHashes).not.toContain("f2");
    expect(deadHashes).not.toContain("f1");
  });

  it("branch tip that IS the merge parent gets its own lane", () => {
    // Simplest case: single merge, feature branch tip = merge's second parent.
    const commits: GraphCommit[] = [
      commit("m2", ["merge"], ["main"]),
      commit("merge", ["m1", "f2"], [], "Merge feature into main"),
      commit("f2", ["f1"], ["feature"], "feature commit 2"),
      commit("m1", ["base"]),
      commit("f1", ["base"], [], "feature commit 1"),
      commit("base", []),
    ];

    const layout = computeGraphLayout(
      commits,
      ["main", "feature"],
      new Set(),
      "main",
      50,
    );

    // feature should own f2 and f1.
    const featureNodes = layout.nodes.filter((n) => n.branch === "feature");
    expect(featureNodes).toHaveLength(2);
    expect(featureNodes.map((n) => n.hash).sort()).toEqual(["f1", "f2"]);

    // feature should be on its own column.
    expect(featureNodes[0].column).toBeGreaterThan(0);
  });
});
