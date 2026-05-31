/**
 * CheeseGit E2E — App interaction helpers
 *
 * High-level helpers that abstract over WebDriver selectors so test specs
 * read like plain-English scenarios. Each function interacts with a
 * specific part of the app UI.
 */

import path from "path";
import { fileURLToPath } from "url";

import {
  jsClick,
  jsCtrlClick,
  jsMoveTo,
  jsSetValue,
  jsClearValue,
  jsKeys,
  jsShiftRangeClick,
  waitFor,
  sleep,
} from "./webdriver.js";

import { resetTestRepo, commitAllViaGit } from "./repo.js";

const __dirname = fileURLToPath(new URL(".", import.meta.url));

/** Absolute path to the test repository created by the setup script. */
export const TEST_REPO_PATH = path.resolve(__dirname, "..", ".test-repo");

// ── App lifecycle ───────────────────────────────────────────────────────

/** Wait until the app is no longer showing a loading state. */
export async function waitForAppReady() {
  await browser.waitUntil(
    async () => {
      const welcome = await $("[data-testid='welcome-panel']");
      const repo = await $("[data-testid='repo-view']");
      return (await welcome.isExisting()) || (await repo.isExisting());
    },
    { timeout: 15_000, timeoutMsg: "App did not become ready within 15 s" },
  );
}

/** Wait for staging to finish loading. */
export async function waitForStagingLoaded() {
  await browser.waitUntil(
    async () => {
      const loading = await $("span=Loading…");
      return !(await loading.isExisting());
    },
    { timeout: 10_000 },
  );
}

// ── Welcome panel ───────────────────────────────────────────────────────

export async function isWelcomeVisible() {
  const el = await $("[data-testid='welcome-panel']");
  return el.isExisting();
}

export async function clickCreateRepo() {
  const btn = await $("[data-testid='welcome-create-repo']");
  await jsClick(btn);
}

export async function clickOpenRepo() {
  const btn = await $("[data-testid='welcome-open-repo']");
  await jsClick(btn);
}

export async function clickCloneRepo() {
  const btn = await $("[data-testid='welcome-clone-repo']");
  await jsClick(btn);
}

// ── Tab bar ─────────────────────────────────────────────────────────────

export async function getOpenTabs(): Promise<string[]> {
  return browser.execute(() => {
    const tabs = document.querySelectorAll("[data-testid='repo-tab']");
    return Array.from(tabs).map((tab) => {
      const clone = tab.cloneNode(true) as HTMLElement;
      const closeBtn = clone.querySelector("[data-testid='close-tab']");
      if (closeBtn) closeBtn.remove();
      return clone.textContent?.trim() ?? "";
    });
  });
}

export async function switchToTab(name: string) {
  const index = await browser.execute((tabName: string) => {
    const tabs = document.querySelectorAll("[data-testid='repo-tab']");
    for (let i = 0; i < tabs.length; i++) {
      const clone = tabs[i].cloneNode(true) as HTMLElement;
      const closeBtn = clone.querySelector("[data-testid='close-tab']");
      if (closeBtn) closeBtn.remove();
      if (clone.textContent?.trim().includes(tabName)) return i;
    }
    return -1;
  }, name);
  if (index === -1) throw new Error(`Tab "${name}" not found`);
  const tabs = await $$("[data-testid='repo-tab']");
  await jsClick(tabs[index]);
  await sleep(300);
}

export async function closeTab(name: string) {
  const index = await browser.execute((tabName: string) => {
    const tabs = document.querySelectorAll("[data-testid='repo-tab']");
    for (let i = 0; i < tabs.length; i++) {
      const clone = tabs[i].cloneNode(true) as HTMLElement;
      const closeBtn = clone.querySelector("[data-testid='close-tab']");
      if (closeBtn) closeBtn.remove();
      if (clone.textContent?.trim().includes(tabName)) return i;
    }
    return -1;
  }, name);
  if (index === -1) throw new Error(`Tab "${name}" not found`);
  const tabs = await $$("[data-testid='repo-tab']");
  const closeBtn = await tabs[index].$("[data-testid='close-tab']");
  await jsClick(closeBtn);
  await sleep(300);
}

