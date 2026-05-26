import { describe, it, expect } from "vitest";
import { formatCount } from "./format-count";

describe("formatCount", () => {
  it("returns empty string for 0", () => {
    expect(formatCount(0)).toBe("");
  });

  it("returns raw number for values under 1000", () => {
    expect(formatCount(1)).toBe("1");
    expect(formatCount(42)).toBe("42");
    expect(formatCount(999)).toBe("999");
  });

  it("formats thousands with K suffix", () => {
    expect(formatCount(1000)).toBe("1K");
    expect(formatCount(1100)).toBe("1.1K");
    expect(formatCount(1500)).toBe("1.5K");
    expect(formatCount(2300)).toBe("2.3K");
    expect(formatCount(9999)).toBe("10K");
  });

  it("formats ten-thousands and above without decimal", () => {
    expect(formatCount(10000)).toBe("10K");
    expect(formatCount(25000)).toBe("25K");
    expect(formatCount(100000)).toBe("100K");
    expect(formatCount(999999)).toBe("1000K");
  });

  it("formats millions with M suffix", () => {
    expect(formatCount(1000000)).toBe("1M");
    expect(formatCount(1500000)).toBe("1.5M");
    expect(formatCount(2300000)).toBe("2.3M");
  });

  it("returns empty string for negative values", () => {
    expect(formatCount(-5)).toBe("");
  });
});
