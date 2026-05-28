import { useState, useRef, useCallback, useEffect } from "react";
import { getVersion } from "@tauri-apps/api/app";
import { useThemeStore, type ThemeChoice } from "../../theme/store";
import { checkForUpdatesNow } from "../../updater";
import { getDesktopEntryStatusSafe, registerDesktopEntry } from "../../desktop-entry";
import { useClickOutside } from "../../../shared/hooks/useClickOutside";

const themes: { value: ThemeChoice; label: string }[] = [
  { value: "system", label: "System" },
  { value: "light", label: "Light" },
  { value: "dark", label: "Dark" },
  { value: "high-contrast", label: "High Contrast" },
];

interface OptionsMenuProps {
  className?: string;
}

export function OptionsMenu({ className }: OptionsMenuProps) {
  const [open, setOpen] = useState(false);
  const [themeSubmenuOpen, setThemeSubmenuOpen] = useState(false);
  const [aboutOpen, setAboutOpen] = useState(false);
  const [checkingUpdates, setCheckingUpdates] = useState(false);
  const [desktopEntryActionLabel, setDesktopEntryActionLabel] = useState<string | null>(null);
  const [desktopEntryBusy, setDesktopEntryBusy] = useState(false);
  const menuRef = useRef<HTMLDivElement>(null);
  const buttonRef = useRef<HTMLButtonElement>(null);
  const currentTheme = useThemeStore((s) => s.theme);
  const setTheme = useThemeStore((s) => s.setTheme);
  const previewTheme = useThemeStore((s) => s.previewTheme);

  const closeMenu = useCallback(() => {
    setOpen(false);
    setThemeSubmenuOpen(false);
    previewTheme(null);
  }, [previewTheme]);

  useClickOutside([menuRef, buttonRef], closeMenu, open);

  useEffect(() => {
    if (!open) return;
    let cancelled = false;

    void (async () => {
      const status = await getDesktopEntryStatusSafe();
      if (cancelled) return;

      if (status === "Missing") {
        setDesktopEntryActionLabel("Register Desktop Entry");
      } else if (status === "Stale") {
        setDesktopEntryActionLabel("Update Desktop Entry");
      } else {
        setDesktopEntryActionLabel(null);
      }
    })();

    return () => {
      cancelled = true;
    };
  }, [open]);

  async function handleCheckForUpdates() {
    if (checkingUpdates) return;
    setCheckingUpdates(true);
    await checkForUpdatesNow();
    setCheckingUpdates(false);
    setOpen(false);
    setThemeSubmenuOpen(false);
  }

  async function handleRegisterDesktopEntry() {
    if (desktopEntryBusy) return;
    setDesktopEntryBusy(true);
    const ok = await registerDesktopEntry({ showSuccessAlert: true });
    setDesktopEntryBusy(false);
    if (ok) {
      setDesktopEntryActionLabel(null);
      setOpen(false);
      setThemeSubmenuOpen(false);
    }
  }

  return (
    <div className={`relative ${className ?? "ml-auto"}`}>
      <button
        ref={buttonRef}
        onClick={() => {
          setOpen(!open);
          if (open) {
            setThemeSubmenuOpen(false);
            previewTheme(null);
          }
        }}
        className="flex h-9 w-11 cursor-pointer items-center justify-center text-fg-muted transition-colors hover:bg-bg-hover hover:text-fg"
        data-testid="options-menu-button"
        title="Options"
      >
        <GearIcon />
      </button>

      {open && (
        <div
          ref={menuRef}
          data-testid="options-menu"
          className="absolute right-0 top-full z-50 mt-1 w-48 rounded-md border border-border bg-bg-surface shadow-lg py-1"
        >
          <div
            className="relative"
            onMouseEnter={() => setThemeSubmenuOpen(true)}
            onMouseLeave={() => {
              setThemeSubmenuOpen(false);
              previewTheme(null);
            }}
          >
            <button className="flex w-full items-center justify-between px-3 py-1.5 text-sm text-fg transition-colors hover:bg-bg-hover cursor-pointer">
              <span>Theme</span>
              <ArrowRightIcon />
            </button>

            {themeSubmenuOpen && (
              <div className="absolute right-full top-0 mr-1 w-44 rounded-md border border-border bg-bg-surface shadow-lg py-1">
                {themes.map(({ value, label }) => (
                  <button
                    key={value}
                    onClick={() => {
                      setTheme(value);
                      setOpen(false);
                      setThemeSubmenuOpen(false);
                    }}
                    onMouseEnter={() => previewTheme(value)}
                    onMouseLeave={() => previewTheme(null)}
                    className={`flex w-full items-center gap-2 px-3 py-1.5 text-sm transition-colors hover:bg-bg-hover cursor-pointer ${
                      currentTheme === value ? "text-accent" : "text-fg"
                    }`}
                  >
                    {currentTheme === value && <span className="text-accent">✓</span>}
                    <span className={currentTheme === value ? "" : "ml-5"}>{label}</span>
                  </button>
                ))}
              </div>
            )}
          </div>

          <div className="mx-2 my-1 border-t border-border" />

          <button
            onClick={handleCheckForUpdates}
            disabled={checkingUpdates}
            className="flex w-full items-center px-3 py-1.5 text-sm text-fg transition-colors hover:bg-bg-hover disabled:opacity-50 disabled:cursor-default cursor-pointer"
          >
            {checkingUpdates ? "Checking for updates..." : "Check for Updates"}
          </button>

          {desktopEntryActionLabel && (
            <button
              onClick={handleRegisterDesktopEntry}
              disabled={desktopEntryBusy}
              className="flex w-full items-center px-3 py-1.5 text-sm text-fg transition-colors hover:bg-bg-hover disabled:opacity-50 disabled:cursor-default cursor-pointer"
            >
              {desktopEntryBusy ? "Working..." : desktopEntryActionLabel}
            </button>
          )}

          <div className="mx-2 my-1 border-t border-border" />

          <button
            onClick={() => {
              setAboutOpen(true);
              setOpen(false);
              setThemeSubmenuOpen(false);
            }}
            className="flex w-full items-center px-3 py-1.5 text-sm text-fg transition-colors hover:bg-bg-hover cursor-pointer"
          >
            About
          </button>
        </div>
      )}

      {aboutOpen && <AboutDialog onClose={() => setAboutOpen(false)} />}
    </div>
  );
}

