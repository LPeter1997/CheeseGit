import { useEffect, type RefObject } from "react";

/**
 * Calls `onClose` when a mousedown event occurs outside all provided refs.
 * Attaches/detaches the listener based on `active` (defaults to true).
 */
export function useClickOutside(
  refs: RefObject<HTMLElement | null>[],
  onClose: () => void,
  active = true,
) {
  useEffect(() => {
    if (!active) return;
    const handler = (e: MouseEvent) => {
      const target = e.target as Node;
      const inside = refs.some(
        (ref) => ref.current && ref.current.contains(target),
      );
      if (!inside) {
        onClose();
      }
    };
    document.addEventListener("mousedown", handler);
    return () => document.removeEventListener("mousedown", handler);
  }, [refs, onClose, active]);
}