/** Close all open repo tabs to return to welcome screen. */
export async function closeAllTabs() {
  const tabs = await $$("[data-testid='repo-tab']");
  for (let i = tabs.length - 1; i >= 0; i--) {
    const closeBtn = await tabs[i].$("[data-testid='close-tab']");
    await jsClick(closeBtn);
    await sleep(300);
  }
}

export async function openRepoViaTabBar() {
  const addBtn = await $("[data-testid='add-repo-button']");
  await jsClick(addBtn);
  await sleep(200);
  const item = await $("[data-testid='menu-open-repo']");
  await jsClick(item);
}

/** Get the count of open tabs. */
export async function getTabCount(): Promise<number> {
  const tabs = await $$("[data-testid='repo-tab']");
  return tabs.length;
}

// ── Left panel tabs ─────────────────────────────────────────────────────

export async function switchLeftPanel(tab: "staging" | "history" | "stash") {
  const btn = await $(`[data-testid='left-tab-${tab}']`);
  await jsClick(btn);
  await sleep(300);
}

export async function getActiveLeftTab(): Promise<string> {
  const tabs = await $$("[data-testid^='left-tab-']");
  for (const tab of tabs) {
    const cls = await tab.getAttribute("class");
    if (cls?.includes("border-accent")) {
      return (await tab.getText()).toLowerCase();
    }
  }
  return "";
}

// ── Branch bar ──────────────────────────────────────────────────────────

export async function getCurrentBranch(): Promise<string> {
  return browser.execute(() => {
    const el = document.querySelector("[data-testid='branch-selector']");
    if (!el) return "";
    const span = el.querySelector(".font-medium");
    if (span) return span.textContent?.trim() ?? "";
    return el.textContent?.trim() ?? "";
  });
}

export async function openBranchDropdown() {
  const existing = await $("[data-testid='branch-dropdown']");
  if (await existing.isExisting()) return;
  const btn = await $("[data-testid='branch-selector']");
  await jsClick(btn);
  await waitFor("[data-testid='branch-dropdown']");
  // Wait for branches to finish loading before interacting
  await waitFor("[data-testid^='branch-row-']");
}

export async function closeBranchDropdown() {
  await jsKeys("Escape");
  await sleep(200);
}

export async function switchBranch(name: string) {
  await openBranchDropdown();
  const input = await $("[data-testid='branch-search']");
  await jsSetValue(input, name);
  await sleep(300);
  // Click the branch-name button inside the row (not the row div itself)
  const row = await $(`[data-testid='branch-row-${name}']`);
  const nameBtn = (await row.$$("button"))[1];
  await jsClick(nameBtn);
  await sleep(500);
}

export async function createBranch(name: string) {
  await openBranchDropdown();
  const input = await $("[data-testid='branch-search']");
  await jsSetValue(input, name);
  await sleep(300);
  const createBtn = await $("[data-testid='create-branch-button']");
  await jsClick(createBtn);
  await sleep(500);
}

export async function getBranchList(): Promise<string[]> {
  await openBranchDropdown();
  await sleep(300);
  const rows = await $$("[data-testid^='branch-row-']");
  const names: string[] = [];
  for (const row of rows) {
    const tid = await row.getAttribute("data-testid");
    if (tid) names.push(tid.replace("branch-row-", ""));
  }
  await jsKeys("Escape");
  await sleep(200);
  return names;
}

export async function getRemoteBranchList(): Promise<string[]> {
  await openBranchDropdown();
  await sleep(300);
  const rows = await $$("[data-testid^='remote-branch-row-']");
  const names: string[] = [];
  for (const row of rows) {
    const tid = await row.getAttribute("data-testid");
    if (tid) names.push(tid.replace("remote-branch-row-", ""));
  }
  await jsKeys("Escape");
  await sleep(200);
  return names;
}

export async function switchToRemoteBranch(name: string) {
  await openBranchDropdown();
  const input = await $("[data-testid='branch-search']");
  await jsSetValue(input, name);
  await sleep(300);
  const row = await $(`[data-testid='remote-branch-row-${name}']`);
  const nameBtn = await row.$("button");
  await jsClick(nameBtn);
  await sleep(500);
}

// ── Staging panel ───────────────────────────────────────────────────────

