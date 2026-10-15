import { TestBed } from '@angular/core/testing';
import { ThemeService } from './theme.service';

describe('ThemeService', () => {
  const root = () => document.documentElement;

  beforeEach(() => {
    localStorage.clear();
    root().classList.remove('theme-light', 'theme-dark');
  });

  function create(): ThemeService {
    const service = TestBed.inject(ThemeService);
    TestBed.tick();
    return service;
  }

  it('defaults to the system theme without forcing a class', () => {
    const service = create();

    expect(service.preference()).toBe('system');
    expect(root().classList.contains('theme-light')).toBe(false);
    expect(root().classList.contains('theme-dark')).toBe(false);
  });

  it('applies and persists an explicit theme', () => {
    const service = create();

    service.set('dark');
    TestBed.tick();

    expect(root().classList.contains('theme-dark')).toBe(true);
    expect(localStorage.getItem('territory.theme')).toBe('dark');
  });

  it('restores a stored theme and ignores invalid values', () => {
    localStorage.setItem('territory.theme', 'light');
    expect(create().preference()).toBe('light');

    TestBed.resetTestingModule();
    localStorage.setItem('territory.theme', '<script>');
    expect(create().preference()).toBe('system');
  });
});
