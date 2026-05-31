import { create } from "zustand";

export type AlertType = "error" | "warning" | "info";

interface AlertBase {
  id: number;
  type: AlertType;
  message: string;
}

export interface UpdateAlert {
  id: number;
  type: "update";
  version: string;
}

export interface DesktopEntryAlert {
  id: number;
  type: "desktop-entry";
  /** Whether this is for a missing entry or a stale (moved) entry. */
  variant: "missing" | "stale";
}

export interface MergeInProgressAlert {
  id: number;
  type: "merge-in-progress";
  /** Whether this is a revert (vs a merge). */
  isRevert: boolean;
  /** The branch being merged or the commit being reverted. */
  incomingBranch: string;
  /** Number of conflicted files. */
  conflictCount: number;
  /** The repo path this alert is for. */
  repoPath: string;
}

export type Alert = AlertBase | UpdateAlert | DesktopEntryAlert | MergeInProgressAlert;

interface AlertState {
  alerts: Alert[];
  addAlert: (message: string, type?: AlertType) => void;
  addUpdateAlert: (version: string) => void;
  addDesktopEntryAlert: (variant: "missing" | "stale") => void;
  addMergeInProgressAlert: (repoPath: string, isRevert: boolean, incomingBranch: string, conflictCount: number) => void;
  removeAlert: (id: number) => void;
}

let nextId = 0;

export const useAlertStore = create<AlertState>((set) => ({
  alerts: [],

  addAlert: (message, type = "error") => {
    const id = nextId++;
    set((s) => ({ alerts: [...s.alerts, { id, message, type }] }));
  },

  addUpdateAlert: (version: string) => {
    // Only allow one update alert at a time.
    set((s) => {
      const filtered = s.alerts.filter((a) => a.type !== "update");
      return { alerts: [...filtered, { id: nextId++, type: "update" as const, version }] };
    });
  },

  addDesktopEntryAlert: (variant: "missing" | "stale") => {
    // Only allow one desktop-entry alert at a time.
    set((s) => {
      const filtered = s.alerts.filter((a) => a.type !== "desktop-entry");
      return { alerts: [...filtered, { id: nextId++, type: "desktop-entry" as const, variant }] };
    });
  },

  addMergeInProgressAlert: (repoPath: string, isRevert: boolean, incomingBranch: string, conflictCount: number) => {
    // Only allow one merge-in-progress alert at a time.
    set((s) => {
      const filtered = s.alerts.filter((a) => a.type !== "merge-in-progress");
      return {
        alerts: [
          ...filtered,
          { id: nextId++, type: "merge-in-progress" as const, isRevert, incomingBranch, conflictCount, repoPath },
        ],
      };
    });
  },

  removeAlert: (id) => {
    set((s) => ({ alerts: s.alerts.filter((a) => a.id !== id) }));
  },
}));
