import { useState, useCallback, useRef, useEffect } from "react";
import { commands } from "../../../ipc/bindings";
import { extractErrorMessage } from "../../../shared/utils/errors";

type SshDialogListener = (open: boolean) => void;
let sshDialogListener: SshDialogListener | null = null;

/** Imperatively open the SSH passphrase dialog (e.g. from dev shortcuts). */
export function showSshDialog() {
  sshDialogListener?.(true);
}

interface SshPassphraseDialogProps {
  open: boolean;
  onSuccess: () => void;
  onCancel: () => void;
}

export function SshPassphraseDialog({ open, onSuccess, onCancel }: SshPassphraseDialogProps) {
  const [passphrase, setPassphrase] = useState("");
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const inputRef = useRef<HTMLInputElement>(null);

  useEffect(() => {
    if (open) {
      setPassphrase("");
      setError(null);
      // Focus input after mount
      setTimeout(() => inputRef.current?.focus(), 50);
    }
  }, [open]);

  const handleSubmit = useCallback(async () => {
    if (!passphrase || loading) return;
    setLoading(true);
    setError(null);

    const result = await commands.sshAddKey(passphrase);

    if (result.status === "ok") {
      setLoading(false);
      setPassphrase("");
      onSuccess();
    } else {
      setLoading(false);
      setError(extractErrorMessage(result.error, "Wrong passphrase or key not found"));
    }
  }, [passphrase, loading, onSuccess]);

  const handleKeyDown = useCallback(
    (e: React.KeyboardEvent) => {
      if (e.key === "Enter") {
        handleSubmit();
      } else if (e.key === "Escape") {
        onCancel();
      }
    },
    [handleSubmit, onCancel],
  );

  if (!open) return null;

  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/50">
      <div className="w-[400px] rounded-lg border border-border bg-bg-surface shadow-xl">
        {/* Header */}
        <div className="border-b border-border px-4 py-3">
          <h2 className="text-sm font-semibold text-text-primary">
            SSH Authentication Required
          </h2>
          <p className="mt-0.5 text-xs text-text-secondary">
            Enter your SSH key passphrase to continue
          </p>
        </div>

        {/* Content */}
        <div className="px-4 py-3">
          <label className="flex flex-col gap-1.5">
            <span className="text-xs font-medium text-text-secondary">Passphrase</span>
            <input
              ref={inputRef}
              type="password"
              value={passphrase}
              onChange={(e) => setPassphrase(e.target.value)}
              onKeyDown={handleKeyDown}
              disabled={loading}
              placeholder="SSH key passphrase"
              className="rounded border border-border bg-bg-input px-2 py-1.5 text-sm text-text-primary outline-none focus:border-border-focus disabled:opacity-50"
            />
          </label>
          {error && (
            <p className="mt-2 text-xs text-text-danger">{error}</p>
          )}
          <p className="mt-2 text-xs text-text-secondary">
            The passphrase is cached in app memory for this session.
          </p>
        </div>

        {/* Footer */}
        <div className="flex justify-end gap-2 border-t border-border px-4 py-3">
          <button
            onClick={onCancel}
            disabled={loading}
            className="cursor-pointer rounded px-3 py-1.5 text-xs font-medium text-text-secondary hover:bg-bg-hover disabled:opacity-50"
          >
            Cancel
          </button>
          <button
            onClick={handleSubmit}
            disabled={loading || !passphrase}
            className="cursor-pointer rounded bg-accent px-3 py-1.5 text-xs font-medium text-white hover:bg-accent-hover disabled:opacity-50"
          >
            {loading ? "Unlocking…" : "Unlock"}
          </button>
        </div>
      </div>
    </div>
  );
}

/** Standalone SSH dialog that can be opened via `showSshDialog()`. */
export function GlobalSshDialog() {
  const [open, setOpen] = useState(false);

  useEffect(() => {
    sshDialogListener = setOpen;
    return () => { sshDialogListener = null; };
  }, []);

  if (!open) return null;

  return (
    <SshPassphraseDialog
      open={open}
      onSuccess={() => setOpen(false)}
      onCancel={() => setOpen(false)}
    />
  );
}
