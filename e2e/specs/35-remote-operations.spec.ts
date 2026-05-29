/**
 * E2E: Remote Operations — verify the remote operations UI renders correctly
 * and handles the no-remote case gracefully.
 *
 * The test repository has no remote configured, so the RemoteButton component
 * is not rendered (it returns null when remotes.length === 0). These tests
 * verify the no-remote UI state and the branch bar layout.
 */
import {
  waitForAppReady,
  openRepoByPath,
  getCurrentBranch,
  getBranchList,
  TEST_REPO_PATH,
} from "../helpers/app.js";

describe("Remote Operations", () => {
  before(async () => {
    await waitForAppReady();
    await openRepoByPath(TEST_REPO_PATH);
  });

  describe("No-Remote State", () => {
    it("starts on main branch", async () => {
      const branch = await getCurrentBranch();
      expect(branch).toBe("main");
    });

    it("remote button is not rendered when no remotes are configured", async () => {
      // The test repo has no remote — RemoteButton returns null in this case.
      const btn = await $("[data-testid='remote-button']");
      expect(await btn.isExisting()).toBe(false);
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

  describe("Branch List", () => {
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

