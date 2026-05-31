/**
 * E2E: Scroll-clamp regression test — staging/unstaging the bottom-most file
 * should never leave the user "below" the remaining items.
 *
 * Reproduces the bug where repeatedly staging files from the bottom of the
 * list would cause the scroll position to exceed the new content height,
 * making remaining files invisible until the user switches tabs.
 */
import fs from "fs";
import path from "path";
import {
  waitForAppReady,
  openRepoByPath,
  closeAllTabs,
  waitForStagingLoaded,
  getUnstagedFiles,
  getStagedFiles,
  resetTestRepo,
  sleep,
  TEST_REPO_PATH,
} from "../helpers/app.js";

const EXTRA_FILES_DIR = "scroll-test-files";

/** Create N files in the test repo so the unstaged list overflows. */
function createExtraFiles(count: number) {
  const dir = path.join(TEST_REPO_PATH, EXTRA_FILES_DIR);
  fs.mkdirSync(dir, { recursive: true });
  for (let i = 0; i < count; i++) {
    const name = `file-${String(i).padStart(3, "0")}.txt`;
    fs.writeFileSync(path.join(dir, name), `content ${i}\n`);
  }
}

/** Remove the extra files directory. */
function cleanupExtraFiles() {
  const dir = path.join(TEST_REPO_PATH, EXTRA_FILES_DIR);
  if (fs.existsSync(dir)) {
    fs.rmSync(dir, { recursive: true, force: true });
  }
}

/** Returns { scrollTop, maxScroll, isOverflowing } for the unstaged section. */
async function getUnstagedScrollInfo(): Promise<{
  scrollTop: number;
  maxScroll: number;
  isOverflowing: boolean;
}> {
  return browser.execute(() => {
    const section = document.querySelector("[data-testid='unstaged-section']");
    if (!section) return { scrollTop: 0, maxScroll: 0, isOverflowing: false };
    const el = section as HTMLElement;
    const maxScroll = el.scrollHeight - el.clientHeight;
    return {
      scrollTop: el.scrollTop,
      maxScroll,
      isOverflowing: maxScroll > 0,
    };
  });
}

/** Returns { scrollTop, maxScroll, isOverflowing } for the staged section. */
async function getStagedScrollInfo(): Promise<{
  scrollTop: number;
  maxScroll: number;
  isOverflowing: boolean;
}> {
  return browser.execute(() => {
    const section = document.querySelector("[data-testid='staged-section']");
    if (!section) return { scrollTop: 0, maxScroll: 0, isOverflowing: false };
    const el = section as HTMLElement;
    const maxScroll = el.scrollHeight - el.clientHeight;
    return {
      scrollTop: el.scrollTop,
      maxScroll,
      isOverflowing: maxScroll > 0,
    };
  });
}

/** Scroll the unstaged section to the very bottom. */
async function scrollUnstagedToBottom() {
  await browser.execute(() => {
    const section = document.querySelector("[data-testid='unstaged-section']");
    if (section) {
      const el = section as HTMLElement;
      el.scrollTop = el.scrollHeight;
    }
  });
  await sleep(100);
}

/** Scroll the staged section to the very bottom. */
async function scrollStagedToBottom() {
  await browser.execute(() => {
    const section = document.querySelector("[data-testid='staged-section']");
    if (section) {
      const el = section as HTMLElement;
      el.scrollTop = el.scrollHeight;
    }
  });
  await sleep(100);
}

/** Stage the last visible file in the unstaged section by clicking its action button. */
async function stageLastUnstagedFile() {
  await browser.execute(() => {
    const section = document.querySelector("[data-testid='unstaged-section']");
    if (!section) return;
    const rows = section.querySelectorAll("[data-testid='file-row']");
    const lastRow = rows[rows.length - 1];
    if (!lastRow) return;
    const btn = lastRow.querySelector("[data-testid='file-action']") as HTMLElement | null;
    btn?.click();
  });
  await sleep(400);
}

/** Unstage the last visible file in the staged section by clicking its action button. */
async function unstageLastStagedFile() {
  await browser.execute(() => {
    const section = document.querySelector("[data-testid='staged-section']");
    if (!section) return;
    const rows = section.querySelectorAll("[data-testid='file-row']");
    const lastRow = rows[rows.length - 1];
    if (!lastRow) return;
    const btn = lastRow.querySelector("[data-testid='file-action']") as HTMLElement | null;
    btn?.click();
  });
  await sleep(400);
}

describe("Scroll Clamp on Staging/Unstaging", () => {
  before(async () => {
    await waitForAppReady();
    await closeAllTabs();
    await sleep(500);
    resetTestRepo();
    // Create extra files AFTER reset but BEFORE opening
    createExtraFiles(25);
    await openRepoByPath(TEST_REPO_PATH);
    await waitForStagingLoaded();
    // Wait for polling to detect the new files
    await sleep(3000);
  });

  it("unstaged section overflows with many files", async () => {
    const files = await getUnstagedFiles();
    expect(files.length).toBeGreaterThanOrEqual(20);
    const info = await getUnstagedScrollInfo();
    expect(info.isOverflowing).toBe(true);
  });

  it("scroll stays clamped when staging bottom-most files repeatedly", async () => {
    // Scroll to bottom, stage the last file, repeat several times
    for (let i = 0; i < 8; i++) {
      await scrollUnstagedToBottom();
      const beforeInfo = await getUnstagedScrollInfo();
      // Sanity: we scrolled near the bottom
      expect(beforeInfo.scrollTop).toBeGreaterThan(0);

      await stageLastUnstagedFile();

      // After staging, scrollTop must not exceed the new max
      const afterInfo = await getUnstagedScrollInfo();
      expect(afterInfo.scrollTop).toBeLessThanOrEqual(
        afterInfo.maxScroll + 1, // +1 for rounding tolerance
      );
    }
  });

  it("remaining files are accessible after staging from bottom", async () => {
    // Verify files are still present and the section is not "stuck"
    const files = await getUnstagedFiles();
    expect(files.length).toBeGreaterThan(0);

    // The first file should be reachable by scrolling to top
    await browser.execute(() => {
      const section = document.querySelector("[data-testid='unstaged-section']");
      if (section) (section as HTMLElement).scrollTop = 0;
    });
    await sleep(100);

    // Verify the first row is visible
    const firstRowVisible = await browser.execute(() => {
      const section = document.querySelector("[data-testid='unstaged-section']");
      if (!section) return false;
      const rows = section.querySelectorAll("[data-testid='file-row']");
      if (rows.length === 0) return false;
      const rect = rows[0].getBoundingClientRect();
      const sectionRect = section.getBoundingClientRect();
      return rect.top >= sectionRect.top && rect.top < sectionRect.bottom;
    });
    expect(firstRowVisible).toBe(true);
  });

  it("scroll stays clamped when unstaging bottom-most files repeatedly", async () => {
    // The staged section should now have files from the staging operations above
    const staged = await getStagedFiles();
    expect(staged.length).toBeGreaterThanOrEqual(8);

    // Scroll staged section to bottom and unstage from the bottom repeatedly
    for (let i = 0; i < 5; i++) {
      await scrollStagedToBottom();
      await unstageLastStagedFile();

      const afterInfo = await getStagedScrollInfo();
      expect(afterInfo.scrollTop).toBeLessThanOrEqual(
        afterInfo.maxScroll + 1,
      );
    }
  });
});
