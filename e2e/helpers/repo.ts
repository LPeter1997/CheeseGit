/**
 * CheeseGit E2E — Test repository management
 *
 * Provides utilities for resetting the test repo to a pristine state,
 * running git commands, and manipulating files in the test repo.
 * Used by spec files to ensure test isolation.
 */

import { spawnSync } from "node:child_process";
import { rmSync, cpSync, mkdirSync, writeFileSync, appendFileSync } from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";

const __dirname = fileURLToPath(new URL(".", import.meta.url));
const e2eDir = path.resolve(__dirname, "..");

/** Absolute path to the active test repository. */
export const TEST_REPO_DIR = path.resolve(e2eDir, ".test-repo");

/** Absolute path to the pristine template repository (created once by onPrepare). */
const TEMPLATE_DIR = path.resolve(e2eDir, ".test-repo-template");

/** Absolute path to the active bare remote clone. */
const REMOTE_DIR = TEST_REPO_DIR + "-remote.git";

/** Absolute path to the pristine template remote clone. */
const TEMPLATE_REMOTE_DIR = TEMPLATE_DIR + "-remote.git";

/**
 * Reset the test repo and its remote to the pristine template state.
 * Call this in each spec's before() hook for test isolation.
 */
export function resetTestRepo(): void {
  rmSync(TEST_REPO_DIR, { recursive: true, force: true });
  rmSync(REMOTE_DIR, { recursive: true, force: true });
  cpSync(TEMPLATE_DIR, TEST_REPO_DIR, { recursive: true });
  cpSync(TEMPLATE_REMOTE_DIR, REMOTE_DIR, { recursive: true });
}

/**
 * Run a git command synchronously in the test repo.
 * Returns trimmed stdout. Throws on non-zero exit.
 */
export function gitSync(...args: string[]): string {
  const result = spawnSync("git", args, {
    cwd: TEST_REPO_DIR,
    encoding: "utf-8",
    env: { ...process.env, GIT_TERMINAL_PROMPT: "0" },
  });
  if (result.status !== 0 && result.status !== null) {
    throw new Error(`git ${args.join(" ")} failed (exit ${result.status}): ${result.stderr}`);
  }
  return (result.stdout ?? "").trim();
}

/** Write a file in the test repo. Creates parent directories as needed. */
export function writeTestFile(relativePath: string, content: string): void {
  const full = path.resolve(TEST_REPO_DIR, relativePath);
  mkdirSync(path.dirname(full), { recursive: true });
  writeFileSync(full, content, "utf-8");
}

/** Append content to a file in the test repo. */
export function appendTestFile(relativePath: string, content: string): void {
  appendFileSync(path.resolve(TEST_REPO_DIR, relativePath), content, "utf-8");
}

/** Stage all changes and commit via git CLI (bypasses the app UI — fast). */
export function commitAllViaGit(message = "setup: commit working tree changes"): void {
  gitSync("add", "-A");
  gitSync("commit", "--no-gpg-sign", "-m", message);
}

/** Remove a file from the test repo. */
export function removeTestFile(relativePath: string): void {
  rmSync(path.resolve(TEST_REPO_DIR, relativePath), { force: true });
}
