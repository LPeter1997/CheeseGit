/**
 * E2E: Remote Management — add/remove remotes via the UI.
 *
 * Tests the remote button, dropdown, add-remote dialog, and remove-remote flow.
 * Uses a freshly created bare repo as a second remote for add/remove tests.
 */
import { execSync } from "node:child_process";
import { mkdtempSync, writeFileSync } from "node:fs";
import { tmpdir } from "node:os";
import path from "node:path";
import {
  openRepoByPath,
  getRemoteButtonText,
  setupTest,
  sleep,
  TEST_REPO_PATH,
} from "../helpers/app.js";
import { jsClick, jsSetValue, waitFor } from "../helpers/webdriver.js";

/** Read textContent from a data-testid element via JS execution. */
async function getTestIdText(testId: string): Promise<string> {
  return browser.execute((id: string) => {
    const el = document.querySelector(`[data-testid='${id}']`);
    return el?.textContent?.trim() ?? "";
  }, testId);
}

/** Create a minimal git repo with no remote. Returns its absolute path. */
function createNoRemoteRepo(): string {
  const dir = mkdtempSync(path.join(tmpdir(), "cheesegit-no-remote-"));
  execSync("git init", { cwd: dir });
  execSync('git config user.email "test@test.com"', { cwd: dir });
  execSync('git config user.name "Test"', { cwd: dir });
  execSync("git config commit.gpgsign false", { cwd: dir });
  writeFileSync(path.join(dir, "README.md"), "# No remote\n");
  execSync("git add -A && git commit -m 'init'", { cwd: dir });
  return dir;
}

/** Create a bare repo that can be used as a remote. Returns its absolute path. */
function createBareRepo(): string {
  const dir = mkdtempSync(path.join(tmpdir(), "cheesegit-bare-"));
  execSync("git init --bare", { cwd: dir });
  return dir;
}

