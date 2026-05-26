import { useLayoutEffect, type RefObject } from "react";

/**
 * Clamps the scroll position of a scrollable container so the user is never
 * stuck "below" the remaining content after items are removed.
 *
 * Runs synchronously before paint (useLayoutEffect) whenever `itemCount`
 * changes, and also observes container resize for layout-driven changes.
 */
export function useScrollClamp(ref: RefObject<HTMLElement | null>, itemCount: number): void {
  useLayoutEffect(() => {
    const el = ref.current;
    if (!el) return;

    clampScroll(el);

    const ro = new ResizeObserver(() => clampScroll(el));
    ro.observe(el);
    return () => ro.disconnect();
  }, [ref, itemCount]);
}

function clampScroll(el: HTMLElement): void {
  const maxScroll = el.scrollHeight - el.clientHeight;
  if (maxScroll <= 0) {
    el.scrollTop = 0;
  } else if (el.scrollTop > maxScroll) {
    el.scrollTop = maxScroll;
  }
}
