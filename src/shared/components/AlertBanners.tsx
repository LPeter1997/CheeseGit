import { useState, useCallback } from "react";
import { useAlertStore, type Alert, type UpdateAlert, type DesktopEntryAlert } from "../stores/alerts";
import { updateNow, updateOnExit, skipUpdate, useUpdaterStore } from "../../features/updater";
import { registerDesktopEntry, dismissDesktopEntry } from "../../features/desktop-entry";

const typeStyles: Record<string, string> = {
  error: "bg-danger/10 border-danger/40 text-danger",
  warning: "bg-warning/10 border-warning/40 text-warning",
  info: "bg-accent/10 border-accent/40 text-accent",
  update: "bg-accent/10 border-accent/40 text-accent",
  "desktop-entry": "bg-accent/10 border-accent/40 text-accent",
};

const typeIcons: Record<string, string> = {
  error: "✕",
  warning: "⚠",
  info: "ℹ",
  update: "⬆",
  "desktop-entry": "🐧",
};

export function AlertBanners() {
  const alerts = useAlertStore((s) => s.alerts);
  const removeAlert = useAlertStore((s) => s.removeAlert);
  const [dismissing, setDismissing] = useState<Set<number>>(new Set());

  const handleDismiss = useCallback((id: number) => {
    setDismissing((prev) => new Set(prev).add(id));
    setTimeout(() => {
      removeAlert(id);
      setDismissing((prev) => {
        const next = new Set(prev);
        next.delete(id);
        return next;
      });
    }, 200);
  }, [removeAlert]);

  if (alerts.length === 0) return null;

  return (
    <div className="flex flex-col overflow-hidden">
      {alerts.map((alert) => {
        const isDismissing = dismissing.has(alert.id);
        return (
          <div
            key={alert.id}
            className={`flex items-center gap-2 border-b px-3 py-1.5 text-sm ${typeStyles[alert.type]}`}
            style={{
              animation: isDismissing
                ? "alert-slide-up 200ms ease-in forwards"
                : "alert-slide-down 200ms ease-out",
            }}
          >
            <span className="text-xs font-bold">{typeIcons[alert.type]}</span>
            {alert.type === "update" ? (
              <UpdateBannerContent alert={alert as UpdateAlert} onDismiss={() => handleDismiss(alert.id)} />
            ) : alert.type === "desktop-entry" ? (
              <DesktopEntryBannerContent alert={alert as DesktopEntryAlert} onDismiss={() => handleDismiss(alert.id)} />
            ) : (
              <DefaultBannerContent alert={alert} onDismiss={() => handleDismiss(alert.id)} />
            )}
          </div>
        );
      })}
    </div>
  );
}

function DefaultBannerContent({ alert, onDismiss }: { alert: Alert; onDismiss: () => void }) {
  const message = "message" in alert ? alert.message : "";
  const [copied, setCopied] = useState(false);

  const handleCopy = useCallback(() => {
    navigator.clipboard.writeText(message);
    setCopied(true);
    setTimeout(() => setCopied(false), 1500);
  }, [message]);

  return (
    <>
      <span className="flex-1 truncate">{message}</span>
      {alert.type === "error" && (
        <span className="flex items-center gap-1">
          {copied && (
            <span
              className="text-xs text-fg"
              style={{ animation: "alert-copied-fade 1.5s ease-out forwards" }}
            >
              Copied!
            </span>
          )}
          <button
            onClick={handleCopy}
            className="cursor-pointer rounded px-1.5 py-0.5 text-xs opacity-70 transition-opacity hover:opacity-100"
            title="Copy to clipboard"
          >
            <CopyIcon />
          </button>
        </span>
      )}
      <button
        onClick={onDismiss}
        className="cursor-pointer rounded px-1 text-base leading-none opacity-70 transition-opacity hover:opacity-100"
        title="Dismiss"
      >
        ×
      </button>
    </>
  );
}

