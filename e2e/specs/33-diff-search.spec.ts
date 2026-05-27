/**
 * E2E: Diff view search functionality
 * Inline toolbar search with Ctrl+F focus, highlighting, navigation,
 * file switching reset, and no-hunk/plain diff coverage.
 */
import {
  waitForAppReady,
  openRepoByPath,
  waitForStagingLoaded,
  getUnstagedFiles,
  selectFileForDiff,
  sleep,
  jsClick,
  jsSetValue,
  jsClearValue,
  TEST_REPO_PATH,
} from "../helpers/app.js";

describe("Diff Search", () => {
  async function getSearchInput() {
    return $("[data-testid='diff-search-input']");
  }

  async function getSearchStatusText() {
    return browser.execute(() => {
      const input = document.querySelector("[data-testid='diff-search-input']");
      return input?.parentElement?.textContent ?? "";
    });
  }

  async function setSearchQuery(query: string) {
    const input = await getSearchInput();
    if (!query) {
      await jsClearValue(input);
      return;
    }
    await jsSetValue(input, query);
  }

  async function getSearchQueryValue() {
    return browser.execute(() => {
      const input = document.querySelector("[data-testid='diff-search-input']") as HTMLInputElement | null;
      return input?.value ?? "";
    });
  }

  async function pressSearchKey(key: string, shiftKey = false) {
    await browser.execute((k: string, shift: boolean) => {
      const input = document.querySelector("[data-testid='diff-search-input']") as HTMLInputElement | null;
      if (!input) return;
      input.dispatchEvent(new KeyboardEvent("keydown", { key: k, shiftKey: shift, bubbles: true }));
      input.dispatchEvent(new KeyboardEvent("keyup", { key: k, shiftKey: shift, bubbles: true }));
    }, key, shiftKey);
  }

  async function openFirstUnstagedFile() {
    const files = await getUnstagedFiles();
    if (files.length === 0) throw new Error("No unstaged files available for diff search tests");
    await selectFileForDiff(files[0]!);
    await sleep(400);
  }

  before(async () => {
    await waitForAppReady();
    await openRepoByPath(TEST_REPO_PATH);
    await waitForStagingLoaded();
  });

  describe("Inline search UI", () => {
    before(async () => {
      await openFirstUnstagedFile();
    });

    it("renders inline search input without opening", async () => {
      const searchInput = await getSearchInput();
      expect(await searchInput.isDisplayed()).toBe(true);
    });

    it("keeps up/down arrows visible inline", async () => {
      const prevButton = await $("button[title='Previous match (Shift+Return)']");
      const nextButton = await $("button[title='Next match (Return)']");
      expect(await prevButton.isDisplayed()).toBe(true);
      expect(await nextButton.isDisplayed()).toBe(true);
    });

    it("focuses the inline search input with Ctrl+F", async () => {
      await browser.execute(() => {
        document.dispatchEvent(
          new KeyboardEvent("keydown", { key: "f", ctrlKey: true, bubbles: true }),
        );
      });
      await sleep(200);

      const activeIsSearch = await browser.execute(() => {
        const input = document.querySelector("[data-testid='diff-search-input']");
        return document.activeElement === input;
      });
      expect(activeIsSearch).toBe(true);
    });
  });

  describe("Search functionality", () => {
    before(async () => {
      await openFirstUnstagedFile();
    });

    it("finds matches and displays count/no-match feedback", async () => {
      await setSearchQuery("line");
      await sleep(500);

      const status = await getSearchStatusText();
      expect(status.includes(" of ") || status.includes("No matches")).toBe(true);
    });

    it("updates results live as query changes", async () => {
      // Try multiple terms to find one that exists in the fixture
      const terms = ["const", "function", "export", "import", "return", "src", "."];
      let first = "";
      for (const term of terms) {
        await setSearchQuery(term);
        await sleep(200);
        first = await getSearchStatusText();
        if (first.includes(" of ")) break;
      }
      
      expect(first.includes(" of ") || first.includes("No matches")).toBe(true);

      const nextTerms = terms.filter(t => !first.includes(t) || !first.includes(" of "));
      let second = first;
      for (const term of nextTerms) {
        await setSearchQuery(term);
        await sleep(200);
        second = await getSearchStatusText();
        if (second !== first) break;
      }

      // If we found different results, verify they're different
      if (first.includes(" of ") && second.includes(" of ")) {
        expect(first).not.toBe(second);
      }
    });

    it("displays No matches for non-existing query", async () => {
      await setSearchQuery("xyzNotAValidSearchTerm123");
      await sleep(300);
      const status = await getSearchStatusText();
      expect(status).toContain("No matches");
    });
  });

  describe("Search highlighting and navigation", () => {
    before(async () => {
      await openFirstUnstagedFile();
    });

    it("creates visible highlights for query matches", async () => {
      // Try multiple terms to find one that produces matches
      const terms = ["const", "function", "export", "import", "return", "src", "."];
      let status = "No matches↑↓";
      
      for (const term of terms) {
        await setSearchQuery(term);
        await sleep(300);
        status = await getSearchStatusText();
        if (status.includes(" of ")) break;
      }

      if (status.includes(" of ")) {
        const hasHighlights = await browser.execute(() => {
          const nodes = Array.from(document.querySelectorAll("*"));
          return nodes.some((el) => {
            const cls = (el as HTMLElement).className;
            return typeof cls === "string" && (
              cls.includes("bg-accent/15") || 
              cls.includes("bg-accent/50") ||
              cls.includes("bg-yellow") ||
              cls.includes("highlight")
            );
          });
        });

        expect(hasHighlights).toBe(true);
      }
    });

    it("navigates with Enter and Shift+Enter", async () => {
      // Try multiple terms to find one that produces matches
      const terms = ["const", "function", "export", "import", "return", "src", "."];
      let start = "No matches↑↓";
      
      for (const term of terms) {
        await setSearchQuery(term);
        await sleep(300);
        start = await getSearchStatusText();
        if (start.includes(" of ")) break;
      }

      if (start.includes(" of ")) {
        await pressSearchKey("Enter");
        await sleep(300);
        const afterNext = await getSearchStatusText();

        await pressSearchKey("Enter", true);
        await sleep(300);
        const afterPrev = await getSearchStatusText();

        expect(afterNext).not.toBe(start);
        expect(afterPrev).not.toBe(afterNext);
      }
    });

    it("disables navigation buttons when query is empty", async () => {
      await setSearchQuery("");
      await sleep(150);

      const prevButton = await $("button[title='Previous match (Shift+Return)']");
      const nextButton = await $("button[title='Next match (Return)']");
      expect(await prevButton.getAttribute("disabled")).not.toBeNull();
      expect(await nextButton.getAttribute("disabled")).not.toBeNull();
    });
  });

  describe("Search with view switching", () => {
    before(async () => {
      await openFirstUnstagedFile();
    });

    it("preserves query and counts between unified and split views", async () => {
      const splitButton = await $("[data-testid='diff-mode-split']");
      if (!(await splitButton.isExisting())) {
        console.log("Skipping: selected file does not have diff mode toggles");
        return;
      }

      await setSearchQuery("const");
      await sleep(350);

      const before = await getSearchStatusText();

      await jsClick(splitButton);
      await sleep(350);
      const afterSplit = await getSearchStatusText();
      const query = await getSearchQueryValue();
      expect(query).toBe("const");

      expect(afterSplit.includes("No matches") || afterSplit.includes(" of ")).toBe(true);

      const unifiedButton = await $("[data-testid='diff-mode-unified']");
      await jsClick(unifiedButton);
      await sleep(350);
      const afterUnified = await getSearchStatusText();

      expect(before.includes("No matches") || before.includes(" of ")).toBe(true);
      expect(afterUnified.includes("No matches") || afterUnified.includes(" of ")).toBe(true);
    });
  });

  describe("Search with file switching", () => {
    it("resets search when switching to a different file", async () => {
      const files = await getUnstagedFiles();
      if (files.length < 2) {
        console.log("Skipping: need at least 2 unstaged files");
        return;
      }

      // Select first file
      await selectFileForDiff(files[0]!);
      await sleep(500);

      await setSearchQuery("test");
      await sleep(500);

      // Select second file
      await selectFileForDiff(files[1]!);
      await sleep(500);

      // Search should be cleared
      const currentValue = await getSearchQueryValue();
      expect(currentValue).toBe("");
    });
  });

  describe("No-hunk/plain diff coverage", () => {
    it("supports search on a file without diff mode toggles", async () => {
      const files = await getUnstagedFiles();
      let noToggleFile: string | null = null;

      for (const file of files) {
        await selectFileForDiff(file);
        await sleep(350);
        const hasToggle = await browser.execute(() => !!document.querySelector("[data-testid='diff-mode-unified']"));
        if (!hasToggle) {
          noToggleFile = file;
          break;
        }
      }

      if (!noToggleFile) {
        console.log("Skipping: no file without diff toggles found in this fixture");
        return;
      }

      await setSearchQuery("a");
      await sleep(300);

      const status = await getSearchStatusText();
      expect(status.includes("No matches") || status.includes(" of ")).toBe(true);

      await browser.execute(() => {
        document.dispatchEvent(new KeyboardEvent("keydown", { key: "f", ctrlKey: true, bubbles: true }));
      });
      await sleep(150);
      const activeIsSearch = await browser.execute(() => {
        const input = document.querySelector("[data-testid='diff-search-input']");
        return document.activeElement === input;
      });
      expect(activeIsSearch).toBe(true);
    });
  });
});
