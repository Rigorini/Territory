import type { AdminProfile } from './auth.service';
import { SUPPORTED_LANGUAGES, type LanguageService } from './language.service';
import { THEMES, type ThemeService } from './theme.service';

/** Applies the theme and language saved on the profile; both services mirror them into the local storage. */
export function applyProfilePreferences(profile: AdminProfile, theme: ThemeService, language: LanguageService): void {
  theme.set(THEMES[profile.theme] ?? 'system');
  const saved = SUPPORTED_LANGUAGES.find(l => l === profile.language);
  if (saved) {
    language.use(saved);
  }
}
