/**
 * Format a number into a compact string with at most 3–4 visible characters.
 *
 *   0        → ""
 *   1–999    → "1", "42", "999"
 *   1 000    → "1K"
 *   1 100    → "1.1K"
 *   10 000   → "10K"
 *   999 999  → "999K"
 *   1 000 000 → "1M"
 */
export function formatCount(n: number): string {
  if (n <= 0) return "";
  if (n < 1_000) return String(n);
  if (n < 10_000) {
    const k = n / 1_000;
    const rounded = Math.round(k * 10) / 10;
    return rounded % 1 === 0 ? `${rounded}K` : `${rounded}K`;
  }
  if (n < 1_000_000) return `${Math.round(n / 1_000)}K`;
  const m = n / 1_000_000;
  const rounded = Math.round(m * 10) / 10;
  return `${rounded}M`;
}
