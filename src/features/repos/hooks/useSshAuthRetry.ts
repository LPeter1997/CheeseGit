import { useCallback, useState } from "react";
import { type AppError } from "../../../ipc/bindings";
import { useAlertStore } from "../../../shared/stores/alerts";

/**
 * Checks if an error is an SSH authentication error.
 */
export function isSshAuthError(error: AppError): boolean {
  return "SshAuthRequired" in error;
}

/**
 * Hook for executing git commands that might require SSH authentication.
 * Automatically shows the SSH dialog and retries the operation if needed.
 *
 * Usage:
 *   const { executeWithSshRetry, showSshDialog } = useSshAuthRetry();
 *   const result = await executeWithSshRetry(() => commands.push(repoPath, remote));
 *
 * If SSH auth is required, it will:
 * 1. Show the SSH passphrase dialog
 * 2. On success, automatically retry the original operation
 * 3. On failure/cancel, abort and optionally alert the user
 */
export function useSshAuthRetry() {
  const [sshDialogOpen, setSshDialogOpen] = useState(false);
  const [pendingRetry, setPendingRetry] = useState<(() => Promise<any>) | null>(null);
  const addAlert = useAlertStore((s) => s.addAlert);

  const executeWithSshRetry = useCallback(
    async <T,>(
      operation: () => Promise<{ status: "ok"; data: T } | { status: "error"; error: AppError }>,
      options?: {
        operationName?: string;
        alertOnError?: boolean;
      },
    ): Promise<{ status: "ok"; data: T } | { status: "error"; error: AppError } | null> => {
      const result = await operation();

      if (result.status === "error" && isSshAuthError(result.error)) {
        // Set up retry and show SSH dialog
        setPendingRetry(() => operation);
        setSshDialogOpen(true);
        return null; // Operation pending
      }

      if (result.status === "error" && options?.alertOnError) {
        addAlert(
          result.error.Git ??
            result.error.Io ??
            result.error.SshAuthRequired ??
            result.error.Other ??
            `Failed to ${options.operationName || "execute operation"}`,
        );
      }

      return result;
    },
    [addAlert],
  );

  const handleSshSuccess = useCallback(async () => {
    setSshDialogOpen(false);
    if (pendingRetry) {
      const operation = pendingRetry;
      setPendingRetry(null);
      // Retry after auth
      await operation();
    }
  }, [pendingRetry]);

  const handleSshCancel = useCallback(() => {
    setSshDialogOpen(false);
    setPendingRetry(null);
  }, []);

  return {
    sshDialogOpen,
    setSshDialogOpen,
    executeWithSshRetry,
    handleSshSuccess,
    handleSshCancel,
  };
}
