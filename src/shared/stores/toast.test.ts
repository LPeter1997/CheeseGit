import { describe, it, expect, vi, beforeEach, afterEach } from "vitest";
import { useToastStore } from "./toast";

function resetStore() {
  useToastStore.setState({ toasts: [] });
}

describe("useToastStore", () => {
  beforeEach(() => {
    resetStore();
    vi.useFakeTimers();
  });

  afterEach(() => {
    vi.useRealTimers();
  });

  it("starts with no toasts", () => {
    expect(useToastStore.getState().toasts).toEqual([]);
  });

  it("addToast adds an error toast by default", () => {
    useToastStore.getState().addToast("something went wrong");
    const toasts = useToastStore.getState().toasts;
    expect(toasts).toHaveLength(1);
    expect(toasts[0].message).toBe("something went wrong");
    expect(toasts[0].type).toBe("error");
  });

  it("addToast supports info type", () => {
    useToastStore.getState().addToast("heads up", "info");
    expect(useToastStore.getState().toasts[0].type).toBe("info");
  });

  it("removeToast removes by id", () => {
    useToastStore.getState().addToast("a");
    useToastStore.getState().addToast("b");
    const [first] = useToastStore.getState().toasts;
    useToastStore.getState().removeToast(first.id);
    const remaining = useToastStore.getState().toasts;
    expect(remaining).toHaveLength(1);
    expect(remaining[0].message).toBe("b");
  });

  it("auto-dismisses after 5 seconds", () => {
    useToastStore.getState().addToast("ephemeral");
    expect(useToastStore.getState().toasts).toHaveLength(1);

    vi.advanceTimersByTime(5000);
    expect(useToastStore.getState().toasts).toHaveLength(0);
  });
});
