import { describe, expect, it } from "vitest";
import { shortenFilenamePreferExtension } from "./SmartPath";

describe("shortenFilenamePreferExtension", () => {
  it("preserves extension when enough room", () => {
    const short = shortenFilenamePreferExtension("very-very-loooooooong-filename.txt", 22);
    expect(short.endsWith(".txt")).toBe(true);
    expect(short.includes("…")).toBe(true);
  });

  it("falls back to end truncation when extension cannot fit", () => {
    const short = shortenFilenamePreferExtension("very-very-loooooooong-filename.txt", 4);
    expect(short).toBe("ver…");
  });

  it("returns full filename when it already fits", () => {
    expect(shortenFilenamePreferExtension("file.ts", 20)).toBe("file.ts");
  });
});
