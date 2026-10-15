import { TestBed } from '@angular/core/testing';
import { provideTranslateService } from '@ngx-translate/core';
import { LanguageService, resolveLanguage } from './language.service';

describe('LanguageService', () => {
  it('applies the language to translations, document and signal', () => {
    TestBed.configureTestingModule({ providers: [provideTranslateService()] });
    const service = TestBed.inject(LanguageService);

    service.use('it');

    expect(service.active()).toBe('it');
    expect(service.current).toBe('it');
    expect(document.documentElement.lang).toBe('it');
  });
});

describe('resolveLanguage', () => {
  it('returns the first supported browser language', () => {
    expect(resolveLanguage(['de-DE', 'it-IT', 'en-US'])).toBe('it');
  });

  it('matches regardless of case and region', () => {
    expect(resolveLanguage(['EN-gb'])).toBe('en');
  });

  it('falls back to English when nothing is supported', () => {
    expect(resolveLanguage(['fr-FR', 'es'])).toBe('en');
  });

  it('falls back to English for an empty list', () => {
    expect(resolveLanguage([])).toBe('en');
  });
});