export async function getUnstagedFiles(): Promise<string[]> {
  const section = await $("[data-testid='unstaged-section']");
  if (!(await section.isExisting())) return [];
  const rows = await section.$$("[data-testid='file-row']");
  const paths: string[] = [];
  for (const row of rows) {
    paths.push((await row.getAttribute("data-filepath")) ?? "");
  }
  return paths;
}

export async function getStagedFiles(): Promise<string[]> {
  const section = await $("[data-testid='staged-section']");
  if (!(await section.isExisting())) return [];
  const rows = await section.$$("[data-testid='file-row']");
  const paths: string[] = [];
  for (const row of rows) {
    paths.push((await row.getAttribute("data-filepath")) ?? "");
  }
  return paths;
}

export async function stageFile(filePath: string) {
  const row = await $(`[data-testid='file-row'][data-filepath='${filePath}']`);
  const btn = await row.$("[data-testid='file-action']");
  await jsClick(btn);
  await sleep(300);
}

export async function stageAll() {
  const btn = await $("[data-testid='stage-all']");
  if (!(await btn.isExisting())) return;
  await jsClick(btn);
  await sleep(300);
}

export async function unstageAll() {
  const btn = await $("[data-testid='unstage-all']");
  if (!(await btn.isExisting())) return;
  await jsClick(btn);
  await sleep(300);
}

export async function selectFileForDiff(filePath: string) {
  const row = await $(`[data-testid='file-row'][data-filepath='${filePath}']`);
  await jsClick(row);
  await sleep(300);
}

export async function ctrlSelectFile(filePath: string) {
  const row = await $(`[data-testid='file-row'][data-filepath='${filePath}']`);
  await jsCtrlClick(row);
  await sleep(250);
}

export async function shiftSelectFile(filePath: string) {
  const row = await $(`[data-testid='file-row'][data-filepath='${filePath}']`);
  await jsShiftRangeClick(row);
  await sleep(250);
}

export async function getSelectedFilesInSection(section: "unstaged" | "staged"): Promise<string[]> {
  const sectionEl = await $(`[data-testid='${section}-section']`);
  if (!(await sectionEl.isExisting())) return [];
  const rows = await sectionEl.$$(`[data-testid='file-row'][data-selected='true']`);
  const paths: string[] = [];
  for (const row of rows) {
    paths.push((await row.getAttribute("data-filepath")) ?? "");
  }
  return paths;
}

export async function getBulkActionLabel(kind: "stage" | "unstage"): Promise<string> {
  const testId = kind === "stage" ? "stage-all" : "unstage-all";
  return browser.execute((id: string) => {
    const btn = document.querySelector(`[data-testid='${id}']`);
    return btn?.textContent?.trim() ?? "";
  }, testId);
}

export async function hasNoChanges(): Promise<boolean> {
  return browser.execute(() => {
    // New UI shows "Working tree clean" while older UI used "No changes".
    const cleanLabel = Array.from(document.querySelectorAll("span, p, div")).some((el) => {
      const text = el.textContent?.trim();
      return text === "Working tree clean" || text === "No changes";
    });
    if (cleanLabel) return true;

    // Fallback: if both staging sections exist and contain no file rows, treat as clean.
    const fileRows = document.querySelectorAll(
      "[data-testid='unstaged-section'] [data-testid='file-row'], [data-testid='staged-section'] [data-testid='file-row']",
    );
    return fileRows.length === 0;
  });
}

// ── Commit ──────────────────────────────────────────────────────────────

export async function setCommitSummary(text: string) {
  const input = await $("[data-testid='commit-summary']");
  await jsSetValue(input, text);
}

export async function setCommitDescription(text: string) {
  const textarea = await $("[data-testid='commit-description']");
  await jsSetValue(textarea, text);
}

export async function clickCommit() {
  const btn = await $("[data-testid='commit-button']");
  await jsClick(btn);
  await sleep(500);
}

/** After clicking a history commit, select the first file to show its diff. */
export async function selectFirstCommitFile() {
  const entry = await $("[data-testid='commit-file-entry']");
  await entry.waitForExist({ timeout: 8000 });
  await jsClick(entry);
  await sleep(500);
}

