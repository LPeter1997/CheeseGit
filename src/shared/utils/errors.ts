import type { AppError } from "../../ipc/bindings";

export function extractErrorMessage(error: AppError, fallback = "Unknown error"): string {
  return error.Git ?? error.Io ?? error.SshAuthRequired ?? error.Other ?? fallback;
}
