import { useEffect, useRef, useState } from "react";
import Markdown from "react-markdown";
import { getAppStateSafe, saveAppStateSafe } from "../../shared/utils/app-state";

interface PendingChangelog {
  version: string;
  body: string;
}

type ChangelogListener = (changelog: PendingChangelog) => void;
let listener: ChangelogListener | null = null;

/** Imperatively show the "What's new" dialog. */
export function showWhatsNew(version: string, body: string) {
  listener?.({ version, body });
}

export function WhatsNewDialog() {
  const [changelog, setChangelog] = useState<PendingChangelog | null>(null);

  useEffect(() => {
    listener = setChangelog;
    return () => { listener = null; };
  }, []);

  useEffect(() => {
    getAppStateSafe().then((state) => {
      if (state.pending_changelog && state.pending_changelog_version) {
        setChangelog({
          version: state.pending_changelog_version,
          body: state.pending_changelog,
        });
        // Clear the pending changelog so it only shows once.
        saveAppStateSafe({
          ...state,
          pending_changelog: null,
          pending_changelog_version: null,
        });
      }
    });
  }, []);

  if (!changelog) return null;

  return <WhatsNewContent changelog={changelog} onClose={() => setChangelog(null)} />;
}

function WhatsNewContent({
  changelog,
  onClose,
}: {
  changelog: PendingChangelog;
  onClose: () => void;
}) {
  const backdropRef = useRef<HTMLDivElement>(null);

  useEffect(() => {
    const handler = (e: KeyboardEvent) => {
      if (e.key === "Escape") onClose();
    };
    document.addEventListener("keydown", handler);
    return () => document.removeEventListener("keydown", handler);
  }, [onClose]);

  return (
    <div
      ref={backdropRef}
      onClick={(e) => {
        if (e.target === backdropRef.current) onClose();
      }}
      className="fixed inset-0 z-50 flex items-center justify-center bg-black/40"
    >
      <div className="flex max-h-[70vh] w-[420px] flex-col rounded-lg border border-border bg-bg-surface shadow-xl">
        <div className="flex items-center justify-between border-b border-border px-5 py-3">
          <h2 className="text-sm font-semibold text-fg">
            What&apos;s new in {changelog.version}
          </h2>
          <button
            onClick={onClose}
            className="cursor-pointer rounded px-1 text-base leading-none text-fg-muted transition-opacity hover:text-fg"
            title="Close"
          >
            ×
          </button>
        </div>
        <div className="flex-1 overflow-y-auto px-5 py-4">
          <div className="prose-changelog text-xs leading-relaxed text-fg-muted">
            <Markdown>{changelog.body}</Markdown>
          </div>
        </div>
        <div className="flex justify-end border-t border-border px-5 py-3">
          <button
            onClick={onClose}
            className="cursor-pointer rounded bg-accent px-4 py-1 text-xs font-medium text-accent-fg transition-colors hover:bg-accent/80"
          >
            Got it
          </button>
        </div>
      </div>
    </div>
  );
}
