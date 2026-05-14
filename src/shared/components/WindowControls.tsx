import { getCurrentWindow } from "@tauri-apps/api/window";

const appWindow = getCurrentWindow();

export function WindowControls() {
  return (
    <div className="flex items-center">
      <button
        onClick={() => appWindow.minimize()}
        className="flex h-9 w-11 cursor-pointer items-center justify-center text-fg-muted transition-colors hover:bg-bg-hover hover:text-fg"
        title="Minimize"
      >
        <MinimizeIcon />
      </button>
      <button
        onClick={() => appWindow.toggleMaximize()}
        className="flex h-9 w-11 cursor-pointer items-center justify-center text-fg-muted transition-colors hover:bg-bg-hover hover:text-fg"
        title="Maximize"
      >
        <MaximizeIcon />
      </button>
      <button
        onClick={() => appWindow.close()}
        className="flex h-9 w-11 cursor-pointer items-center justify-center text-fg-muted transition-colors hover:bg-bg-hover hover:text-danger"
        title="Close"
      >
        <CloseIcon />
      </button>
    </div>
  );
}

function MinimizeIcon() {
  return (
    <svg className="h-3 w-3" viewBox="0 0 12 12" fill="none" stroke="currentColor" strokeWidth="1.5">
      <path d="M2 6h8" />
    </svg>
  );
}

function MaximizeIcon() {
  return (
    <svg className="h-3 w-3" viewBox="0 0 12 12" fill="none" stroke="currentColor" strokeWidth="1.5">
      <rect x="1.5" y="1.5" width="9" height="9" rx="1" />
    </svg>
  );
}

function CloseIcon() {
  return (
    <svg className="h-3 w-3" viewBox="0 0 12 12" fill="none" stroke="currentColor" strokeWidth="1.5" strokeLinecap="round">
      <path d="M2 2l8 8M10 2l-8 8" />
    </svg>
  );
}