function UpdateBannerContent({ alert, onDismiss }: { alert: UpdateAlert; onDismiss: () => void }) {
  const status = useUpdaterStore((s) => s.status);
  const downloadProgress = useUpdaterStore((s) => s.downloadProgress);
  const isDownloading = status === "downloading";

  return (
    <>
      <span className="flex-1">
        {isDownloading
          ? `Downloading update${downloadProgress !== null ? ` (${downloadProgress}%)` : ""}…`
          : `Version ${alert.version} is available`}
      </span>
      <div className="flex items-center gap-1.5">
        <button
          onClick={updateNow}
          disabled={isDownloading}
          className="cursor-pointer rounded bg-accent px-2 py-0.5 text-xs font-medium text-accent-fg transition-colors hover:bg-accent/80 disabled:opacity-50"
          title="Update now"
        >
          Update now
        </button>
        <button
          onClick={updateOnExit}
          disabled={isDownloading}
          className="cursor-pointer rounded border border-current/30 px-2 py-0.5 text-xs font-medium transition-colors hover:bg-accent/10 disabled:opacity-50"
          title="Update when the application exits"
        >
          On exit
        </button>
        <button
          onClick={skipUpdate}
          disabled={isDownloading}
          className="cursor-pointer rounded border border-current/30 px-2 py-0.5 text-xs font-medium transition-colors hover:bg-accent/10 disabled:opacity-50"
          title="Skip this version"
        >
          Skip
        </button>
        <button
          onClick={onDismiss}
          className="cursor-pointer rounded px-1 text-base leading-none opacity-70 transition-opacity hover:opacity-100"
          title="Remind me later"
        >
          ×
        </button>
      </div>
    </>
  );
}

function DesktopEntryBannerContent({ alert, onDismiss }: { alert: DesktopEntryAlert; onDismiss: () => void }) {
  const message = alert.variant === "stale"
    ? "CheeseGit has moved — update the desktop entry?"
    : "Register CheeseGit as a desktop application?";

  const registerLabel = alert.variant === "stale" ? "Update" : "Register";

  return (
    <>
      <span className="flex-1">{message}</span>
      <div className="flex items-center gap-1.5">
        <button
          onClick={() => { registerDesktopEntry(); onDismiss(); }}
          className="cursor-pointer rounded bg-accent px-2 py-0.5 text-xs font-medium text-accent-fg transition-colors hover:bg-accent/80"
        >
          {registerLabel}
        </button>
        <button
          onClick={() => { dismissDesktopEntry(); onDismiss(); }}
          className="cursor-pointer rounded border border-current/30 px-2 py-0.5 text-xs font-medium transition-colors hover:bg-accent/10"
        >
          Don&apos;t ask again
        </button>
        <button
          onClick={onDismiss}
          className="cursor-pointer rounded px-1 text-base leading-none opacity-70 transition-opacity hover:opacity-100"
          title="Remind me later"
        >
          ×
        </button>
      </div>
    </>
  );
}

function CopyIcon() {
  return (
    <svg className="h-3.5 w-3.5" viewBox="0 0 16 16" fill="currentColor">
      <path d="M0 6.75C0 5.784.784 5 1.75 5h1.5a.75.75 0 0 1 0 1.5h-1.5a.25.25 0 0 0-.25.25v7.5c0 .138.112.25.25.25h7.5a.25.25 0 0 0 .25-.25v-1.5a.75.75 0 0 1 1.5 0v1.5A1.75 1.75 0 0 1 9.25 16h-7.5A1.75 1.75 0 0 1 0 14.25Z" />
      <path d="M5 1.75C5 .784 5.784 0 6.75 0h7.5C15.216 0 16 .784 16 1.75v7.5A1.75 1.75 0 0 1 14.25 11h-7.5A1.75 1.75 0 0 1 5 9.25Zm1.75-.25a.25.25 0 0 0-.25.25v7.5c0 .138.112.25.25.25h7.5a.25.25 0 0 0 .25-.25v-7.5a.25.25 0 0 0-.25-.25Z" />
    </svg>
  );
}
