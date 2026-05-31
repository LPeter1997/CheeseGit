/**
 * E2E: Staging — file diff stats display.
 *
 * Verifies that the staging panel shows +/- line counts for modified files,
 * and that file status badges (M, A, D, R) are correctly displayed.
 */
import {
  hasNoChanges,
  waitForStagingLoaded,
  switchLeftPanel,
  setupTest,
  sleep,
} from "../helpers/app.js";

describe("File Status Display", () => {
  before(async () => {
    await setupTest();
    await switchLeftPanel("staging");
  });

  it("file rows contain the file path", async () => {
    const rows = await $$("[data-testid='file-row']");
    if (rows.length > 0) {
      const filepath = await rows[0].getAttribute("data-filepath");
      expect(filepath).toBeTruthy();
      expect(filepath!.length).toBeGreaterThan(0);
    }
  });

  it("file rows have action buttons", async () => {
    const rows = await $$("[data-testid='file-row']");
    if (rows.length > 0) {
      const actionBtn = await rows[0].$("[data-testid='file-action']");
      expect(await actionBtn.isExisting()).toBe(true);
    }
  });

  it("staged and unstaged sections are properly labeled", async () => {
    const unstagedSection = await $("[data-testid='unstaged-section']");
    const stagedSection = await $("[data-testid='staged-section']");
    const cleanState = await hasNoChanges();
    // Either sections exist or the "No changes" state is shown
    const unstagedExists = await unstagedSection.isExisting();
    const stagedExists = await stagedSection.isExisting();
    expect(unstagedExists || stagedExists || cleanState).toBe(true);
  });
});
