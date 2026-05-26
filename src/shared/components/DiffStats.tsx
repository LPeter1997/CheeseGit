import { formatCount } from "../utils/format-count";

interface DiffStatsProps {
  additions: number;
  deletions: number;
  className?: string;
  /** Fixed column width (in ch) for the additions part. Enables aligned mode. */
  addWidth?: number;
  /** Fixed column width (in ch) for the deletions part. Enables aligned mode. */
  delWidth?: number;
}

/**
 * Compact additions / deletions display.
 *
 * Renders e.g. `+42 −3` or `+2.3K −150` with success/danger colors.
 * Omits a side entirely when its count is 0.
 *
 * When `addWidth` / `delWidth` are provided the component switches to
 * fixed-column mode so that numbers align vertically across sibling rows.
 */
export function DiffStats({ additions, deletions, className = "", addWidth, delWidth }: DiffStatsProps) {
  const add = formatCount(additions);
  const del = formatCount(deletions);
  if (!add && !del) return null;

  // Aligned mode: fixed-width columns for cross-row alignment
  if (addWidth != null || delWidth != null) {
    return (
      <span className={`flex-shrink-0 tabular-nums whitespace-nowrap ${className}`}>
        {addWidth != null && (
          <span className="text-success inline-block text-right" style={{ minWidth: `${addWidth}ch` }}>
            {add ? `+${add}` : ""}
          </span>
        )}
        {addWidth != null && delWidth != null && " "}
        {delWidth != null && (
          <span className="text-danger inline-block text-right" style={{ minWidth: `${delWidth}ch` }}>
            {del ? `−${del}` : ""}
          </span>
        )}
      </span>
    );
  }

  // Compact mode: omit empty sides
  return (
    <span className={`flex-shrink-0 tabular-nums whitespace-nowrap ${className}`}>
      {add && <span className="text-success">+{add}</span>}
      {add && del && " "}
      {del && <span className="text-danger">−{del}</span>}
    </span>
  );
}

/** Compute column widths (in ch) from a stats map. Returns `undefined` when no data. */
export function computeStatWidths(stats: Map<string, { additions: number; deletions: number }>) {
  let maxAdd = 0;
  let maxDel = 0;
  for (const s of stats.values()) {
    const a = formatCount(s.additions);
    const d = formatCount(s.deletions);
    if (a) maxAdd = Math.max(maxAdd, a.length + 1); // +1 for "+"
    if (d) maxDel = Math.max(maxDel, d.length + 1); // +1 for "−"
  }
  return { addWidth: maxAdd || undefined, delWidth: maxDel || undefined };
}
