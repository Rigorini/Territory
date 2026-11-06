import { HttpClient } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { Feedback } from '../shared/feedback';
import { AdminProfile, AuthService } from './auth.service';
import { Language, LanguageService } from './language.service';
import { applyProfilePreferences } from './profile-preferences';
import { THEMES, ThemePreference, ThemeService } from './theme.service';

/**
 * Theme and language of the logged-in admin: applied immediately on this device and saved on the profile,
 * so they follow the admin on every device. After login the saved profile wins over the local choice.
 */
@Injectable({ providedIn: 'root' })
export class PreferencesService {
  private readonly http = inject(HttpClient);
  private readonly auth = inject(AuthService);
  private readonly theme = inject(ThemeService);
  private readonly language = inject(LanguageService);
  private readonly feedback = inject(Feedback);

  applyProfile(profile: AdminProfile): void {
    applyProfilePreferences(profile, this.theme, this.language);
  }

  setTheme(theme: ThemePreference): Promise<void> {
    this.theme.set(theme);
    return this.save();
  }

  setLanguage(language: Language): Promise<void> {
    this.language.use(language);
    return this.save();
  }

  private async save(): Promise<void> {
    const body = { theme: THEMES.indexOf(this.theme.preference()), language: this.language.active() };
    try {
      this.auth.updateProfile(await firstValueFrom(this.http.put<AdminProfile>('/api/auth/me/preferences', body)));
    } catch (error) {
      // The choice still applies locally; only its persistence failed.
      this.feedback.notifyError(error);
    }
  }
}
