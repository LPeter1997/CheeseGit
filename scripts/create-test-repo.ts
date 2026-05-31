/**
 * create-test-repo.ts
 *
 * Creates a reproducible "interesting" git repository for e2e testing CheeseGit.
 * The repo includes multiple branches, merge commits, conflicts, file renames,
 * deletions, and a realistic commit history.
 *
 * Usage:
 *   node --experimental-strip-types scripts/create-test-repo.ts <target-folder>
 *
 * The target folder must not exist (to avoid accidental overwrites).
 */

import { execFileSync } from "node:child_process";
import { mkdirSync, writeFileSync, rmSync, existsSync, appendFileSync } from "node:fs";
import { resolve, basename } from "node:path";

// ---------------------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------------------

/** Run a git command inside the repo. Returns trimmed stdout. */
function git(repoPath: string, ...args: string[]): string {
  return execFileSync("git", args, {
    cwd: repoPath,
    encoding: "utf-8",
    env: {
      ...process.env,
      // Ensure reproducible timestamps and no interactive prompts.
      GIT_AUTHOR_DATE: currentDate,
      GIT_COMMITTER_DATE: currentDate,
      GIT_TERMINAL_PROMPT: "0",
    },
  }).trim();
}

/** Advance the fake clock by `minutes` so commits have distinct timestamps. */
let clock = new Date("2025-03-15T10:00:00Z");
let currentDate = clock.toISOString();

function tick(minutes: number = 5): void {
  clock = new Date(clock.getTime() + minutes * 60_000);
  currentDate = clock.toISOString();
}

/** Write a file relative to the repo root. Creates parent dirs as needed. */
function writeFile(repoPath: string, relativePath: string, content: string): void {
  const full = resolve(repoPath, relativePath);
  const dir = resolve(full, "..");
  mkdirSync(dir, { recursive: true });
  writeFileSync(full, content, "utf-8");
}

/** Append content to a file relative to the repo root. */
function appendFile(repoPath: string, relativePath: string, content: string): void {
  appendFileSync(resolve(repoPath, relativePath), content, "utf-8");
}

/** Remove a file relative to the repo root. */
function removeFile(repoPath: string, relativePath: string): void {
  rmSync(resolve(repoPath, relativePath), { force: true });
}

/** Stage all changes and commit with the given message. */
function commit(repoPath: string, message: string): string {
  tick();
  git(repoPath, "add", "-A");
  // Force deterministic non-interactive commits even if global signing is enabled.
  git(repoPath, "commit", "--no-gpg-sign", "-m", message);
  return git(repoPath, "rev-parse", "HEAD");
}

/** Create and switch to a new branch. */
function createBranch(repoPath: string, name: string): void {
  git(repoPath, "checkout", "-b", name);
}

/** Switch to an existing branch. */
function checkout(repoPath: string, name: string): void {
  git(repoPath, "checkout", name);
}

/** Merge a branch with a merge commit (no fast-forward). */
function merge(repoPath: string, branch: string, message?: string): void {
  tick();
  const msg = message ?? `Merge branch '${branch}'`;
  git(repoPath, "merge", "--no-ff", "--no-edit", "-m", msg, branch);
}

// ---------------------------------------------------------------------------
// Repository construction
// ---------------------------------------------------------------------------

