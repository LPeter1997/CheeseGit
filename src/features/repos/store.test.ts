import { describe, it, expect, vi, beforeEach } from "vitest";
import { useReposStore } from "./store";

vi.mock("../../ipc/bindings", () => ({
  commands: {
    openRepository: vi.fn(),
  },
}));

import { commands } from "../../ipc/bindings";
const mockOpenRepository = vi.mocked(commands.openRepository);

function resetStore() {
  useReposStore.setState({ repos: [], activeIndex: -1 });
}

describe("useReposStore", () => {
  beforeEach(() => {
    resetStore();
    vi.clearAllMocks();
  });

  it("starts with empty repos and activeIndex -1", () => {
    const state = useReposStore.getState();
    expect(state.repos).toEqual([]);
    expect(state.activeIndex).toBe(-1);
  });

  it("openRepo adds a repo and sets activeIndex", async () => {
    mockOpenRepository.mockResolvedValue({
      status: "ok",
      data: { name: "my-repo", path: "/home/user/my-repo" },
    });

    const error = await useReposStore.getState().openRepo("/home/user/my-repo");

    expect(error).toBeNull();
    const state = useReposStore.getState();
    expect(state.repos).toHaveLength(1);
    expect(state.repos[0]).toEqual({ name: "my-repo", path: "/home/user/my-repo" });
    expect(state.activeIndex).toBe(0);
  });

  it("openRepo returns error string on failure", async () => {
    mockOpenRepository.mockResolvedValue({
      status: "error",
      error: { Git: "not a git repo" },
    });

    const error = await useReposStore.getState().openRepo("/tmp/bad");

    expect(error).toBe("not a git repo");
    expect(useReposStore.getState().repos).toHaveLength(0);
  });

  it("openRepo deduplicates by path", async () => {
    mockOpenRepository.mockResolvedValue({
      status: "ok",
      data: { name: "repo", path: "/home/user/repo" },
    });

    await useReposStore.getState().openRepo("/home/user/repo");
    await useReposStore.getState().openRepo("/home/user/repo");

    const state = useReposStore.getState();
    expect(state.repos).toHaveLength(1);
    expect(state.activeIndex).toBe(0);
  });

  it("setActiveIndex updates the active tab", () => {
    useReposStore.setState({
      repos: [
        { name: "a", path: "/a" },
        { name: "b", path: "/b" },
      ],
      activeIndex: 0,
    });

    useReposStore.getState().setActiveIndex(1);
    expect(useReposStore.getState().activeIndex).toBe(1);
  });

  it("closeRepo removes a repo and adjusts activeIndex", () => {
    useReposStore.setState({
      repos: [
        { name: "a", path: "/a" },
        { name: "b", path: "/b" },
        { name: "c", path: "/c" },
      ],
      activeIndex: 2,
    });

    // Close middle tab while last is active → active shifts left
    useReposStore.getState().closeRepo(1);
    const state = useReposStore.getState();
    expect(state.repos).toHaveLength(2);
    expect(state.repos.map((r) => r.name)).toEqual(["a", "c"]);
    expect(state.activeIndex).toBe(1);
  });

  it("closeRepo resets activeIndex to -1 when last repo closed", () => {
    useReposStore.setState({
      repos: [{ name: "a", path: "/a" }],
      activeIndex: 0,
    });

    useReposStore.getState().closeRepo(0);
    const state = useReposStore.getState();
    expect(state.repos).toHaveLength(0);
    expect(state.activeIndex).toBe(-1);
  });
});
