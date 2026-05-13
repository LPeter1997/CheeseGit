import { create } from "zustand";
import { commands, type DiffArea, type FileDiff } from "../../ipc/bindings";

export type DiffViewMode = "unified" | "split";

interface DiffState {
  selectedFile: string | null;
  selectedArea: DiffArea | null;
  fileContent: string | null;
  fileDiff: FileDiff | null;
  viewMode: DiffViewMode;
  loading: boolean;
  selectFile: (
    repoPath: string,
    relativePath: string,
    area: DiffArea,
  ) => Promise<void>;
  setViewMode: (mode: DiffViewMode) => void;
  clearSelection: () => void;
}

export const useDiffStore = create<DiffState>((set) => ({
  selectedFile: null,
  selectedArea: null,
  fileContent: null,
  fileDiff: null,
  viewMode: "unified",
  loading: false,

  selectFile: async (
    repoPath: string,
    relativePath: string,
    area: DiffArea,
  ) => {
    set({
      selectedFile: relativePath,
      selectedArea: area,
      fileContent: null,
      fileDiff: null,
      loading: true,
    });

    const [contentResult, diffResult] = await Promise.all([
      commands.readFileContents(repoPath, relativePath),
      commands.getFileDiff(repoPath, relativePath, area),
    ]);

    set({
      fileContent:
        contentResult.status === "ok" ? contentResult.data : null,
      fileDiff: diffResult.status === "ok" ? diffResult.data : null,
      loading: false,
    });
  },

  setViewMode: (mode: DiffViewMode) => set({ viewMode: mode }),

  clearSelection: () =>
    set({
      selectedFile: null,
      selectedArea: null,
      fileContent: null,
      fileDiff: null,
      loading: false,
    }),
}));
