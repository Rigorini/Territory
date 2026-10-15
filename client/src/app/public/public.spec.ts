import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { heldDuration, pluralKey } from './held-duration';
import { PublicApi, SHARE_TOKEN_HEADER } from './public.api';

describe('heldDuration', () => {
  it.each([
    [new Date(2025, 2, 15, 9), new Date(2025, 2, 15, 18), 0, 0],
    [new Date(2025, 2, 15), new Date(2025, 3, 15), 1, 0],
    [new Date(2025, 2, 15), new Date(2025, 5, 20), 3, 5],
    [new Date(2025, 0, 31), new Date(2025, 1, 28), 1, 0],
    [new Date(2025, 0, 31), new Date(2025, 1, 27), 0, 27],
    [new Date(2024, 0, 31), new Date(2024, 1, 29), 1, 0],
    [new Date(2024, 10, 10), new Date(2025, 0, 5), 1, 26],
    [new Date(2025, 2, 20, 23, 30), new Date(2025, 3, 5, 0, 30), 0, 16],
    [new Date(2023, 4, 1), new Date(2025, 4, 1), 24, 0],
  ])('from %s to %s is %i months and %i days', (from, to, months, days) => {
    expect(heldDuration(from, to)).toEqual({ months, days });
  });

  it('never goes negative', () => {
    expect(heldDuration(new Date(2025, 5, 10), new Date(2025, 5, 1))).toEqual({ months: 0, days: 0 });
  });
});

describe('pluralKey', () => {
  it.each([
    ['it', 1, 'public.days.one'],
    ['it', 0, 'public.days.other'],
    ['it', 5, 'public.days.other'],
    ['en', 1, 'public.days.one'],
    ['en', 2, 'public.days.other'],
  ])('%s %i → %s', (language, count, key) => {
    expect(pluralKey('public.days', count, language)).toBe(key);
  });
});

describe('PublicApi', () => {
  let api: PublicApi;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    api = TestBed.inject(PublicApi);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('sends the token in a header, never in the URL', async () => {
    const territory = api.territory('tok');
    const map = api.map('tok');
    const returned = api.returnTerritory('tok');

    for (const [method, url] of [['GET', '/api/public/territory'], ['GET', '/api/public/territory/map'], ['POST', '/api/public/territory/return']]) {
      const request = http.expectOne({ method, url });
      expect(request.request.headers.get(SHARE_TOKEN_HEADER)).toBe('tok');
      expect(request.request.urlWithParams).not.toContain('tok');
      request.flush({});
    }

    await expect(Promise.all([territory, map, returned])).resolves.toEqual([{}, {}, {}]);
  });
});
