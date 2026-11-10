import { DOCUMENT, Injectable, effect, inject, signal } from '@angular/core';

export type ThemePreference = 'system' | 'light' | 'dark';

const STORAGE_KEY = 'territory.theme';
/** Ordered as the server enum (0=system, 1=light, 2=dark). */
export const THEMES: readonly ThemePreference[] = ['system', 'light', 'dark'];

@Injectable({ providedIn: 'root' })
export class ThemeService {
  private readonly document = inject(DOCUMENT);
  readonly preference = signal<ThemePreference>(this.readStored());

  constructor() {
    effect(() => this.apply(this.preference()));
  }

  set(theme: ThemePreference): void {
    this.preference.set(theme);
    try {
      localStorage.setItem(STORAGE_KEY, theme);
    } catch {
      // Storage unavailable (e.g. private mode): the theme still applies for this session.
    }
  }

  private apply(theme: ThemePreference): void {
    const root = this.document.documentElement;
    root.classList.toggle('theme-light', theme === 'light');
    root.classList.toggle('theme-dark', theme === 'dark');

    const dark =
      theme === 'dark' ||
      (theme === 'system' && !!this.document.defaultView?.matchMedia?.('(prefers-color-scheme: dark)').matches);
    this.document.querySelector('meta[name="theme-color"]')?.setAttribute('content', dark ? '#0d1020' : '#0a2a6b');
  }

  private readStored(): ThemePreference {
    try {
      const stored = localStorage.getItem(STORAGE_KEY);
      return THEMES.find(t => t === stored) ?? 'system';
    } catch {
      return 'system';
    }
  }
}
