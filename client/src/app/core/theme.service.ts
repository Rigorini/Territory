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
    localStorage.setItem(STORAGE_KEY, theme);
  }

  private apply(theme: ThemePreference): void {
    const root = this.document.documentElement;
    root.classList.toggle('theme-light', theme === 'light');
    root.classList.toggle('theme-dark', theme === 'dark');
  }

  private readStored(): ThemePreference {
    const stored = localStorage.getItem(STORAGE_KEY);
    return THEMES.find(t => t === stored) ?? 'system';
  }
}