export async function getCommitButtonText(): Promise<string> {
  return browser.execute(() => {
    const btn = document.querySelector("[data-testid='commit-button']");
    return btn?.textContent?.trim() ?? "";
  });
}

export async function isCommitDisabled(): Promise<boolean> {
  const btn = await $("[data-testid='commit-button']");
  return !(await btn.isEnabled());
}

// ── History ─────────────────────────────────────────────────────────────

export async function getHistoryCommits(): Promise<string[]> {
  await switchLeftPanel("history");
  await sleep(500);
  return browser.execute(() => {
    const rows = document.querySelectorAll("[data-testid='history-row']");
    const messages: string[] = [];
    rows.forEach((row) => {
      const msg = row.querySelector("[data-testid='commit-message']");
      if (msg) messages.push(msg.textContent?.trim() ?? "");
    });
    return messages;
  });
}

export async function clickHistoryCommit(index: number) {
  const rows = await $$("[data-testid='history-row']");
  if (index >= rows.length) throw new Error(`History row ${index} not found`);
  await jsClick(rows[index]);
  await sleep(500);
}

export async function ctrlClickHistoryCommit(index: number) {
  const rows = await $$("[data-testid='history-row']");
  if (index >= rows.length) throw new Error(`History row ${index} not found`);
  await jsCtrlClick(rows[index]);
  await sleep(350);
}

export async function clickHistoryCherryPick() {
  const btn = await $("[data-testid='history-cherry-pick-button']");
  await jsClick(btn);
  await sleep(300);
}

export async function createCherryPickBranch(name: string) {
  await waitFor("[data-testid='cherry-pick-target-picker']", 5000);
  const input = await $("[data-testid='cherry-pick-branch-search']");
  await jsSetValue(input, name);
  await sleep(200);
  const createBtn = await $("[data-testid='cherry-pick-create-branch-button']");
  await jsClick(createBtn);
  await sleep(600);
}

export async function selectCherryPickBranch(name: string) {
  await waitFor("[data-testid='cherry-pick-target-picker']", 5000);
  const input = await $("[data-testid='cherry-pick-branch-search']");
  await jsClearValue(input);
  await jsSetValue(input, name);
  await sleep(200);
  const row = await $(`[data-testid='cherry-pick-branch-row-${name}']`);
  await jsClick(row);
  await sleep(700);
}

// ── Diff viewer ─────────────────────────────────────────────────────────

export async function isDiffVisible(): Promise<boolean> {
  const el = await $("[data-testid='diff-viewer']");
  return el.isExisting();
}

/** Wait until the diff viewer element exists (up to `ms`). */
export async function waitForDiffVisible(ms = 8000) {
  await waitFor("[data-testid='diff-viewer']", ms);
}

export async function getDiffViewMode(): Promise<"unified" | "split"> {
  return browser.execute(() => {
    const unified = document.querySelector("[data-testid='diff-mode-unified']");
    if (!unified) return "unified";
    return unified.className.includes("bg-accent") ? "unified" : "split";
  }) as Promise<"unified" | "split">;
}

export async function setDiffViewMode(mode: "unified" | "split") {
  await waitFor(`[data-testid='diff-mode-${mode}']`, 5000);
  const btn = await $(`[data-testid='diff-mode-${mode}']`);
  await jsClick(btn);
  await sleep(200);
}

// ── Command log ─────────────────────────────────────────────────────────

export async function toggleCommandLog() {
  const btn = await $("[data-testid='command-log-toggle']");
  await jsClick(btn);
  await sleep(300);
}

export async function isCommandLogOpen(): Promise<boolean> {
  const panel = await $("[data-testid='command-log-panel']");
  return panel.isExisting();
}

export async function getCommandLogEntryCount(): Promise<number> {
  return browser.execute(() => {
    const badge = document.querySelector("[data-testid='command-log-count']");
    if (!badge) return 0;
    return parseInt(badge.textContent?.trim() ?? "0", 10);
  });
}

// ── Stash ───────────────────────────────────────────────────────────────

