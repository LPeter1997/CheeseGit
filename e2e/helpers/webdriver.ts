/**
 * WebDriver polyfills for WebKitWebDriver (webkitgtk6.0 ≥ 2.52)
 *
 * WebKitWebDriver does not support several W3C WebDriver endpoints,
 * returning "unsupported operation" for Element Click, moveTo, setValue,
 * clearValue, and the Actions API (browser.keys).
 *
 * This module provides JS-execution-based workarounds that use the
 * supported "Execute Script" endpoint instead.
 */

import type { Element } from "webdriverio";

// ── Click ───────────────────────────────────────────────────────────────

/** Click an element via JavaScript instead of WebDriver's Element Click. */
export async function jsClick(el: Element) {
  await browser.execute((domEl: any) => {
    (domEl as HTMLElement).click();
  }, el);
}

/** Click with optional keyboard modifiers for list selection semantics. */
export async function jsClickWithModifiers(
  el: Element,
  modifiers: { shiftKey?: boolean; ctrlKey?: boolean; metaKey?: boolean },
) {
  await browser.execute((domEl: any, m: { shiftKey?: boolean; ctrlKey?: boolean; metaKey?: boolean }) => {
    const target = domEl as HTMLElement;
    target.dispatchEvent(new MouseEvent("click", {
      bubbles: true,
      cancelable: true,
      shiftKey: !!m.shiftKey,
      ctrlKey: !!m.ctrlKey,
      metaKey: !!m.metaKey,
    }));
  }, el, modifiers);
}

/** Ctrl-click helper for multi-select toggling on Linux/Windows. */
export async function jsCtrlClick(el: Element) {
  await jsClickWithModifiers(el, { ctrlKey: true });
}

/** Shift-click helper that carries shiftKey on the click event. */
export async function jsShiftRangeClick(el: Element) {
  await jsClickWithModifiers(el, { shiftKey: true });
}

/** Shift-click: dispatch Shift keydown, wait for React re-render, click, keyup. */
export async function jsShiftClick(el: Element) {
  // Press shift first and let React update shiftHeld state
  await browser.execute(() => {
    window.dispatchEvent(new KeyboardEvent("keydown", { key: "Shift", bubbles: true }));
  });
  await browser.pause(100);
  // Now click while shift is held
  await browser.execute((domEl: any) => {
    (domEl as HTMLElement).click();
  }, el);
  // Release shift
  await browser.execute(() => {
    window.dispatchEvent(new KeyboardEvent("keyup", { key: "Shift", bubbles: true }));
  });
}

// ── Hover ───────────────────────────────────────────────────────────────

/** Dispatch mouseover/mouseenter on an element (replaces Element moveTo). */
export async function jsMoveTo(el: Element) {
  await browser.execute((domEl: any) => {
    const e = domEl as HTMLElement;
    e.dispatchEvent(new MouseEvent("mouseover", { bubbles: true }));
    e.dispatchEvent(new MouseEvent("mouseenter", { bubbles: true }));
  }, el);
}

// ── Input values ────────────────────────────────────────────────────────

/** Set an input/textarea value via JavaScript (replaces Element setValue). */
export async function jsSetValue(el: Element, value: string) {
  await browser.execute(
    (domEl: any, val: string) => {
      const e = domEl as HTMLInputElement;
      // Pick the correct prototype setter based on element type.
      const proto = e instanceof HTMLTextAreaElement
        ? window.HTMLTextAreaElement.prototype
        : window.HTMLInputElement.prototype;
      const nativeSet = Object.getOwnPropertyDescriptor(proto, "value")?.set;
      nativeSet?.call(e, val);
      e.dispatchEvent(new Event("input", { bubbles: true }));
      e.dispatchEvent(new Event("change", { bubbles: true }));
    },
    el,
    value,
  );
}

/** Clear an input/textarea value via JavaScript (replaces Element clearValue). */
export async function jsClearValue(el: Element) {
  await jsSetValue(el, "");
}

// ── Keyboard ────────────────────────────────────────────────────────────

/** Dispatch a keyboard event via JavaScript (replaces browser.keys). */
export async function jsKeys(key: string) {
  await browser.execute((k: string) => {
    document.activeElement?.dispatchEvent(
      new KeyboardEvent("keydown", { key: k, bubbles: true }),
    );
    document.activeElement?.dispatchEvent(
      new KeyboardEvent("keyup", { key: k, bubbles: true }),
    );
  }, key);
}

// ── Timing helpers ──────────────────────────────────────────────────────

/** Wait for an element to appear, default 10 s. */
export async function waitFor(selector: string, ms = 10_000) {
  const el = await $(selector);
  await el.waitForExist({ timeout: ms });
  return el;
}

/** Short sleep (prefer waitFor when possible). */
export function sleep(ms: number) {
  return browser.pause(ms);
}
