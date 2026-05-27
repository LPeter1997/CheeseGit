import { describe, it, expect, vi } from "vitest";
import { getAppStateSafe, saveAppStateSafe } from "./app-state";

vi.mock("../../ipc/bindings", () => ({
  commands: {
    getAppState: vi.fn(),
    saveAppState: vi.fn(),
  },
}));

import { commands } from "../../ipc/bindings";

const mockGetAppState = vi.mocked(commands.getAppState);
const mockSaveAppState = vi.mocked(commands.saveAppState);

describe("app-state utils", () => {
  it("returns data when getAppState succeeds", async () => {
    mockGetAppState.mockResolvedValue({
      status: "ok",
      data: { open_repos: ["/repo"], active_index: 0 },
    });

    const state = await getAppStateSafe();
    expect(state.open_repos).toEqual(["/repo"]);
  });

  it("returns empty object when getAppState fails", async () => {
    mockGetAppState.mockResolvedValue({
      status: "error",
      error: { Other: "failed" },
    });

    const state = await getAppStateSafe();
    expect(state).toEqual({});
  });

  it("returns true when save succeeds", async () => {
    mockSaveAppState.mockResolvedValue({ status: "ok", data: null });

    const ok = await saveAppStateSafe({ active_index: 1 });
    expect(ok).toBe(true);
  });

  it("returns false when save fails", async () => {
    mockSaveAppState.mockResolvedValue({ status: "error", error: { Other: "failed" } });

    const ok = await saveAppStateSafe({ active_index: 1 });
    expect(ok).toBe(false);
  });
});
