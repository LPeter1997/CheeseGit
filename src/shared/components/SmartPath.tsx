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

  if (textFits(container, fullPath)) return fullPath;

  const fileName = segments[segments.length - 1];

  if (segments.length > 1) {
    for (let keep = segments.length - 2; keep >= 1; keep--) {
      const prefix = segments.slice(0, keep).join(sep);
      const candidate = `${prefix}${sep}\u2026${sep}${fileName}`;
      if (textFits(container, candidate)) return candidate;
    }

    const minimal = `\u2026${sep}${fileName}`;
    if (textFits(container, minimal)) return minimal;
  }

  const fittedName = shortenFilenamePreferExtension(fileName, estimateCharacterCapacity(container));
  if (fittedName.length > 0) return fittedName;

  return fileName;
}

export function shortenFilenamePreferExtension(fileName: string, maxChars: number): string {
  if (maxChars <= 0) return "";
  if (fileName.length <= maxChars) return fileName;

  const extStart = fileName.lastIndexOf(".");
  const hasExtension = extStart > 0 && extStart < fileName.length - 1;

  if (hasExtension) {
    const extension = fileName.slice(extStart);
    const reserved = 1 + extension.length; // ellipsis + extension
    const stemBudget = maxChars - reserved;
    if (stemBudget > 0) {
      return `${fileName.slice(0, stemBudget)}\u2026${extension}`;
    }
  }

  if (maxChars === 1) return "\u2026";
  return `${fileName.slice(0, maxChars - 1)}\u2026`;
}

function estimateCharacterCapacity(container: HTMLElement): number {
  const style = getComputedStyle(container);
  const font = style.font;
  const sample = document.createElement("span");
  sample.style.visibility = "hidden";
  sample.style.position = "absolute";
  sample.style.whiteSpace = "nowrap";
  sample.style.font = font;
  sample.textContent = "MMMMMMMMMM";
  document.body.appendChild(sample);
  const avgCharWidth = Math.max(1, sample.offsetWidth / 10);
  document.body.removeChild(sample);
  return Math.max(1, Math.floor(container.clientWidth / avgCharWidth));
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
