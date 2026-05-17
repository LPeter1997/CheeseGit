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

export type Alert = AlertBase | UpdateAlert;

interface AlertState {
  alerts: Alert[];
  addAlert: (message: string, type?: AlertType) => void;
  addUpdateAlert: (version: string) => void;
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

  removeAlert: (id) => {
    set((s) => ({ alerts: s.alerts.filter((a) => a.id !== id) }));
  },
}));
