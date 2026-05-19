import { useState, useCallback, useEffect, useRef } from "react";

interface UseResizeOptions {
  direction: "horizontal" | "vertical";
  initialSize: number;
  minSize: number;
  maxSize?: number;
}

export function useResize({ direction, initialSize, minSize, maxSize }: UseResizeOptions) {
  const [size, setSize] = useState(initialSize);
  const dragging = useRef(false);
  const startPos = useRef(0);
  const startSize = useRef(0);

  const onMouseDown = useCallback((e: React.MouseEvent) => {
    e.preventDefault();
    dragging.current = true;
    startPos.current = direction === "horizontal" ? e.clientX : e.clientY;
    startSize.current = size;
  }, [direction, size]);

  useEffect(() => {
    const onMouseMove = (e: MouseEvent) => {
      if (!dragging.current) return;
      const delta = direction === "horizontal"
        ? e.clientX - startPos.current
        : -(e.clientY - startPos.current);
      let newSize = startSize.current + delta;
      if (newSize < minSize) newSize = minSize;
      if (maxSize && newSize > maxSize) newSize = maxSize;
      setSize(newSize);
    };

    const onMouseUp = () => {
      dragging.current = false;
    };

    document.addEventListener("mousemove", onMouseMove);
    document.addEventListener("mouseup", onMouseUp);
    return () => {
      document.removeEventListener("mousemove", onMouseMove);
      document.removeEventListener("mouseup", onMouseUp);
    };
  }, [direction, minSize, maxSize]);

  return { size, setSize, onMouseDown };
}