export async function getStashEntries(): Promise<string[]> {
  await switchLeftPanel("stash");
  await sleep(500);
  return browser.execute(() => {
    const rows = document.querySelectorAll("[data-testid='stash-row']");
    const messages: string[] = [];
    rows.forEach((row) => {
      const msg = row.querySelector("[data-testid='stash-message']");
      if (msg) messages.push(msg.textContent?.trim() ?? "");
    });
    return messages;
  });
}

export async function applyStash(index: number) {
  const rows = await $$("[data-testid='stash-row']");
  if (index >= rows.length) throw new Error(`Stash row ${index} not found`);
  await jsMoveTo(rows[index]);
  await sleep(200);
  const applyBtn = await rows[index].$("[data-testid='stash-apply']");
  await jsClick(applyBtn);
  await sleep(200);
  const confirmBtn = await $("[data-testid='confirm-button']");
  await jsClick(confirmBtn);
  await sleep(500);
}

export async function dropStash(index: number) {
  const rows = await $$("[data-testid='stash-row']");
  if (index >= rows.length) throw new Error(`Stash row ${index} not found`);
  await jsMoveTo(rows[index]);
  await sleep(200);
  const dropBtn = await rows[index].$("[data-testid='stash-drop']");
  await jsClick(dropBtn);
  await sleep(200);
  const confirmBtn = await $("[data-testid='confirm-button']");
  await jsClick(confirmBtn);
  await sleep(500);
}

export async function popStash(index: number) {
  const rows = await $$("[data-testid='stash-row']");
  if (index >= rows.length) throw new Error(`Stash row ${index} not found`);
  await jsMoveTo(rows[index]);
  await sleep(200);
  const popBtn = await rows[index].$("[data-testid='stash-pop']");
  await jsClick(popBtn);
  await sleep(200);
  const confirmBtn = await $("[data-testid='confirm-button']");
  await jsClick(confirmBtn);
  await sleep(500);
}

/** Get the number shown in the stash tab badge. */
export async function getStashTabCount(): Promise<number> {
  return browser.execute(() => {
    const tab = document.querySelector("[data-testid='left-tab-stash']");
    if (!tab) return 0;
    const text = tab.textContent?.trim() ?? "";
    const match = text.match(/\((\d+)\)/);
    return match ? parseInt(match[1], 10) : 0;
  });
}

// ── Theme ───────────────────────────────────────────────────────────────

export async function setThemeViaWelcome(theme: "system" | "light" | "dark" | "high-contrast") {
  const btn = await $(`[data-testid='theme-${theme}']`);
  await jsClick(btn);
  await sleep(200);
}

export async function getActiveTheme(): Promise<string> {
  const root = await $("html");
  return (await root.getAttribute("data-theme")) ?? "system";
}

// ── Remote button ───────────────────────────────────────────────────────

export async function getRemoteButtonText(): Promise<string> {
  return browser.execute(() => {
    const btn = document.querySelector("[data-testid='remote-button']");
    return btn?.textContent?.trim() ?? "";
  });
}

// ── Options menu ────────────────────────────────────────────────────────

export async function openOptionsMenu() {
  const btn = await $("[data-testid='options-menu-button']");
  await jsClick(btn);
  await sleep(200);
}

// ── Open repo by path (IPC shortcut) ────────────────────────────────────

export async function openRepoByPath(repoPath: string) {
  await browser.executeAsync((p: string, done: (result?: unknown) => void) => {
    const { invoke } = (window as any).__TAURI_INTERNALS__;
    invoke("open_repository", { path: p }).then(() => done(), () => done());
  }, repoPath);
  await browser.executeAsync((p: string, done: (result?: unknown) => void) => {
    if ((window as any).__cheesegit_openRepo) {
      (window as any).__cheesegit_openRepo(p).then(() => done(), () => done());
    } else {
      done();
    }
  }, repoPath);
  await sleep(1000);
  await waitForAppReady();
}

// ── Confirm / cancel dialogs ────────────────────────────────────────────

export async function confirmDialog() {
  const btn = await $("[data-testid='confirm-button']");
  await jsClick(btn);
  await sleep(300);
}

export async function cancelDialog() {
  const btn = await $("[data-testid='cancel-button']");
  await jsClick(btn);
  await sleep(300);
}

// ── Merge / Revert dialog ───────────────────────────────────────────────

