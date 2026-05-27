import { describe, expect, it } from "vitest";
import { formatStashMessage } from "./format-stash-message";

describe("formatStashMessage", () => {
  it("extracts branch context from default stash subject", () => {
    const parsed = formatStashMessage("On main: update config loader");
    expect(parsed.title).toBe("update config loader");
    expect(parsed.context).toBe("On main");
  });

  it("extracts branch context from WIP stash subject", () => {
    const parsed = formatStashMessage("WIP on feature/x: handle edge cases");
    expect(parsed.title).toBe("handle edge cases");
    expect(parsed.context).toBe("WIP on feature/x");
  });

  it("keeps non-default subjects unchanged", () => {
    const parsed = formatStashMessage("refactor parser");
    expect(parsed.title).toBe("refactor parser");
    expect(parsed.context).toBeNull();
  });
});
