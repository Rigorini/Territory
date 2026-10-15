import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideTranslateService } from '@ngx-translate/core';
import { AdminProfile, AuthService } from './auth.service';
import { LanguageService } from './language.service';
import { PreferencesService } from './preferences.service';
import { ThemeService } from './theme.service';
import { Feedback } from '../shared/feedback';

const profile: AdminProfile = { id: 1, username: 'admin', displayName: 'Mario Rossi', theme: 2, language: 'it', role: 1 };

describe('PreferencesService', () => {
  let service: PreferencesService;
  let http: HttpTestingController;
  let feedback: { notifyError: ReturnType<typeof vi.fn> };

  beforeEach(() => {
    localStorage.clear();
    feedback = { notifyError: vi.fn() };
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting(), provideTranslateService(), { provide: Feedback, useValue: feedback }],
    });
    service = TestBed.inject(PreferencesService);
    http = TestBed.inject(HttpTestingController);
    TestBed.inject(LanguageService).use('en');
  });

  afterEach(() => {
    http.verify();
    localStorage.clear();
  });

  it('applies the saved theme and language of the profile', () => {
    service.applyProfile(profile);

    expect(TestBed.inject(ThemeService).preference()).toBe('dark');
    expect(TestBed.inject(LanguageService).active()).toBe('it');
  });

  it('keeps the browser language and the system theme for unknown profile values', () => {
    service.applyProfile({ ...profile, theme: 9, language: null });

    expect(TestBed.inject(ThemeService).preference()).toBe('system');
    expect(TestBed.inject(LanguageService).active()).toBe('en');
  });

  it('saves the theme on the profile and caches the returned profile', async () => {
    const saving = service.setTheme('light');

    expect(TestBed.inject(ThemeService).preference()).toBe('light');
    const req = http.expectOne('/api/auth/me/preferences');
    expect(req.request.method).toBe('PUT');
    expect(req.request.body).toEqual({ theme: 1, language: 'en' });
    req.flush({ ...profile, theme: 1, language: 'en' });
    await saving;

    expect(TestBed.inject(AuthService).user()).toEqual({ ...profile, theme: 1, language: 'en' });
  });

  it('applies the language locally even when saving fails', async () => {
    const saving = service.setLanguage('it');

    http.expectOne('/api/auth/me/preferences').flush(null, { status: 500, statusText: 'Error' });
    await saving;

    expect(TestBed.inject(LanguageService).active()).toBe('it');
    expect(feedback.notifyError).toHaveBeenCalledOnce();
  });
});
