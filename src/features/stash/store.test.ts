import { describe, it, expect, vi, beforeEach } from "vitest";
import { useStashStore } from "./store";

vi.mock("../../ipc/bindings", () => ({
  commands: {
    listStashes: vi.fn(),
    stashStaged: vi.fn(),
    stashApply: vi.fn(),
    stashPop: vi.fn(),
    stashDrop: vi.fn(),
    listStashFiles: vi.fn(),
    diffStashFile: vi.fn(),
    stashFileStats: vi.fn(),
    showFileAtStash: vi.fn(),
  },
}));

vi.mock("../../shared/stores/alerts", () => ({
  useAlertStore: {
    getState: () => ({ addAlert: vi.fn() }),
  },
}));

import { commands } from "../../ipc/bindings";
const mockListStashes = vi.mocked(commands.listStashes);
const mockStashApply = vi.mocked(commands.stashApply);
const mockStashPop = vi.mocked(commands.stashPop);
const mockStashDrop = vi.mocked(commands.stashDrop);
const mockListStashFiles = vi.mocked(commands.listStashFiles);
const mockStashFileStats = vi.mocked(commands.stashFileStats);
const mockDiffStashFile = vi.mocked(commands.diffStashFile);
const mockShowFileAtStash = vi.mocked(commands.showFileAtStash);

function resetStore() {
  useStashStore.setState({
    stashes: [],
    loading: false,
    initialized: false,
    selectedIndex: null,
    stashFiles: [],
    stashFilesLoading: false,
    stashFileStats: new Map(),
    selectedFilePath: null,
    selectedFileDiff: null,
    selectedFileContent: null,
    selectedFileDiffLoading: false,
  });
}

const REPO = "/tmp/test-repo";

describe("useStashStore", () => {
  beforeEach(() => {
    resetStore();
    vi.clearAllMocks();
  });

  it("starts with empty state", () => {
    const state = useStashStore.getState();
    expect(state.stashes).toEqual([]);
    expect(state.selectedIndex).toBeNull();
    expect(state.initialized).toBe(false);
  });

  it("fetchStashes populates stash list", async () => {
    mockListStashes.mockResolvedValue({
      status: "ok",
      data: [
        {
          index: 0,
          stash_ref: "stash@{0}",
          message: "WIP on main",
          timestamp: "2025-01-01T00:00:00Z",
          author: "Dev",
          hash: "abc123",
          short_hash: "abc",
        },
      ],
    });

    await useStashStore.getState().fetchStashes(REPO);

    const state = useStashStore.getState();
    expect(state.stashes).toHaveLength(1);
    expect(state.stashes[0].message).toBe("WIP on main");
    expect(state.initialized).toBe(true);
    expect(state.loading).toBe(false);
  });

  it("fetchStashes handles error gracefully", async () => {
    mockListStashes.mockResolvedValue({
      status: "error",
      error: { Git: "not a repo" },
    });

    await useStashStore.getState().fetchStashes(REPO);

    const state = useStashStore.getState();
    expect(state.stashes).toEqual([]);
    expect(state.initialized).toBe(true);
  });

  it("selectStash loads files and stats", async () => {
    mockListStashFiles.mockResolvedValue({
      status: "ok",
      data: [{ path: "hello.txt", status: "Modified" }],
    });
    mockStashFileStats.mockResolvedValue({
      status: "ok",
      data: [{ path: "hello.txt", additions: 3, deletions: 1 }],
    });

    useStashStore.setState({ stashes: [{ index: 0, stash_ref: "stash@{0}", message: "test", timestamp: "", author: "Dev", hash: "abc", short_hash: "ab" }] });

    await useStashStore.getState().selectStash(0, REPO);

    const state = useStashStore.getState();
    expect(state.selectedIndex).toBe(0);
    expect(state.stashFiles).toHaveLength(1);
    expect(state.stashFileStats.get("hello.txt")?.additions).toBe(3);
    expect(state.stashFilesLoading).toBe(false);
  });

  it("selectStashFile loads diff and content", async () => {
    const diff = { path: "hello.txt", hunks: [] };
    mockDiffStashFile.mockResolvedValue({ status: "ok", data: diff });
    mockShowFileAtStash.mockResolvedValue({ status: "ok", data: "file content" });

    useStashStore.setState({ selectedIndex: 0 });

    await useStashStore.getState().selectStashFile("hello.txt", REPO);

    const state = useStashStore.getState();
    expect(state.selectedFilePath).toBe("hello.txt");
    expect(state.selectedFileDiff).toEqual(diff);
    expect(state.selectedFileContent).toBe("file content");
  });

  it("applyStash returns true on success", async () => {
    mockStashApply.mockResolvedValue({ status: "ok", data: null });

    const result = await useStashStore.getState().applyStash(REPO, 0);
    expect(result).toBe(true);
  });

  it("applyStash returns false on error", async () => {
    mockStashApply.mockResolvedValue({ status: "error", error: { Git: "fail" } });

    const result = await useStashStore.getState().applyStash(REPO, 0);
    expect(result).toBe(false);
  });

  it("popStash refreshes list on success", async () => {
    mockStashPop.mockResolvedValue({ status: "ok", data: null });
    mockListStashes.mockResolvedValue({ status: "ok", data: [] });

    const result = await useStashStore.getState().popStash(REPO, 0);
    expect(result).toBe(true);
    expect(mockListStashes).toHaveBeenCalled();
  });

  it("dropStash refreshes list on success", async () => {
    mockStashDrop.mockResolvedValue({ status: "ok", data: null });
    mockListStashes.mockResolvedValue({ status: "ok", data: [] });

    const result = await useStashStore.getState().dropStash(REPO, 0);
    expect(result).toBe(true);
    expect(mockListStashes).toHaveBeenCalled();
  });

  it("clearSelection resets selection state", () => {
    useStashStore.setState({
      selectedIndex: 0,
      stashFiles: [{ path: "a.txt", status: "Modified" }],
      selectedFilePath: "a.txt",
    });

    useStashStore.getState().clearSelection();

    const state = useStashStore.getState();
    expect(state.selectedIndex).toBeNull();
    expect(state.stashFiles).toEqual([]);
    expect(state.selectedFilePath).toBeNull();
  });

  it("switchRepo saves and restores state", () => {
    // Set up state for repo A.
    useStashStore.setState({
      stashes: [{ index: 0, stash_ref: "stash@{0}", message: "saved", timestamp: "", author: "Dev", hash: "x", short_hash: "x" }],
      initialized: true,
    });

    // Switch to repo B.
    useStashStore.getState().switchRepo(REPO, "/tmp/repo-b");
    expect(useStashStore.getState().stashes).toEqual([]);
    expect(useStashStore.getState().initialized).toBe(false);

    // Switch back to repo A.
    useStashStore.getState().switchRepo("/tmp/repo-b", REPO);
    expect(useStashStore.getState().stashes).toHaveLength(1);
    expect(useStashStore.getState().initialized).toBe(true);
  });
});
