import type { GraphCommit } from "../../../ipc/bindings";

/** A branch lane in the graph visualization. */
export interface GraphLane {
  /** Branch name. */
  branch: string;
  /** Lane index (column, 0-based from left). */
  column: number;
  /** CSS color for this branch. */
  color: string;
}

/** A node (commit dot) in the rendered graph. */
export interface GraphNode {
  /** Row index (matches commit index). */
  row: number;
  /** Lane column this commit sits on. */
  column: number;
  /** The branch this commit belongs to. */
  branch: string;
  /** Color for this node. */
  color: string;
  /** Whether this commit is local-only (not pushed to remote). */
  isLocalOnly: boolean;
  /** The commit hash. */
  hash: string;
}

/** An edge connecting two nodes in the graph. */
export interface GraphEdge {
  /** Starting row. */
  fromRow: number;
  /** Starting column. */
  fromColumn: number;
  /** Ending row. */
  toRow: number;
  /** Ending column. */
  toColumn: number;
  /** Color for this edge. */
  color: string;
  /** Pre-computed Y pixel coordinate of the source node center. */
  fromY: number;
  /** Pre-computed Y pixel coordinate of the target node center. */
  toY: number;
}

/** The complete layout result for rendering. */
export interface GraphLayout {
  /** Lanes (one per branch). */
  lanes: GraphLane[];
  /** One node per commit row. */
  nodes: GraphNode[];
  /** Edges connecting parent-child commits. */
  edges: GraphEdge[];
  /** Total number of columns used. */
  columnCount: number;
  /** Row height used for Y computations. */
  rowHeight: number;
  /** The filtered commits (only those belonging to visible branches). */
  commits: GraphCommit[];
}

/**
 * Palette of colors for branch lanes. Based on typical git graph UIs.
 * Chosen for readability on both light and dark backgrounds.
 */
const BRANCH_COLORS = [
  "#0ea5e9", // sky-500
  "#a855f7", // purple-500
  "#f97316", // orange-500
  "#22c55e", // green-500
  "#ef4444", // red-500
  "#eab308", // yellow-500
  "#ec4899", // pink-500
  "#14b8a6", // teal-500
];

/**
 * Determine the "required" branches for a given current branch.
 * These are the current branch plus any local branch whose tip sits on the
 * current branch's first-parent chain (the waterfall ancestors).
 */
export function computeRequiredBranches(
  commits: GraphCommit[],
  branches: string[],
  currentBranch: string,
  remote?: string | null,
): string[] {
  const isRemote = (b: string) => (remote ? b.startsWith(remote + "/") : false);

  const hashToIndex = new Map<string, number>();
  for (let i = 0; i < commits.length; i++) {
    hashToIndex.set(commits[i].hash, i);
  }

  // Find branch tips.
  const branchTips = new Map<string, string>();
  for (const commit of commits) {
    for (const ref of commit.refs) {
      if (branches.includes(ref) && !branchTips.has(ref)) {
        branchTips.set(ref, commit.hash);
      }
    }
  }

  // Local branch tip hashes (excl. current).
  const localTipOwner = new Map<string, string>();
  for (const [branch, hash] of branchTips) {
    if (branch !== currentBranch && !isRemote(branch)) {
      localTipOwner.set(hash, branch);
    }
  }

  // Walk the current branch's first-parent chain and collect waterfall branches.
  const required = new Set<string>();
  required.add(currentBranch);
  const tip = branchTips.get(currentBranch);
  if (tip) {
    let h: string | undefined = tip;
    while (h && hashToIndex.has(h)) {
      const owner = localTipOwner.get(h);
      if (owner) required.add(owner);
      h = commits[hashToIndex.get(h)!].parents[0];
    }
  }
  return [...required];
}

/**
 * Compute the graph layout for a set of commits across branches.
 *
 * Algorithm:
 * 1. "Waterfall" — walk the current branch's first-parent chain. Ownership
 *    starts with the current branch and transfers whenever we hit another
 *    *local* branch's tip. Remote-tracking branches are ignored.
 * 2. Side branches (tips not on the current chain) claim unclaimed commits.
 * 3. Branches with zero unique commits are pruned from the lane list.
 * 4. Edges connect parent-child nodes with proper colors.
 */
