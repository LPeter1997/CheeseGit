import { getCurrentWindow } from "@tauri-apps/api/window";

const appWindow = getCurrentWindow();

const EDGE_SIZE = 6;

type Direction =
  | "North"
  | "South"
  | "East"
  | "West"
  | "NorthEast"
  | "NorthWest"
  | "SouthEast"
  | "SouthWest";

/**
 * Invisible resize edges for undecorated windows.
 * Renders absolutely-positioned hit areas along all four edges + corners
 * that trigger Tauri's native window resize on mousedown.
 */
export function WindowResizeEdges() {
  function onResize(direction: Direction) {
    return (e: React.MouseEvent) => {
      e.preventDefault();
      appWindow.startResizeDragging(direction);
    };
  }

  return (
    <>
      {/* Top edge */}
      <div
        className="fixed top-0 left-0 right-0 z-[9999]"
        style={{ height: EDGE_SIZE, cursor: "n-resize" }}
        onMouseDown={onResize("North")}
      />
      {/* Bottom edge */}
      <div
        className="fixed bottom-0 left-0 right-0 z-[9999]"
        style={{ height: EDGE_SIZE, cursor: "s-resize" }}
        onMouseDown={onResize("South")}
      />
      {/* Left edge */}
      <div
        className="fixed top-0 bottom-0 left-0 z-[9999]"
        style={{ width: EDGE_SIZE, cursor: "w-resize" }}
        onMouseDown={onResize("West")}
      />
      {/* Right edge */}
      <div
        className="fixed top-0 bottom-0 right-0 z-[9999]"
        style={{ width: EDGE_SIZE, cursor: "e-resize" }}
        onMouseDown={onResize("East")}
      />
      {/* Top-left corner */}
      <div
        className="fixed top-0 left-0 z-[10000]"
        style={{ width: EDGE_SIZE, height: EDGE_SIZE, cursor: "nw-resize" }}
        onMouseDown={onResize("NorthWest")}
      />
      {/* Top-right corner */}
      <div
        className="fixed top-0 right-0 z-[10000]"
        style={{ width: EDGE_SIZE, height: EDGE_SIZE, cursor: "ne-resize" }}
        onMouseDown={onResize("NorthEast")}
      />
      {/* Bottom-left corner */}
      <div
        className="fixed bottom-0 left-0 z-[10000]"
        style={{ width: EDGE_SIZE, height: EDGE_SIZE, cursor: "sw-resize" }}
        onMouseDown={onResize("SouthWest")}
      />
      {/* Bottom-right corner */}
      <div
        className="fixed bottom-0 right-0 z-[10000]"
        style={{ width: EDGE_SIZE, height: EDGE_SIZE, cursor: "se-resize" }}
        onMouseDown={onResize("SouthEast")}
      />
    </>
  );
}
