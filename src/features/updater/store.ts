import { create } from "zustand";

export type UpdateStatus = "idle" | "checking" | "downloading" | "ready-on-exit";

interface UpdaterState {
  status: UpdateStatus;
  /** The version available for update, if any. */
  availableVersion: string | null;
  /** Download progress 0-100, null if not downloading. */
  downloadProgress: number | null;
  setStatus: (status: UpdateStatus) => void;
  setAvailableVersion: (version: string | null) => void;
  setDownloadProgress: (progress: number | null) => void;
}

export const useUpdaterStore = create<UpdaterState>((set) => ({
  status: "idle",
  availableVersion: null,
  downloadProgress: null,
  setStatus: (status) => set({ status }),
  setAvailableVersion: (version) => set({ availableVersion: version }),
  setDownloadProgress: (progress) => set({ downloadProgress: progress }),
}));