export function computeGraphLayout(
  commits: GraphCommit[],
  branches: string[],
  localOnlyCommits: Set<string>,
  currentBranch: string,
  rowHeight: number,
  remote?: string | null,
): GraphLayout {
  if (commits.length === 0 || branches.length === 0) {
    return { lanes: [], nodes: [], edges: [], columnCount: 0, rowHeight, commits: [] };
  }

  const isRemoteBranch = (b: string) =>
    remote ? b.startsWith(remote + "/") : false;

  // Build hash → index lookup.
  const hashToIndex = new Map<string, number>();
  for (let i = 0; i < commits.length; i++) {
    hashToIndex.set(commits[i].hash, i);
  }

  // Find branch tips from commit decorations.
  const branchTips = new Map<string, string>();
  for (const commit of commits) {
    for (const ref of commit.refs) {
      if (branches.includes(ref) && !branchTips.has(ref)) {
        branchTips.set(ref, commit.hash);
      }
    }
  }

  // Build a reverse map: hash → local branch name for tips (excl. current).
  const localTipOwner = new Map<string, string>();
  for (const [branch, hash] of branchTips) {
    if (branch !== currentBranch && !isRemoteBranch(branch)) {
      localTipOwner.set(hash, branch);
    }
  }

  // --- Phase 1: Waterfall along the current branch's first-parent chain ---
  // Ownership starts with the current branch. When we encounter another
  // local branch's tip, ownership transfers to that branch for the rest
  // of the chain (until the next local tip, etc.).
  const commitBranch = new Map<string, string>();
  const waterfallBranches = new Set<string>();
  {
    const tip = branchTips.get(currentBranch);
    if (tip) {
      let owner = currentBranch;
      waterfallBranches.add(owner);
      let h: string | undefined = tip;
      while (h && hashToIndex.has(h)) {
        // Transfer ownership at local branch tips (skip the very first
        // commit which is the current branch's own tip).
        if (h !== tip) {
          const newOwner = localTipOwner.get(h);
          if (newOwner) {
            owner = newOwner;
            waterfallBranches.add(owner);
          }
        }
        commitBranch.set(h, owner);
        h = commits[hashToIndex.get(h)!].parents[0];
      }
    }
  }

  // --- Phase 2: Side branches claim unclaimed first-parent commits ---
  const otherBranches = branches.filter((b) => b !== currentBranch).sort();
  for (const branch of otherBranches) {
    const tip = branchTips.get(branch);
    if (!tip) continue;
    // Skip branches whose tip is already on the waterfall chain.
    if (commitBranch.has(tip)) continue;
    let h: string | undefined = tip;
    while (h && hashToIndex.has(h)) {
      if (commitBranch.has(h)) break;
      commitBranch.set(h, branch);
      h = commits[hashToIndex.get(h)!].parents[0];
    }
  }

  // --- Phase 3: Prune branches with no unique commits ---
  const activeBranches = new Set<string>();
  activeBranches.add(currentBranch);
  for (const [, branch] of commitBranch) {
    activeBranches.add(branch);
  }
  const sortedBranches = [...activeBranches].sort((a, b) => {
    if (a === currentBranch) return -1;
    if (b === currentBranch) return 1;
    return a.localeCompare(b);
  });

  // Assign lanes. Waterfall branches (sequential on the first-parent
  // chain) share column 0 — no visual fork when there are no parallel
  // commits. Side branches get columns 1, 2, …
  const waterfallActive = sortedBranches.filter((b) => waterfallBranches.has(b));
  const sideActive = sortedBranches.filter((b) => !waterfallBranches.has(b));
  let colorIdx = 0;
  const lanes: GraphLane[] = [];
  for (const branch of waterfallActive) {
    lanes.push({
      branch,
      column: 0,
      color: BRANCH_COLORS[colorIdx++ % BRANCH_COLORS.length],
    });
  }
  for (let i = 0; i < sideActive.length; i++) {
    lanes.push({
      branch: sideActive[i],
      column: i + 1,
      color: BRANCH_COLORS[colorIdx++ % BRANCH_COLORS.length],
    });
  }
  const branchToLane = new Map<string, GraphLane>();
  for (const lane of lanes) {
    branchToLane.set(lane.branch, lane);
  }

  // Filter to only commits claimed by a visible branch, and re-index rows.
  const filteredCommits: GraphCommit[] = [];
  const oldToNewRow = new Map<number, number>();
  for (let i = 0; i < commits.length; i++) {
    if (commitBranch.has(commits[i].hash)) {
      oldToNewRow.set(i, filteredCommits.length);
      filteredCommits.push(commits[i]);
    }
  }

  // Rebuild hash → new row index for filtered commits.
  const filteredHashToRow = new Map<string, number>();
  for (let i = 0; i < filteredCommits.length; i++) {
    filteredHashToRow.set(filteredCommits[i].hash, i);
  }

  // Build nodes (only for filtered commits).
  const nodes: GraphNode[] = [];
  for (let i = 0; i < filteredCommits.length; i++) {
    const commit = filteredCommits[i];
    const branch = commitBranch.get(commit.hash) ?? currentBranch;
    const lane = branchToLane.get(branch) ?? lanes[0];
    nodes.push({
      row: i,
      column: lane.column,
      branch,
      color: lane.color,
      isLocalOnly: localOnlyCommits.has(commit.hash),
      hash: commit.hash,
    });
  }

  // Build edges (only between filtered commits).
  const edges: GraphEdge[] = [];
  for (let i = 0; i < filteredCommits.length; i++) {
    const commit = filteredCommits[i];
    const node = nodes[i];
    for (const parentHash of commit.parents) {
      const parentRow = filteredHashToRow.get(parentHash);
      if (parentRow === undefined) continue;
      const parentNode = nodes[parentRow];
      edges.push({
        fromRow: node.row,
        fromColumn: node.column,
        toRow: parentNode.row,
        toColumn: parentNode.column,
        color: node.column >= parentNode.column ? node.color : parentNode.color,
        fromY: node.row * rowHeight + rowHeight / 2,
        toY: parentNode.row * rowHeight + rowHeight / 2,
      });
    }
  }

  const columnCount = lanes.length > 0 ? Math.max(...lanes.map((l) => l.column)) + 1 : 0;
  return { lanes, nodes, edges, columnCount, rowHeight, commits: filteredCommits };
}
