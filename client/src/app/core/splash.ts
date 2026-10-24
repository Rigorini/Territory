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
