import { create } from "zustand";
import { commands } from "../../ipc/bindings";

interface DiffState {
  selectedFile: string | null;
  fileContent: string | null;
  loading: boolean;
  selectFile: (repoPath: string, relativePath: string) => Promise<void>;
  clearSelection: () => void;
}

export const useDiffStore = create<DiffState>((set) => ({
  selectedFile: null,
  fileContent: null,
  loading: false,

  selectFile: async (repoPath: string, relativePath: string) => {
    set({ selectedFile: relativePath, fileContent: null, loading: true });
    const result = await commands.readFileContents(repoPath, relativePath);
    if (result.status === "ok") {
      set({ fileContent: result.data, loading: false });
    } else {
      set({ fileContent: null, loading: false });
    }
  },

  clearSelection: () =>
    set({ selectedFile: null, fileContent: null, loading: false }),
}));
