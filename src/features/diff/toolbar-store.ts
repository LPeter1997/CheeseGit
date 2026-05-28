import { create } from "zustand";
import type { DiffViewMode } from "./store";

export type DiffToolbarContext = "staging" | "history" | "stash";

type ContextMap<T> = Record<DiffToolbarContext, T>;

interface MatchStatus {
  currentIndex: number;
  totalMatches: number;
  isSearching: boolean;
}

interface DiffToolbarState {
  query: ContextMap<string>;
  focusSignal: ContextMap<number>;
  nextSignal: ContextMap<number>;
  previousSignal: ContextMap<number>;
  matchStatus: ContextMap<MatchStatus>;
  viewMode: ContextMap<DiffViewMode>;
  setQuery: (context: DiffToolbarContext, query: string) => void;
  requestFocus: (context: DiffToolbarContext) => void;
  requestNext: (context: DiffToolbarContext) => void;
  requestPrevious: (context: DiffToolbarContext) => void;
  setMatchStatus: (context: DiffToolbarContext, status: MatchStatus) => void;
  setViewMode: (context: DiffToolbarContext, mode: DiffViewMode) => void;
}

function isToolbarContext(context: string): context is DiffToolbarContext {
  return context === "staging" || context === "history" || context === "stash";
}

function updateContext<T>(current: ContextMap<T>, context: DiffToolbarContext, value: T): ContextMap<T> {
  if (current[context] === value) return current;
  return { ...current, [context]: value };
}

export const useDiffToolbarStore = create<DiffToolbarState>((set) => ({
  query: {
    staging: "",
    history: "",
    stash: "",
  },
  focusSignal: {
    staging: 0,
    history: 0,
    stash: 0,
  },
  nextSignal: {
    staging: 0,
    history: 0,
    stash: 0,
  },
  previousSignal: {
    staging: 0,
    history: 0,
    stash: 0,
  },
  matchStatus: {
    staging: { currentIndex: 0, totalMatches: 0, isSearching: false },
    history: { currentIndex: 0, totalMatches: 0, isSearching: false },
    stash: { currentIndex: 0, totalMatches: 0, isSearching: false },
  },
  viewMode: {
    staging: "unified",
    history: "unified",
    stash: "unified",
  },

  setQuery: (context, query) => {
    set((state) => ({ query: updateContext(state.query, context, query) }));
  },

  requestFocus: (context) => {
    set((state) => ({
      focusSignal: {
        ...state.focusSignal,
        [context]: state.focusSignal[context] + 1,
      },
    }));
  },

  requestNext: (context) => {
    set((state) => ({
      nextSignal: {
        ...state.nextSignal,
        [context]: state.nextSignal[context] + 1,
      },
    }));
  },

  requestPrevious: (context) => {
    set((state) => ({
      previousSignal: {
        ...state.previousSignal,
        [context]: state.previousSignal[context] + 1,
      },
    }));
  },

  setMatchStatus: (context, status) => {
    set((state) => {
      if (!isToolbarContext(context)) return state;
      const current = state.matchStatus[context];
      if (
        current.currentIndex === status.currentIndex &&
        current.totalMatches === status.totalMatches &&
        current.isSearching === status.isSearching
      ) {
        return state;
      }
      return {
        matchStatus: {
          ...state.matchStatus,
          [context]: status,
        },
      };
    });
  },

  setViewMode: (context, mode) => {
    set((state) => ({ viewMode: updateContext(state.viewMode, context, mode) }));
  },
}));