export async function isMergeDialogVisible(): Promise<boolean> {
  const el = await $("[data-testid='merge-dialog']");
  return el.isExisting();
}

export async function mergeBranch(name: string) {
  await openBranchDropdown();
  const input = await $("[data-testid='branch-search']");
  await jsSetValue(input, name);
  await sleep(300);
  const mergeBtn = await $(`[data-testid='merge-branch-${name}']`);
  await jsClick(mergeBtn);
  await sleep(1000);
}

export async function getConflictFiles(): Promise<string[]> {
  return browser.execute(() => {
    const rows = document.querySelectorAll("[data-testid='conflict-file-row']");
    const paths: string[] = [];
    rows.forEach((row) => {
      const text = row.textContent?.trim() ?? "";
      paths.push(text.split("\n")[0].trim());
    });
    return paths;
  });
}

export async function setConflictResolution(resolution: "AcceptCurrent" | "AcceptIncoming" | "AcceptBoth") {
  const btn = await $(`[data-testid='resolution-${resolution}']`);
  await jsClick(btn);
  await sleep(300);
}

export async function setConflictResolutionForFile(
  fileIndex: number,
  resolution: "AcceptCurrent" | "AcceptIncoming" | "AcceptBoth",
) {
  const rows = await $$("[data-testid='conflict-file-row']");
  if (fileIndex >= rows.length) throw new Error(`Conflict file row ${fileIndex} not found`);
  const btn = await rows[fileIndex].$(`[data-testid='resolution-${resolution}']`);
  await jsClick(btn);
  await sleep(300);
}

export async function completeMerge() {
  const btn = await $("[data-testid='merge-complete-button']");
  await jsClick(btn);
  await sleep(1000);
}

export async function abortMerge() {
  const btn = await $("[data-testid='merge-abort-button']");
  await jsClick(btn);
  await sleep(1000);
}

export async function getMergeCommitMessage(): Promise<string> {
  const input = await $("[data-testid='merge-commit-message']");
  return input.getValue();
}

export async function setMergeCommitMessage(text: string) {
  const input = await $("[data-testid='merge-commit-message']");
  await jsClearValue(input);
  await jsSetValue(input, text);
}

export async function isMergeCompleteEnabled(): Promise<boolean> {
  const btn = await $("[data-testid='merge-complete-button']");
  return btn.isEnabled();
}

export async function getMergeCompleteButtonText(): Promise<string> {
  return browser.execute(() => {
    const btn = document.querySelector("[data-testid='merge-complete-button']");
    return btn?.textContent?.trim() ?? "";
  });
}

// ── Revert ──────────────────────────────────────────────────────────────

export async function revertCommit(index: number) {
  const rows = await $$("[data-testid='history-row']");
  if (index >= rows.length) throw new Error(`History row ${index} not found`);
  await jsMoveTo(rows[index]);
  await sleep(300);
  const revertBtn = await rows[index].$("[data-testid='revert-commit-button']");
  await jsClick(revertBtn);
  await sleep(1000);
}

// ── Branch deletion ─────────────────────────────────────────────────────

export async function deleteBranch(name: string) {
  await openBranchDropdown();
  const input = await $("[data-testid='branch-search']");
  await jsSetValue(input, name);
  await sleep(300);
  const deleteBtn = await $(`[data-testid='delete-branch-${name}']`);
  await jsClick(deleteBtn);
  await sleep(500);
}

export async function isDeleteBranchDialogVisible(): Promise<boolean> {
  const el = await $("[data-testid='delete-branch-dialog']");
  return el.isExisting();
}

export async function confirmDeleteBranch() {
  const btn = await $("[data-testid='delete-branch-confirm']");
  await jsClick(btn);
  await sleep(500);
}

export async function cancelDeleteBranch() {
  const btn = await $("[data-testid='delete-branch-cancel']");
  await jsClick(btn);
  await sleep(300);
}

// ── Discard operations ──────────────────────────────────────────────────

export async function discardFile(filePath: string) {
  await browser.executeAsync((fp: string, done: (result?: unknown) => void) => {
    const store = (window as any).__zustand_staging_store;
    if (store) {
      store.getState().discardFile(
        store.getState().repoPath,
        fp,
        "Unstaged",
      ).then(() => done(), () => done());
    } else {
      done();
    }
  }, filePath);
  await sleep(500);
}

