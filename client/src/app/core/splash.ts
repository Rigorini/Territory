/**
 * Bridge to the static splash screen defined in index.html and public/splash.js.
 * The splash must exist before Angular renders, so it is plain HTML/CSS/JS rather than a component.
 */
export interface TerritorySplash {
  hide(): void;
  fail(): void;
}

declare global {
  interface Window {
    __territorySplash?: TerritorySplash;
  }
}

/** Fades the splash out and removes it from the DOM. */
export function hideSplash(): void {
  const splash = window.__territorySplash;
  if (splash) {
    splash.hide();
    return;
  }
  // The deferred splash script always runs before the Angular bundle, but retry once in case
  // the script was blocked (for example by an aggressive content blocker).
  setTimeout(() => window.__territorySplash?.hide(), 250);
}

/** Turns the splash into an error state with a reload button. */
export function failSplash(): void {
  window.__territorySplash?.fail();
}

let publicViewShown = false;
let resolvePublicView: (() => void) | undefined;

/**
 * Waits until the public link page has its map on screen (or an error/empty state), so the
 * splash covers the API call, the map bundle and the first tiles instead of a bare spinner.
 * Resolves anyway after the timeout: the page has its own loading and retry states.
 */
export function whenPublicViewReady(timeoutMs: number): Promise<void> {
  if (publicViewShown) {
    return Promise.resolve();
  }
  const shown = new Promise<void>(resolve => (resolvePublicView = resolve));
  const timeout = new Promise<void>(resolve => setTimeout(resolve, timeoutMs));
  return Promise.race([shown, timeout]);
}

/** Called by the public link page once its map (or its error state) is on screen. */
export function publicViewReady(): void {
  publicViewShown = true;
  resolvePublicView?.();
  resolvePublicView = undefined;
}
