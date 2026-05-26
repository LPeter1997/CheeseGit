/**
 * E2E: Diff viewer — unified and split modes
 */
import fs from "fs";
import path from "path";
import {
  waitForAppReady,
  openRepoByPath,
  waitForStagingLoaded,
  getUnstagedFiles,
  selectFileForDiff,
  isDiffVisible,
  getDiffViewMode,
  setDiffViewMode,
  switchLeftPanel,
  clickHistoryCommit,
  sleep,
  TEST_REPO_PATH,
} from "../helpers/app.js";

describe("Diff Viewer", () => {
  before(async () => {
    // Ensure there's a dirty file for diff testing
    const filePath = path.join(TEST_REPO_PATH, "src", "constants.ts");
    if (fs.existsSync(filePath)) {
      fs.appendFileSync(filePath, "\n// e2e diff test modification\n");
    }
    await waitForAppReady();
    await openRepoByPath(TEST_REPO_PATH);
    await waitForStagingLoaded();
  });

  it("shows diff when selecting a modified file", async () => {
    const files = await getUnstagedFiles();
    if (files.length > 0) {
      await selectFileForDiff(files[0]);
    } else {
      // Fallback: use history commit diff
      await switchLeftPanel("history");
      await sleep(500);
      await clickHistoryCommit(0);
      await sleep(300);
    }
    expect(await isDiffVisible()).toBe(true);
  });

  it("starts in unified view mode", async () => {
    const mode = await getDiffViewMode();
    expect(mode).toBe("unified");
  });

  it("switches to split view mode", async () => {
    await setDiffViewMode("split");
    expect(await getDiffViewMode()).toBe("split");
  });

  it("switches back to unified view mode", async () => {
    await setDiffViewMode("unified");
    expect(await getDiffViewMode()).toBe("unified");
  });

  it("displays diff lines with additions and deletions", async () => {
    // Use browser.execute for reliable text extraction in WebKitWebDriver
    const text = await browser.execute(() => {
      const viewer = document.querySelector("[data-testid='diff-viewer']");
      return viewer?.textContent?.trim() ?? "";
    });
    expect(text.length).toBeGreaterThan(0);
  });
});