export async function isDiscardDialogVisible(): Promise<boolean> {
  const el = await $("[data-testid='discard-dialog']");
  return el.isExisting();
}

export async function confirmDiscard() {
  const btn = await $("[data-testid='discard-confirm-button']");
  await jsClick(btn);
  await sleep(500);
}

export async function cancelDiscard() {
  const btn = await $("[data-testid='discard-cancel-button']");
  await jsClick(btn);
  await sleep(300);
}

// ── Alert banners ───────────────────────────────────────────────────────

export async function getAlertMessages(): Promise<string[]> {
  const alerts = await $$(".border-b.px-3.py-1\\.5.text-sm");
  const messages: string[] = [];
  for (const alert of alerts) {
    messages.push(await alert.getText());
  }
  return messages;
}

export async function hasErrorAlert(): Promise<boolean> {
  const alerts = await $$(".bg-danger\\/10");
  return alerts.length > 0;
}

export async function getInfoAlertMessages(): Promise<string[]> {
  return browser.execute(() => {
    const els = document.querySelectorAll("[data-testid='alert-info']");
    return Array.from(els).map(el => el.textContent ?? "");
  });
}

export async function getWarningAlertMessages(): Promise<string[]> {
  const alerts = await $$("[data-testid='alert-warning'], [data-testid='alert-merge-in-progress']");
  const messages: string[] = [];
  for (const alert of alerts) {
    messages.push(await alert.getText());
  }
  return messages;
}

export async function hasMergeInProgressAlert(): Promise<boolean> {
  const btn = await $("[data-testid='merge-in-progress-resolve']");
  return btn.isExisting();
}

export async function clickMergeInProgressResolve() {
  const btn = await $("[data-testid='merge-in-progress-resolve']");
  await jsClick(btn);
  await sleep(1000);
}

// ── Git helpers (run git commands via the app's backend) ─────────────────

export async function gitInTestRepo(...args: string[]) {
  return browser.executeAsync((repoPath: string, gitArgs: string[], done: (result?: unknown) => void) => {
    const { invoke } = (window as any).__TAURI_INTERNALS__;
    invoke("run_git_command", { path: repoPath, args: gitArgs })
      .then((result: unknown) => done(result), () => done());
  }, TEST_REPO_PATH, args);
}

// ── Setup helpers ───────────────────────────────────────────────────────

/** Commit all dirty changes so the working tree is clean (required before branch switching). */
export async function commitAllChanges(message = "test: commit dirty changes") {
  await switchLeftPanel("staging");
  await waitForStagingLoaded();
  const noChanges = await hasNoChanges();
  if (noChanges) return;
  // stage-all may not exist if all changes are already staged
  const stageAllBtn = await $("[data-testid='stage-all']");
  if (await stageAllBtn.isExisting()) {
    await jsClick(stageAllBtn);
    await sleep(300);
  }
  await setCommitSummary(message);
  await clickCommit();
  await sleep(500);
}

// ── Spec setup helpers ──────────────────────────────────────────────────

/**
 * Reset the test repo to pristine state and open it in the app.
 * The repo will have the default dirty files from create-test-repo.ts.
 * Call in each spec's before() hook for test isolation.
 */
export async function setupTest() {
  await waitForAppReady();
  await closeAllTabs();
  await sleep(500);
  resetTestRepo();
  await openRepoByPath(TEST_REPO_PATH);
  await waitForStagingLoaded();
}

/**
 * Like setupTest(), but commits all dirty files via git CLI before opening.
 * Use for specs that need a clean working tree (e.g., merge, branch tests).
 */
export async function setupTestClean() {
  await waitForAppReady();
  await closeAllTabs();
  await sleep(500);
  resetTestRepo();
  commitAllViaGit("setup: commit working tree changes");
  await openRepoByPath(TEST_REPO_PATH);
  await waitForStagingLoaded();
}

/**
 * Close all tabs to return to the welcome screen.
 * Use for specs that test welcome panel or don't need a repo.
 */
export async function setupTestNoRepo() {
  await waitForAppReady();
  await closeAllTabs();
  await sleep(500);
}
