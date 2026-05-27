import { describe, it, expect } from "vitest";
import { extractDiffContent, findDiffMatches } from "./useDiffSearch";
import type { FileDiff } from "../../../ipc/bindings";

describe("useDiffSearch utilities", () => {
  describe("extractDiffContent", () => {
    it("extracts content from a diff with hunks", () => {
      const diff: FileDiff = {
        path: "test.ts",
        hunks: [
          {
            header: "@@ -1,3 +1,4 @@",
            old_start: 1,
            new_start: 1,
            lines: [
              { kind: "Context", content: "function add(a, b) {", old_lineno: 1, new_lineno: 1 },
              { kind: "Deletion", content: "  return a - b;", old_lineno: 2, new_lineno: null },
              { kind: "Addition", content: "  return a + b;", old_lineno: null, new_lineno: 2 },
              { kind: "Context", content: "}", old_lineno: 3, new_lineno: 3 },
            ],
          },
        ],
      };

      const content = extractDiffContent(diff);

      expect(content).toHaveLength(4);
      expect(content[0]).toEqual({
        content: "function add(a, b) {",
        kind: "context",
      });
      expect(content[1]).toEqual({
        content: "  return a - b;",
        kind: "deletion",
      });
      expect(content[2]).toEqual({
        content: "  return a + b;",
        kind: "addition",
      });
      expect(content[3]).toEqual({
        content: "}",
        kind: "context",
      });
    });

    it("returns empty array for null diff", () => {
      const content = extractDiffContent(null);
      expect(content).toEqual([]);
    });

    it("returns empty array for diff with no hunks", () => {
      const diff: FileDiff = { path: "empty.ts", hunks: [] };
      const content = extractDiffContent(diff);
      expect(content).toEqual([]);
    });
  });

  describe("findDiffMatches", () => {
    const createTestDiff = (): FileDiff => ({
      path: "test.ts",
      hunks: [
        {
          header: "@@ -1,4 +1,4 @@",
          old_start: 1,
          new_start: 1,
          lines: [
            { kind: "Context", content: "const name = 'Alice';", old_lineno: 1, new_lineno: 1 },
            { kind: "Deletion", content: "console.log(name);", old_lineno: 2, new_lineno: null },
            { kind: "Addition", content: "console.log(name, name);", old_lineno: null, new_lineno: 2 },
            { kind: "Context", content: "console.log(age);", old_lineno: 3, new_lineno: 3 },
          ],
        },
      ],
    });

    it("finds all occurrences of a search term (case-insensitive)", async () => {
      const diff = createTestDiff();
      const matches = await findDiffMatches(diff, "name");

      // "name" appears in:
      // Line 0: "const name = 'Alice';" (1 match)
      // Line 1: "console.log(name);" (1 match)
      // Line 2: "console.log(name, name);" (2 matches)
      // Line 3: "console.log(age);" (0 matches)
      expect(matches).toHaveLength(4);
      expect(matches[0]).toEqual({ lineIndex: 0, charOffset: 6, length: 4, kind: "context" });
      expect(matches[1]).toEqual({ lineIndex: 1, charOffset: 12, length: 4, kind: "deletion" });
      expect(matches[2]).toEqual({ lineIndex: 2, charOffset: 12, length: 4, kind: "addition" });
      expect(matches[3]).toEqual({ lineIndex: 2, charOffset: 18, length: 4, kind: "addition" });
    });

    it("finds matches case-insensitively", async () => {
      const diff = createTestDiff();
      const matches = await findDiffMatches(diff, "NAME");

      // Should find "name" even though search is "NAME"
      expect(matches.length).toBeGreaterThan(0);
      expect(matches.every((m) => m.length === 4)).toBe(true);
    });

    it("returns empty array when search term is empty", async () => {
      const diff = createTestDiff();
      const matches = await findDiffMatches(diff, "");

      expect(matches).toEqual([]);
    });

    it("returns empty array when no matches found", async () => {
      const diff = createTestDiff();
      const matches = await findDiffMatches(diff, "nonexistent");

      expect(matches).toEqual([]);
    });

    it("returns empty array for null diff", async () => {
      const matches = await findDiffMatches(null, "test");

      expect(matches).toEqual([]);
    });

    it("searches in both additions and deletions", async () => {
      const diff = createTestDiff();
      const matches = await findDiffMatches(diff, "console");

      // "console" appears in lines 1, 2, and 3
      expect(matches.length).toBe(3);
      expect(matches[0].kind).toBe("deletion");
      expect(matches[1].kind).toBe("addition");
      expect(matches[2].kind).toBe("context");
    });

    it("handles multiple matches on the same line", async () => {
      const diff = createTestDiff();
      const matches = await findDiffMatches(diff, "name");

      // Find all matches on line 2 (the one with "console.log(name, name);")
      const line2Matches = matches.filter((m) => m.lineIndex === 2);
      expect(line2Matches).toHaveLength(2);
      expect(line2Matches[0].charOffset).toBe(12);
      expect(line2Matches[1].charOffset).toBe(18);
    });
  });
});