function buildRepo(repoPath: string): void {
  // ── Init ──────────────────────────────────────────────────────────────
  git(repoPath, "init");
  git(repoPath, "config", "user.email", "dev@cheesegit.test");
  git(repoPath, "config", "user.name", "CheeseGit Dev");
  // Neutralize machine-level Git config that may trigger interactive prompts.
  git(repoPath, "config", "commit.gpgsign", "false");
  git(repoPath, "config", "tag.gpgSign", "false");
  git(repoPath, "config", "core.hooksPath", "/dev/null");
  // Use "main" as the default branch for consistency across git versions.
  git(repoPath, "checkout", "-b", "main");

  // ── Initial project structure ─────────────────────────────────────────
  writeFile(repoPath, "README.md", "# Test Project\n\nA sample project for testing CheeseGit.\n");
  writeFile(repoPath, ".gitignore", "node_modules/\ndist/\n*.log\n.DS_Store\n");
  writeFile(
    repoPath,
    "src/main.ts",
    [
      'export function greet(name: string): string {',
      '  return `Hello, ${name}!`;',
      '}',
      '',
      'console.log(greet("World"));',
      '',
    ].join("\n"),
  );
  writeFile(
    repoPath,
    "src/utils.ts",
    [
      'export function clamp(value: number, min: number, max: number): number {',
      '  return Math.min(Math.max(value, min), max);',
      '}',
      '',
      'export function capitalize(s: string): string {',
      '  return s.charAt(0).toUpperCase() + s.slice(1);',
      '}',
      '',
    ].join("\n"),
  );
  writeFile(repoPath, "src/config.json", JSON.stringify({ version: "0.1.0", debug: false }, null, 2) + "\n");
  commit(repoPath, "Initial commit: project scaffold");

  // ── A few linear commits on main ──────────────────────────────────────
  writeFile(repoPath, "docs/getting-started.md", "# Getting Started\n\n1. Clone the repo\n2. Run `npm install`\n3. Run `npm start`\n");
  commit(repoPath, "docs: add getting started guide");

  writeFile(
    repoPath,
    "src/logger.ts",
    [
      'type LogLevel = "info" | "warn" | "error";',
      '',
      'export function log(level: LogLevel, message: string): void {',
      '  const timestamp = new Date().toISOString();',
      '  console.log(`[${timestamp}] [${level.toUpperCase()}] ${message}`);',
      '}',
      '',
    ].join("\n"),
  );
  commit(repoPath, "feat: add logger module");

  // ── Feature branch: add-tests (clean merge) ──────────────────────────
  createBranch(repoPath, "feature/add-tests");

  writeFile(
    repoPath,
    "src/utils.test.ts",
    [
      'import { clamp, capitalize } from "./utils";',
      '',
      'describe("clamp", () => {',
      '  it("clamps below min", () => expect(clamp(-5, 0, 10)).toBe(0));',
      '  it("clamps above max", () => expect(clamp(15, 0, 10)).toBe(10));',
      '  it("passes through values in range", () => expect(clamp(5, 0, 10)).toBe(5));',
      '});',
      '',
      'describe("capitalize", () => {',
      '  it("capitalizes first letter", () => expect(capitalize("hello")).toBe("Hello"));',
      '  it("handles empty string", () => expect(capitalize("")).toBe(""));',
      '});',
      '',
    ].join("\n"),
  );
  commit(repoPath, "test: add unit tests for utils");

  writeFile(
    repoPath,
    "src/main.test.ts",
    [
      'import { greet } from "./main";',
      '',
      'describe("greet", () => {',
      '  it("returns greeting with name", () => {',
      '    expect(greet("Alice")).toBe("Hello, Alice!");',
      '  });',
      '});',
      '',
    ].join("\n"),
  );
  commit(repoPath, "test: add tests for main module");

  writeFile(
    repoPath,
    "jest.config.js",
    [
      "module.exports = {",
      "  preset: 'ts-jest',",
      "  testEnvironment: 'node',",
      "};",
      "",
    ].join("\n"),
  );
  commit(repoPath, "chore: add jest configuration");

  // Back to main and merge
  checkout(repoPath, "main");

  // Add a commit on main while the branch exists (to avoid fast-forward)
  appendFile(repoPath, "README.md", "\n## License\n\nMIT\n");
  commit(repoPath, "docs: add license section to README");

  merge(repoPath, "feature/add-tests", "Merge branch 'feature/add-tests' — add test infrastructure");

  // ── Feature branch: refactor-logger (will have file rename + delete) ──
  createBranch(repoPath, "feature/refactor-logger");

  // Rename logger.ts → logging.ts with changes
  const loggerContent = [
    'type LogLevel = "debug" | "info" | "warn" | "error";',
    '',
    'interface LogEntry {',
    '  timestamp: string;',
    '  level: LogLevel;',
    '  message: string;',
    '}',
    '',
    'const history: LogEntry[] = [];',
    '',
    'export function log(level: LogLevel, message: string): void {',
    '  const entry: LogEntry = {',
    '    timestamp: new Date().toISOString(),',
    '    level,',
    '    message,',
    '  };',
    '  history.push(entry);',
    '  console.log(`[${entry.timestamp}] [${level.toUpperCase()}] ${message}`);',
    '}',
    '',
    'export function getHistory(): readonly LogEntry[] {',
    '  return history;',
    '}',
    '',
  ].join("\n");
  removeFile(repoPath, "src/logger.ts");
  writeFile(repoPath, "src/logging.ts", loggerContent);
  git(repoPath, "add", "-A");
  tick();
  git(repoPath, "commit", "--no-gpg-sign", "-m", "refactor: rename logger → logging, add history");

  // Delete old config, replace with ts
  removeFile(repoPath, "src/config.json");
  writeFile(
    repoPath,
    "src/config.ts",
    [
      'export interface AppConfig {',
      '  version: string;',
      '  debug: boolean;',
      '  logLevel: "debug" | "info" | "warn" | "error";',
      '}',
      '',
      'export const defaultConfig: AppConfig = {',
      '  version: "0.2.0",',
      '  debug: false,',
      '  logLevel: "info",',
      '};',
      '',
    ].join("\n"),
  );
  commit(repoPath, "refactor: replace config.json with typed config.ts");

  checkout(repoPath, "main");
  merge(repoPath, "feature/refactor-logger");

  // ── Feature branch: api-module (larger branch, multiple commits) ──────
  createBranch(repoPath, "feature/api-module");

  writeFile(
    repoPath,
    "src/api/client.ts",
    [
      'export interface RequestOptions {',
      '  method: "GET" | "POST" | "PUT" | "DELETE";',
      '  headers?: Record<string, string>;',
      '  body?: string;',
      '}',
      '',
      'export async function request(url: string, options: RequestOptions): Promise<Response> {',
      '  return fetch(url, {',
      '    method: options.method,',
      '    headers: options.headers,',
      '    body: options.body,',
      '  });',
      '}',
      '',
    ].join("\n"),
  );
  commit(repoPath, "feat: add API client module");

  writeFile(
    repoPath,
    "src/api/endpoints.ts",
    [
      'export const API_BASE = "https://api.example.com/v1";',
      '',
      'export const endpoints = {',
      '  users: `${API_BASE}/users`,',
      '  posts: `${API_BASE}/posts`,',
      '  comments: `${API_BASE}/comments`,',
      '} as const;',
      '',
    ].join("\n"),
  );
  commit(repoPath, "feat: add API endpoint definitions");

  writeFile(
    repoPath,
    "src/api/index.ts",
    [
      'export { request, type RequestOptions } from "./client";',
      'export { endpoints, API_BASE } from "./endpoints";',
      '',
    ].join("\n"),
  );
  commit(repoPath, "feat: add API barrel export");

  // Meanwhile, add something on main so the merge isn't ff
  checkout(repoPath, "main");

  writeFile(repoPath, "CHANGELOG.md", "# Changelog\n\n## 0.2.0\n\n- Added test infrastructure\n- Refactored logger module\n- Replaced JSON config with typed config\n");
  commit(repoPath, "docs: add changelog");

  merge(repoPath, "feature/api-module", "Merge branch 'feature/api-module'");

  // ── Bugfix branch (small, single commit) ──────────────────────────────
  createBranch(repoPath, "fix/clamp-edge-case");

  writeFile(
    repoPath,
    "src/utils.ts",
    [
      'export function clamp(value: number, min: number, max: number): number {',
      '  if (min > max) {',
      '    throw new RangeError(`min (${min}) must be <= max (${max})`);',
      '  }',
      '  return Math.min(Math.max(value, min), max);',
      '}',
      '',
      'export function capitalize(s: string): string {',
      '  if (s.length === 0) return s;',
      '  return s.charAt(0).toUpperCase() + s.slice(1);',
      '}',
      '',
    ].join("\n"),
  );
  commit(repoPath, "fix: validate clamp arguments, guard empty capitalize");

  checkout(repoPath, "main");
  merge(repoPath, "fix/clamp-edge-case");

  // ── A branch that is NOT yet merged (open feature) ────────────────────
  createBranch(repoPath, "feature/dark-mode");

  writeFile(
    repoPath,
    "src/theme.ts",
    [
      'export type Theme = "light" | "dark" | "system";',
      '',
      'export function getSystemTheme(): "light" | "dark" {',
      '  return "light"; // TODO: detect from OS',
      '}',
      '',
      'export function applyTheme(theme: Theme): void {',
      '  const resolved = theme === "system" ? getSystemTheme() : theme;',
      '  document.documentElement.dataset.theme = resolved;',
      '}',
      '',
    ].join("\n"),
  );
  commit(repoPath, "feat: add theme module (WIP)");

  writeFile(
    repoPath,
    "src/theme.test.ts",
    [
      'import { getSystemTheme } from "./theme";',
      '',
      'describe("getSystemTheme", () => {',
      '  it("returns light by default", () => {',
      '    expect(getSystemTheme()).toBe("light");',
      '  });',
      '});',
      '',
    ].join("\n"),
  );
  commit(repoPath, "test: add theme tests (WIP)");

  // ── Go back to main for a couple more commits ─────────────────────────
  checkout(repoPath, "main");

  // Modify a file that the dark-mode branch also touches? No — let's keep
  // that clean.  Instead, add new stuff.
  writeFile(
    repoPath,
    "src/constants.ts",
    [
      'export const APP_NAME = "TestProject";',
      'export const APP_VERSION = "0.3.0";',
      'export const MAX_RETRIES = 3;',
      '',
    ].join("\n"),
  );
  commit(repoPath, "feat: add application constants");

  // Update main.ts to use the new modules
  writeFile(
    repoPath,
    "src/main.ts",
    [
      'import { log } from "./logging";',
      'import { APP_NAME, APP_VERSION } from "./constants";',
      '',
      'export function greet(name: string): string {',
      '  return `Hello, ${name}!`;',
      '}',
      '',
      'log("info", `${APP_NAME} v${APP_VERSION} starting...`);',
      'console.log(greet("World"));',
      '',
    ].join("\n"),
  );
  commit(repoPath, "feat: integrate logger and constants into main");

  // ── Create a branch with diverging changes for conflict potential ──────
  // This branch will NOT be merged automatically — it's left for the user
  // to test conflict resolution manually.
  createBranch(repoPath, "feature/new-greeting");

  writeFile(
    repoPath,
    "src/main.ts",
    [
      'import { log } from "./logging";',
      'import { APP_NAME, APP_VERSION } from "./constants";',
      '',
      'export function greet(name: string, formal = false): string {',
      '  if (formal) {',
      '    return `Good day, ${name}. Welcome.`;',
      '  }',
      '  return `Hey ${name}! 👋`;',
      '}',
      '',
      'log("info", `${APP_NAME} v${APP_VERSION} starting...`);',
      'console.log(greet("World"));',
      '',
    ].join("\n"),
  );
  commit(repoPath, "feat: redesign greeting with formal mode");

  // Go back to main and make a conflicting change to the same function
  checkout(repoPath, "main");

  writeFile(
    repoPath,
    "src/main.ts",
    [
      'import { log } from "./logging";',
      'import { APP_NAME, APP_VERSION } from "./constants";',
      '',
      'export function greet(name: string): string {',
      '  log("debug", `Greeting ${name}`);',
      '  return `Hello, ${name}! Welcome to ${APP_NAME}.`;',
      '}',
      '',
      'log("info", `${APP_NAME} v${APP_VERSION} starting...`);',
      'console.log(greet("World"));',
      '',
    ].join("\n"),
  );
  commit(repoPath, "feat: include app name in greeting, add debug log");

  // ── Add some more files for variety ───────────────────────────────────
  writeFile(
    repoPath,
    "src/api/auth.ts",
    [
      'export interface Credentials {',
      '  username: string;',
      '  password: string;',
      '}',
      '',
      'export async function login(creds: Credentials): Promise<string> {',
      '  // TODO: implement real auth',
      '  return "fake-token-" + creds.username;',
      '}',
      '',
      'export function logout(): void {',
      '  // TODO: clear session',
      '}',
      '',
    ].join("\n"),
  );
  commit(repoPath, "feat: add auth module stub");

  appendFile(repoPath, "CHANGELOG.md", "\n## 0.3.0\n\n- Added API client\n- Added constants module\n- Fixed clamp edge case\n- Added auth module stub\n");
  commit(repoPath, "docs: update changelog for 0.3.0");

  // ── Create a branch for merge-abort testing (conflicts with main) ─────
  // This branch modifies CHANGELOG.md differently from main, creating a conflict.
  createBranch(repoPath, "test/merge-abort-conflict");
  writeFile(repoPath, "CHANGELOG.md", "# Changelog\n\n## 0.3.0 (UNRELEASED)\n\n- ABORT TEST: This changelog conflicts with main\n- Different content here\n");
  commit(repoPath, "docs: conflicting changelog for abort test");
  checkout(repoPath, "main");

  // ── Modify CHANGELOG.md on main so test/merge-abort-conflict truly conflicts ──
  // Both branches now modify the same lines from the common ancestor.
  appendFile(repoPath, "CHANGELOG.md", "\n## Upcoming\n\n- Main branch changelog updates\n");
  commit(repoPath, "docs: add upcoming section to changelog");

  // ── Create a tag ──────────────────────────────────────────────────────
  git(repoPath, "tag", "-a", "v0.3.0", "-m", "Release 0.3.0");

  // ── Create branches for conflict resolution strategy E2E tests (spec 36) ──
  // Each branch modifies src/constants.ts with a distinct APP_VERSION.
  // After spec 36 commits the unstaged constants.ts changes (APP_VERSION = "0.4.0"),
  // merging any of these branches will create a conflict on constants.ts, because
  // both main and the branch diverge from the common ancestor (APP_VERSION = "0.3.0").

  createBranch(repoPath, "test/conflict-accept-current");
  writeFile(repoPath, "src/constants.ts", [
    'export const APP_NAME = "TestProject";',
    'export const APP_VERSION = "1.0.0-alpha";',
    'export const MAX_RETRIES = 3;',
    '',
  ].join("\n"));
  commit(repoPath, "test: modify constants for AcceptCurrent e2e test");
  checkout(repoPath, "main");

  createBranch(repoPath, "test/conflict-accept-incoming");
  writeFile(repoPath, "src/constants.ts", [
    'export const APP_NAME = "TestProject";',
    'export const APP_VERSION = "2.0.0-beta";',
    'export const MAX_RETRIES = 3;',
    '',
  ].join("\n"));
  commit(repoPath, "test: modify constants for AcceptIncoming e2e test");
  checkout(repoPath, "main");

  createBranch(repoPath, "test/conflict-accept-both");
  writeFile(repoPath, "src/constants.ts", [
    'export const APP_NAME = "TestProject";',
    'export const APP_VERSION = "3.0.0-rc";',
    'export const MAX_RETRIES = 3;',
    '',
  ].join("\n"));
  commit(repoPath, "test: modify constants for AcceptBoth e2e test");
  checkout(repoPath, "main");

  // ── Create stash entries ──────────────────────────────────────────────
  // Stash 1: A work-in-progress config change.
  writeFile(
    repoPath,
    "src/config.ts",
    [
      'export interface AppConfig {',
      '  version: string;',
      '  debug: boolean;',
      '  logLevel: "debug" | "info" | "warn" | "error";',
      '  maxRetries: number;',
      '}',
      '',
      'export const defaultConfig: AppConfig = {',
      '  version: "0.4.0",',
      '  debug: true,',
      '  logLevel: "debug",',
      '  maxRetries: 5,',
      '};',
      '',
    ].join("\n"),
  );
  git(repoPath, "add", "src/config.ts");
  tick();
  git(repoPath, "stash", "push", "--staged", "-m", "WIP: config overhaul with retries");

  // Stash 2: A small utility addition.
  writeFile(
    repoPath,
    "src/utils.ts",
    [
      'export function clamp(value: number, min: number, max: number): number {',
      '  if (min > max) {',
      '    throw new RangeError(`min (${min}) must be <= max (${max})`);',
      '  }',
      '  return Math.min(Math.max(value, min), max);',
      '}',
      '',
      'export function capitalize(s: string): string {',
      '  if (s.length === 0) return s;',
      '  return s.charAt(0).toUpperCase() + s.slice(1);',
      '}',
      '',
      'export function truncate(s: string, maxLen: number): string {',
      '  if (s.length <= maxLen) return s;',
      '  return s.slice(0, maxLen - 1) + "…";',
      '}',
      '',
    ].join("\n"),
  );
  git(repoPath, "add", "src/utils.ts");
  tick();
  git(repoPath, "stash", "push", "--staged", "-m", "feat: add truncate utility");

  // Stash 3: Conflicts with the uncommitted changes left below.
  // This stash modifies src/constants.ts differently than the unstaged changes,
  // so applying it will cause a merge conflict.
  writeFile(
    repoPath,
    "src/constants.ts",
    [
      'export const APP_NAME = "TestProject";',
      'export const APP_VERSION = "0.5.0-beta";',
      'export const MAX_RETRIES = 10;',
      'export const REQUEST_TIMEOUT = 60_000;',
      '',
    ].join("\n"),
  );
  git(repoPath, "add", "src/constants.ts");
  tick();
  git(repoPath, "stash", "push", "--staged", "-m", "WIP: bump constants for beta release");

  // ── Create a branch for clean merge testing ───────────────────────────
  // This branch touches a file that main has NOT modified, so merging is clean.
  createBranch(repoPath, "feature/clean-merge-target");
  writeFile(
    repoPath,
    "src/format.ts",
    [
      'export function formatBytes(bytes: number): string {',
      '  const units = ["B", "KB", "MB", "GB"];',
      '  let i = 0;',
      '  let val = bytes;',
      '  while (val >= 1024 && i < units.length - 1) {',
      '    val /= 1024;',
      '    i++;',
      '  }',
      '  return `${val.toFixed(1)} ${units[i]}`;',
      '}',
      '',
    ].join("\n"),
  );
  commit(repoPath, "feat: add byte formatting utility");
  checkout(repoPath, "main");

  // ── Leave uncommitted changes for staging tests ───────────────────────
  // Unstaged: modify an existing file
  writeFile(
    repoPath,
    "src/constants.ts",
    [
      'export const APP_NAME = "TestProject";',
      'export const APP_VERSION = "0.4.0";',
      'export const MAX_RETRIES = 5;',
      'export const TIMEOUT_MS = 30_000;',
      '',
    ].join("\n"),
  );

  // Unstaged: add a new file
  writeFile(
    repoPath,
    "src/helpers.ts",
    [
      'export function sleep(ms: number): Promise<void> {',
      '  return new Promise((resolve) => setTimeout(resolve, ms));',
      '}',
      '',
    ].join("\n"),
  );

  // Staged: stage a modification
  writeFile(repoPath, "docs/getting-started.md", "# Getting Started\n\n1. Clone the repo\n2. Run `npm install`\n3. Run `npm start`\n4. Open http://localhost:3000\n");
  git(repoPath, "add", "docs/getting-started.md");

  // ── Set up a local "remote" with remote-only branches ─────────────────
  // Create a bare clone to act as a remote, add branches there, then
  // add it as a remote to the main repo and fetch.
  const remotePath = resolve(repoPath, "..", basename(repoPath) + "-remote.git");
  // Clone the repo as a bare repository (includes all current branches).
  execFileSync("git", ["clone", "--bare", repoPath, remotePath], {
    encoding: "utf-8",
    env: { ...process.env, GIT_TERMINAL_PROMPT: "0" },
  });

  // Create remote-only branches in the bare repo by branching off main.
  const mainHash = execFileSync("git", ["rev-parse", "main"], {
    cwd: remotePath,
    encoding: "utf-8",
  }).trim();

  // Branch 1: a remote-only feature branch.
  execFileSync("git", ["branch", "feature/remote-analytics", mainHash], {
    cwd: remotePath,
    encoding: "utf-8",
  });

  // Branch 2: another remote-only branch.
  execFileSync("git", ["branch", "feature/remote-notifications", mainHash], {
    cwd: remotePath,
    encoding: "utf-8",
  });

  // Add the bare clone as a remote named "origin" and fetch.
  git(repoPath, "remote", "add", "origin", remotePath);
  git(repoPath, "fetch", "origin");

  // ── Final summary ─────────────────────────────────────────────────────
  console.log("");
  console.log("Branches:");
  console.log(git(repoPath, "branch", "-a"));
  console.log("");
  console.log(`Total commits: ${git(repoPath, "rev-list", "--count", "HEAD")}`);
  console.log("");
  console.log("Notes:");
  console.log("  • 'feature/dark-mode' is an unmerged feature branch (2 commits ahead)");
  console.log("  • 'feature/new-greeting' conflicts with main on src/main.ts");
  console.log("  • 'feature/clean-merge-target' merges cleanly into main");
  console.log("  • 'test/merge-abort-conflict' conflicts with main on CHANGELOG.md");
  console.log("  • 'test/conflict-accept-current' conflicts with main on src/constants.ts (APP_VERSION 1.0.0-alpha)");
  console.log("  • 'test/conflict-accept-incoming' conflicts with main on src/constants.ts (APP_VERSION 2.0.0-beta)");
  console.log("  • 'test/conflict-accept-both' conflicts with main on src/constants.ts (APP_VERSION 3.0.0-rc)");
  console.log("  • The repo has renames (logger→logging), deletions (config.json), and nested dirs (src/api/)");
  console.log("  • Tag v0.3.0 marks the latest release");
  console.log("  • 3 stash entries: config overhaul + truncate utility + conflicting constants");
  console.log("  • 2 unstaged files: src/constants.ts (modified), src/helpers.ts (new)");
  console.log("  • 1 staged file: docs/getting-started.md (modified)");
  console.log("  • Remote 'origin' with 2 remote-only branches: feature/remote-analytics, feature/remote-notifications");
}

// ---------------------------------------------------------------------------
// CLI entry point
// ---------------------------------------------------------------------------

function main(): void {
  const targetArg = process.argv[2];

  if (!targetArg) {
    console.error("Usage: node --experimental-strip-types scripts/create-test-repo.ts <target-folder>");
    process.exit(1);
  }

  const targetPath = resolve(targetArg);

  if (existsSync(targetPath)) {
    console.error(`Error: '${targetPath}' already exists. Pick a new folder to avoid data loss.`);
    process.exit(1);
  }

  mkdirSync(targetPath, { recursive: true });
  console.log(`Creating test repository in ${targetPath} ...`);

  buildRepo(targetPath);

  console.log(`\nDone! Repository created at ${targetPath}`);
}

main();
