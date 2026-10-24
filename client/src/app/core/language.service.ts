import { DOCUMENT, Injectable, inject, signal } from '@angular/core';
import { TranslateService } from '@ngx-translate/core';

export const SUPPORTED_LANGUAGES = ['en', 'it'] as const;
export type Language = (typeof SUPPORTED_LANGUAGES)[number];
export const FALLBACK_LANGUAGE: Language = 'en';

/** Picks the first browser language we support, falling back to English. */
export function resolveLanguage(browserLanguages: readonly string[]): Language {
  for (const candidate of browserLanguages) {
    const base = candidate.toLowerCase().split('-')[0];
    const match = SUPPORTED_LANGUAGES.find(l => l === base);
    if (match) {
      return match;
    }
  }
  return FALLBACK_LANGUAGE;
}

@Injectable({ providedIn: 'root' })
export class LanguageService {
  private readonly translate = inject(TranslateService);
  private readonly document = inject(DOCUMENT);
  private readonly activeLanguage = signal<Language>(FALLBACK_LANGUAGE);
  private pendingLoad: Promise<void> = Promise.resolve();

  /** Reactive current language, e.g. for locale-aware formatting. */
  readonly active = this.activeLanguage.asReadonly();

  init(): void {
    this.use(resolveLanguage(navigator.languages ?? [navigator.language]));
  }

  use(language: Language): void {
    this.document.documentElement.lang = language;
    this.activeLanguage.set(language);
    this.pendingLoad = new Promise<void>(resolve => {
      try {
        // The splash screen waits for this before revealing the app, so the first screen
        // never flashes raw translation keys. A failed request must not block startup.
        this.translate.use(language).subscribe({
          next: () => resolve(),
          error: () => resolve(),
          complete: () => resolve(),
        });
      } catch {
        resolve();
      }
    });
  }

  /** Resolves when the translations of the most recently selected language loaded (or failed). */
  whenReady(): Promise<void> {
    return this.pendingLoad;
  }

  get current(): string | null {
    return this.translate.getCurrentLang();
  }
}
