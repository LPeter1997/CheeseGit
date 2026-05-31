/**
 * E2E: Remote Operations — verify the remote operations UI renders correctly.
 *
 * The test repository has a local bare clone configured as "origin" remote,
 * so the RemoteButton component is rendered. A second no-remote repo is
 * created on the fly to verify the button is hidden when no remotes exist.
 */
import { execSync } from "node:child_process";
import { mkdtempSync, writeFileSync } from "node:fs";
import { tmpdir } from "node:os";
import path from "node:path";
import {
  openRepoByPath,
  getCurrentBranch,
  getBranchList,
  getRemoteButtonText,
  setupTest,
  sleep,
  TEST_REPO_PATH,
} from "../helpers/app.js";

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

describe("Remote Operations", () => {
  describe("With Remote", () => {
    before(async () => {
      await setupTest();
    });

    it("starts on main branch", async () => {
      const branch = await getCurrentBranch();
      expect(branch).toBe("main");
    });

    it("remote button is rendered when a remote is configured", async () => {
      const btn = await $("[data-testid='remote-button']");
      expect(await btn.isExisting()).toBe(true);
    });

    it("branch selector is visible", async () => {
      const selector = await $("[data-testid='branch-selector']");
      expect(await selector.isExisting()).toBe(true);
    });

    it("repo view is shown (not welcome screen)", async () => {
      const repoView = await $("[data-testid='repo-view']");
      expect(await repoView.isExisting()).toBe(true);
    });
  });

  describe("Without Remote", () => {
    let noRemotePath: string;

    before(async () => {
      noRemotePath = createNoRemoteRepo();
      await openRepoByPath(noRemotePath);
      await sleep(500);
    });

    it("remote button shows 'Add remote' when no remotes are configured", async () => {
      const btn = await $("[data-testid='remote-button']");
      expect(await btn.isExisting()).toBe(true);
      const text = await getRemoteButtonText();
      expect(text.toLowerCase()).toContain("add remote");
    });

    it("branch selector is still visible", async () => {
      const selector = await $("[data-testid='branch-selector']");
      expect(await selector.isExisting()).toBe(true);
    });

    after(async () => {
      // No cleanup needed — next describe resets the repo.
    });
  });

  describe("Branch List", () => {
    before(async () => {
      await setupTest();
    });

    it("includes expected branches from the test repo", async () => {
      const branches = await getBranchList();
      expect(branches).toContain("main");
      expect(branches.some((b) => b.startsWith("feature/"))).toBe(true);
    });

    it("includes the conflict strategy test branches", async () => {
      const branches = await getBranchList();
      expect(branches).toContain("test/conflict-accept-current");
      expect(branches).toContain("test/conflict-accept-incoming");
      expect(branches).toContain("test/conflict-accept-both");
    });

    it("includes feature/dark-mode (unmerged branch)", async () => {
      const branches = await getBranchList();
      expect(branches).toContain("feature/dark-mode");
    });
  });
});