describe("Remote Management", () => {
  describe("Remote button visible without remotes", () => {
    let noRemotePath: string;

    before(async () => {
      noRemotePath = createNoRemoteRepo();
      await setupTest();
      await openRepoByPath(noRemotePath);
      await sleep(500);
    });

    it("remote button is shown with 'Add remote' label when no remotes exist", async () => {
      const btn = await $("[data-testid='remote-button']");
      expect(await btn.isExisting()).toBe(true);
      const text = await getRemoteButtonText();
      expect(text.toLowerCase()).toContain("add remote");
    });

    it("dropdown toggle is visible even without remotes", async () => {
      const toggle = await $("[data-testid='remote-dropdown-toggle']");
      expect(await toggle.isExisting()).toBe(true);
    });

    it("clicking remote button opens add remote dialog when no remotes", async () => {
      const btn = await $("[data-testid='remote-button']");
      await jsClick(btn);
      await sleep(300);
      const dialog = await $("[data-testid='add-remote-dialog']");
      expect(await dialog.isExisting()).toBe(true);
      // Close it
      const cancelBtn = await $("[data-testid='add-remote-cancel']");
      await jsClick(cancelBtn);
      await sleep(200);
    });
  });

  describe("Add remote dialog", () => {
    let noRemotePath: string;

    before(async () => {
      noRemotePath = createNoRemoteRepo();
      await setupTest();
      await openRepoByPath(noRemotePath);
      await sleep(500);
    });

    it("pre-fills 'origin' for first remote name", async () => {
      const btn = await $("[data-testid='remote-button']");
      await jsClick(btn);
      await sleep(300);

      const nameInput = await $("[data-testid='add-remote-name']");
      const value = await nameInput.getValue();
      expect(value).toBe("origin");

      const cancelBtn = await $("[data-testid='add-remote-cancel']");
      await jsClick(cancelBtn);
      await sleep(200);
    });

    it("can add a remote via the dialog", async () => {
      const barePath = createBareRepo();

      const btn = await $("[data-testid='remote-button']");
      await jsClick(btn);
      await sleep(300);

      const urlInput = await $("[data-testid='add-remote-url']");
      await jsSetValue(urlInput, barePath);

      const nameInput = await $("[data-testid='add-remote-name']");
      // Name should already be "origin" for first remote
      const nameVal = await nameInput.getValue();
      expect(nameVal).toBe("origin");

      const confirmBtn = await $("[data-testid='add-remote-confirm']");
      await jsClick(confirmBtn);
      await sleep(1500);

      // Dialog should be closed and remote button should now show a remote action
      const dialog = await $("[data-testid='add-remote-dialog']");
      expect(await dialog.isExisting()).toBe(false);

      // The remote button should now show something other than "Add remote"
      const text = await getRemoteButtonText();
      expect(text.toLowerCase()).not.toContain("add remote");
    });
  });

  describe("Remote dropdown with existing remotes", () => {
    before(async () => {
      await setupTest();
      await sleep(500);
    });

    it("dropdown toggle is visible with remotes", async () => {
      const toggle = await $("[data-testid='remote-dropdown-toggle']");
      expect(await toggle.isExisting()).toBe(true);
    });

    it("dropdown shows remotes and add remote button", async () => {
      const toggle = await $("[data-testid='remote-dropdown-toggle']");
      await jsClick(toggle);
      await sleep(300);

      const dropdown = await $("[data-testid='remote-dropdown']");
      expect(await dropdown.isExisting()).toBe(true);

      // Should show add remote button at the bottom
      const addBtn = await $("[data-testid='add-remote-button']");
      expect(await addBtn.isExisting()).toBe(true);

      // Should show the origin remote
      const originRow = await $("[data-testid='remote-row-origin']");
      expect(await originRow.isExisting()).toBe(true);

      // Close dropdown
      await jsClick(toggle);
      await sleep(200);
    });

    it("add remote button in dropdown opens dialog", async () => {
      const toggle = await $("[data-testid='remote-dropdown-toggle']");
      await jsClick(toggle);
      await sleep(300);

      const addBtn = await $("[data-testid='add-remote-button']");
      await jsClick(addBtn);
      await sleep(300);

      const dialog = await $("[data-testid='add-remote-dialog']");
      expect(await dialog.isExisting()).toBe(true);

      // Name should NOT be "origin" since one remote already exists
      const nameInput = await $("[data-testid='add-remote-name']");
      const nameVal = await nameInput.getValue();
      expect(nameVal).not.toBe("origin");

      const cancelBtn = await $("[data-testid='add-remote-cancel']");
      await jsClick(cancelBtn);
      await sleep(200);
    });

    it("shows remove button for each remote", async () => {
      const toggle = await $("[data-testid='remote-dropdown-toggle']");
      await jsClick(toggle);
      await sleep(300);

      const removeBtn = await $("[data-testid='remove-remote-origin']");
      expect(await removeBtn.isExisting()).toBe(true);

      await jsClick(toggle);
      await sleep(200);
    });
  });

  describe("Add and remove a second remote", () => {
    before(async () => {
      await setupTest();
      await sleep(500);
    });

    it("can add a second remote", async () => {
      const barePath = createBareRepo();

      // Open the dropdown
      const toggle = await $("[data-testid='remote-dropdown-toggle']");
      await jsClick(toggle);
      await sleep(300);

      // Click add remote
      const addBtn = await $("[data-testid='add-remote-button']");
      await jsClick(addBtn);
      await sleep(300);

      // Fill in the URL
      const urlInput = await $("[data-testid='add-remote-url']");
      await jsSetValue(urlInput, barePath);

      // Set a name
      const nameInput = await $("[data-testid='add-remote-name']");
      await jsSetValue(nameInput, "mirror");

      // Click confirm
      const confirmBtn = await $("[data-testid='add-remote-confirm']");
      await jsClick(confirmBtn);
      await sleep(1500);

      // Verify dialog closed
      const dialog = await $("[data-testid='add-remote-dialog']");
      expect(await dialog.isExisting()).toBe(false);
    });

    it("dropdown now shows both remotes", async () => {
      const toggle = await $("[data-testid='remote-dropdown-toggle']");
      await jsClick(toggle);
      await sleep(300);

      const originRow = await $("[data-testid='remote-row-origin']");
      expect(await originRow.isExisting()).toBe(true);

      const mirrorRow = await $("[data-testid='remote-row-mirror']");
      expect(await mirrorRow.isExisting()).toBe(true);

      await jsClick(toggle);
      await sleep(200);
    });

    it("can remove the second remote via confirm flow", async () => {
      const toggle = await $("[data-testid='remote-dropdown-toggle']");
      await jsClick(toggle);
      await sleep(300);

      // Click the trash icon for mirror
      const removeBtn = await $("[data-testid='remove-remote-mirror']");
      await jsClick(removeBtn);
      await sleep(200);

      // Confirm removal
      const confirmBtn = await $("[data-testid='remove-remote-confirm-mirror']");
      expect(await confirmBtn.isExisting()).toBe(true);
      await jsClick(confirmBtn);
      await sleep(500);

      // Mirror row should be gone
      const mirrorRow = await $("[data-testid='remote-row-mirror']");
      expect(await mirrorRow.isExisting()).toBe(false);

      // Origin should still exist
      const originRow = await $("[data-testid='remote-row-origin']");
      expect(await originRow.isExisting()).toBe(true);

      // Close
      await jsClick(toggle);
      await sleep(200);
    });
  });

  describe("Duplicate remote name validation", () => {
    before(async () => {
      await setupTest();
      await sleep(500);
    });

    it("shows error when entering a name that already exists", async () => {
      const toggle = await $("[data-testid='remote-dropdown-toggle']");
      await jsClick(toggle);
      await sleep(300);

      const addBtn = await $("[data-testid='add-remote-button']");
      await jsClick(addBtn);
      await sleep(300);

      const nameInput = await $("[data-testid='add-remote-name']");
      await jsSetValue(nameInput, "origin");

      const error = await $("[data-testid='add-remote-name-error']");
      expect(await error.isExisting()).toBe(true);
      const errorText = await getTestIdText("add-remote-name-error");
      expect(errorText.toLowerCase()).toContain("already exists");

      const cancelBtn = await $("[data-testid='add-remote-cancel']");
      await jsClick(cancelBtn);
      await sleep(200);
    });
  });
});
