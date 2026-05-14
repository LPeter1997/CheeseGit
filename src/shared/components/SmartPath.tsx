import { useRef, useEffect, useState } from "react";

interface SmartPathProps {
  path: string;
  className?: string;
}

/**
 * Renders a file path that intelligently truncates from the middle when space
 * is limited, always preserving the filename and as much of the leading path
 * as fits. Example: "src/features/…/FileViewer.tsx"
 */
export function SmartPath({ path, className = "" }: SmartPathProps) {
  const containerRef = useRef<HTMLSpanElement>(null);
  const [displayPath, setDisplayPath] = useState(path);

  useEffect(() => {
    const container = containerRef.current;
    if (!container) return;

    // Reset to full path first
    setDisplayPath(path);

    const rafId = requestAnimationFrame(() => {
      if (!containerRef.current) return;
      const shortened = computeShortenedPath(containerRef.current, path);
      setDisplayPath(shortened);
    });

    return () => cancelAnimationFrame(rafId);
  }, [path]);

  useEffect(() => {
    const container = containerRef.current;
    if (!container) return;

    if (typeof ResizeObserver === "undefined") return;

    const observer = new ResizeObserver(() => {
      const shortened = computeShortenedPath(container, path);
      setDisplayPath(shortened);
    });

    observer.observe(container);
    return () => observer.disconnect();
  }, [path]);

  return (
    <span
      ref={containerRef}
      className={`block overflow-hidden whitespace-nowrap ${className}`}
      title={path}
    >
      {displayPath}
    </span>
  );
}

function computeShortenedPath(container: HTMLElement, fullPath: string): string {
  // If container has no width (e.g., in test environment), show full path
  if (container.clientWidth === 0) return fullPath;

  // Normalize backslashes to forward slashes, detect original separator
  const sep = fullPath.includes("\\") ? "\\" : "/";
  const normalized = fullPath.replaceAll("\\", "/");
  const segments = normalized.split("/");

  if (segments.length <= 1) return fullPath;
  if (textFits(container, fullPath)) return fullPath;

  const fileName = segments[segments.length - 1];

  for (let keep = segments.length - 2; keep >= 1; keep--) {
    const prefix = segments.slice(0, keep).join(sep);
    const candidate = `${prefix}${sep}\u2026${sep}${fileName}`;
    if (textFits(container, candidate)) return candidate;
  }

  const minimal = `\u2026${sep}${fileName}`;
  if (textFits(container, minimal)) return minimal;

  return fileName;
}

function textFits(container: HTMLElement, text: string): boolean {
  const measureSpan = document.createElement("span");
  measureSpan.style.visibility = "hidden";
  measureSpan.style.position = "absolute";
  measureSpan.style.whiteSpace = "nowrap";
  measureSpan.style.font = getComputedStyle(container).font;
  measureSpan.textContent = text;
  document.body.appendChild(measureSpan);
  const textWidth = measureSpan.offsetWidth;
  document.body.removeChild(measureSpan);

  return textWidth <= container.clientWidth;
}
