import { create } from "zustand";

interface Toast {
  id: number;
  message: string;
  type: "error" | "info";
}

interface ToastState {
  toasts: Toast[];
  addToast: (message: string, type?: "error" | "info") => void;
  removeToast: (id: number) => void;
}

let nextId = 0;

export const useToastStore = create<ToastState>((set) => ({
  toasts: [],

  addToast: (message, type = "error") => {
    const id = nextId++;
    set((s) => ({ toasts: [...s.toasts, { id, message, type }] }));

    // Auto-dismiss after 5 seconds
    setTimeout(() => {
      set((s) => ({ toasts: s.toasts.filter((t) => t.id !== id) }));
    }, 5000);
  },

  removeToast: (id) => {
    set((s) => ({ toasts: s.toasts.filter((t) => t.id !== id) }));
  },
}));