function AboutDialog({ onClose }: { onClose: () => void }) {
  const backdropRef = useRef<HTMLDivElement>(null);
  const [version, setVersion] = useState<string | null>(null);

  useEffect(() => {
    const handler = (e: KeyboardEvent) => {
      if (e.key === "Escape") onClose();
    };
    document.addEventListener("keydown", handler);
    return () => document.removeEventListener("keydown", handler);
  }, [onClose]);

  useEffect(() => {
    if (import.meta.env.DEV) {
      setVersion("Development");
    } else {
      getVersion().then(setVersion);
    }
  }, []);

  return (
    <div
      ref={backdropRef}
      onClick={(e) => { if (e.target === backdropRef.current) onClose(); }}
      className="fixed inset-0 z-50 flex items-center justify-center bg-black/40"
    >
      <div className="flex flex-col items-center gap-3 rounded-lg border border-border bg-bg-surface px-8 py-6 shadow-xl">
        <img src="/icon.png" alt="CheeseGit" className="h-16 w-16 rounded-lg" />
        <h2 className="text-lg font-semibold text-fg">CheeseGit</h2>
        <span className="text-xs text-fg-muted">{version ? (version === "Development" ? "Development version" : `Version ${version}`) : ""}</span>
        <button
          onClick={onClose}
          className="mt-2 cursor-pointer rounded bg-accent px-4 py-1 text-sm font-medium text-accent-fg transition-colors hover:bg-accent/80"
        >
          Close
        </button>
      </div>
    </div>
  );
}

function GearIcon() {
  return (
    <svg className="h-4 w-4" viewBox="0 0 16 16" fill="currentColor">
      <path fillRule="evenodd" d="M7.429 1.525a6.593 6.593 0 0 1 1.142 0c.036.003.108.036.137.146l.289 1.105c.147.56.55.967.997 1.189.174.086.341.183.501.29.417.278.97.423 1.53.27l1.102-.303c.11-.03.175.016.195.046.219.31.41.641.573.989.014.031.022.11-.059.19l-.815.806c-.411.406-.562.957-.53 1.456a4.588 4.588 0 0 1 0 .582c-.032.499.119 1.05.53 1.456l.815.806c.08.08.073.159.059.19a6.494 6.494 0 0 1-.573.99c-.02.029-.086.074-.195.045l-1.103-.303c-.559-.153-1.112-.008-1.529.27-.16.107-.327.204-.5.29-.449.222-.851.628-.998 1.189l-.289 1.105c-.029.11-.101.143-.137.146a6.613 6.613 0 0 1-1.142 0c-.036-.003-.108-.037-.137-.146l-.289-1.105c-.147-.56-.55-.967-.997-1.189a4.502 4.502 0 0 1-.501-.29c-.417-.278-.97-.423-1.53-.27l-1.102.303c-.11.03-.175-.016-.195-.046a6.492 6.492 0 0 1-.573-.989c-.014-.031-.022-.11.059-.19l.815-.806c.411-.406.562-.957.53-1.456a4.587 4.587 0 0 1 0-.582c.032-.499-.119-1.05-.53-1.456l-.815-.806c-.08-.08-.073-.159-.059-.19a6.44 6.44 0 0 1 .573-.99c.02-.029.086-.074.195-.045l1.103.303c.559.153 1.112.008 1.529-.27.16-.107.327-.204.5-.29.449-.222.851-.628.998-1.189l.289-1.105c.029-.11.101-.143.137-.146zM8 0c-.236 0-.47.01-.701.03-.743.065-1.29.615-1.458 1.261l-.29 1.106c-.017.066-.078.158-.211.227a5.971 5.971 0 0 0-.668.386c-.123.082-.233.117-.3.1L3.27 2.808c-.662-.182-1.376.016-1.82.63-.19.263-.362.54-.517.829-.408.76-.272 1.527.198 2.093l.817.808c.047.047.111.142.103.27a6.09 6.09 0 0 0 0 .774c.008.128-.056.223-.103.27l-.817.808c-.47.566-.606 1.333-.198 2.093.155.29.327.566.517.83.444.613 1.158.811 1.82.629l1.103-.303c.066-.018.177.018.3.1.216.144.44.275.668.386.133.069.194.161.211.227l.29 1.106c.167.646.714 1.196 1.457 1.26.46.04.925.04 1.385 0 .744-.064 1.29-.614 1.458-1.26l.289-1.106c.018-.066.079-.158.212-.227.228-.111.452-.242.668-.386.123-.082.233-.117.3-.1l1.102.302c.662.183 1.376-.015 1.82-.628.19-.264.362-.541.517-.83.408-.76.272-1.527-.198-2.093l-.817-.808c-.047-.047-.111-.142-.103-.27a6.09 6.09 0 0 0 0-.774c-.008-.128.056-.223.103-.27l.817-.808c.47-.566.606-1.333.198-2.093a6.44 6.44 0 0 0-.517-.83c-.444-.613-1.158-.811-1.82-.629l-1.103.303c-.066.018-.177-.018-.3-.1a5.971 5.971 0 0 0-.668-.386c-.133-.069-.194-.161-.212-.227l-.288-1.106c-.168-.646-.715-1.196-1.458-1.26A8.094 8.094 0 0 0 8 0zM8 11a3 3 0 1 0 0-6 3 3 0 0 0 0 6zm0-1.5a1.5 1.5 0 1 0 0-3 1.5 1.5 0 0 0 0 3z" />
    </svg>
  );
}

function ArrowRightIcon() {
  return (
    <svg className="h-3 w-3 text-fg-muted" viewBox="0 0 12 12" fill="none" stroke="currentColor" strokeWidth="1.5" strokeLinecap="round" strokeLinejoin="round">
      <path d="M4.5 2.5L8 6l-3.5 3.5" />
    </svg>
  );
}
