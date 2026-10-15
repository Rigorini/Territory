import { FormControl } from '@angular/forms';
import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { FormGroup } from '@angular/forms';
import {
  correctedPeriod, dayDate, dayRangeValidator, eventInstant, localDay, MIN_EVENT_DAY, minDayAfter, periodEndValidator,
  startOfToday,
} from './event-date';
import { shareUrl, telegramUrl, whatsAppUrl } from './share-links';
import { AssignmentsApi } from './assignments.api';

describe('event dates', () => {
  it('formats the local day', () => {
    expect(localDay(new Date(2025, 0, 5, 23, 30))).toBe('2025-01-05');
  });

  it('converts a yyyy-MM-dd day to local midnight', () => {
    expect(dayDate('2025-03-04')).toEqual(new Date(2025, 2, 4));
    expect(startOfToday(new Date(2025, 2, 4, 18, 45))).toEqual(new Date(2025, 2, 4));
  });

  it('sends the end of a past day, or now for today', () => {
    const now = new Date(2025, 5, 10, 15, 0, 0);

    expect(eventInstant(new Date(2025, 5, 8), now)).toBe(new Date(2025, 5, 8, 23, 59, 59).toISOString());
    expect(eventInstant(new Date(2025, 5, 10), now)).toBe(now.toISOString());
  });

  it.each([
    [null, { required: true }],
    [new Date(2025, 5, 11), { inFuture: true }],
    [new Date(2025, 5, 1), { tooEarly: true }],
    [new Date(2025, 5, 5), null],
    [new Date(2025, 5, 10, 23, 59), null],
  ])('validates %s between the minimum and today', (day, expected) => {
    const validator = dayRangeValidator(new Date(2025, 5, 5, 12), 'tooEarly', () => new Date(2025, 5, 10, 8));

    expect(validator(new FormControl(day))).toEqual(expected);
  });

  it('uses today by default', () => {
    expect(dayRangeValidator(dayDate(MIN_EVENT_DAY), 'x')(new FormControl(startOfToday()))).toBeNull();
  });

  it('starts after the previous event, never before the server minimum', () => {
    expect(minDayAfter(null)).toEqual(dayDate(MIN_EVENT_DAY));
    expect(minDayAfter('1990-05-01T10:00:00Z')).toEqual(dayDate(MIN_EVENT_DAY));
    const previous = new Date(2025, 2, 4, 12).toISOString();
    expect(minDayAfter(previous)).toEqual(new Date(2025, 2, 4));
  });

  it.each([
    [new Date(2025, 5, 5), null, null],
    [new Date(2025, 5, 3), null, { early: true }],
    [new Date(2025, 5, 9), '2025-06-08', { late: true }],
    [new Date(2025, 5, 8), '2025-06-08', null],
    [new Date(2025, 5, 11), '2025-06-20', { inFuture: true }],
  ])('validates the period end %s against the start and %s', (end, latest, expected) => {
    const now = () => new Date(2025, 5, 10, 8);
    const form = new FormGroup({
      start: new FormControl<Date | null>(new Date(2025, 5, 4)),
      end: new FormControl<Date | null>(end, periodEndValidator('start', 'early', latest, 'late', now)),
    });
    form.controls.end.updateValueAndValidity();

    expect(form.controls.end.errors).toEqual(expected);
  });

  it('accepts any end from the server minimum when the start is missing', () => {
    const control = new FormControl<Date | null>(new Date(2001, 0, 1), periodEndValidator('start', 'early', null, 'late'));

    expect(control.errors).toBeNull();
  });

  describe('corrected period', () => {
    const now = new Date(2025, 5, 10, 15);
    const original = { assignedAt: '2025-06-02T10:00:00.123456+00:00', returnedAt: '2025-06-06T10:00:00.654321+00:00' };
    const noBounds = { notBefore: null, notAfter: null };
    const day = (value: string) => dayDate(localDay(new Date(value)));

    it('keeps unchanged days with their exact instant', () => {
      const days = { assignedAt: day(original.assignedAt), returnedAt: day(original.returnedAt) };

      expect(correctedPeriod(original, days, noBounds, now)).toEqual(original);
    });

    it('sends the end of changed days', () => {
      const days = { assignedAt: new Date(2025, 5, 1), returnedAt: new Date(2025, 5, 7) };

      expect(correctedPeriod(original, days, noBounds, now)).toEqual({
        assignedAt: new Date(2025, 5, 1, 23, 59, 59).toISOString(),
        returnedAt: new Date(2025, 5, 7, 23, 59, 59).toISOString(),
      });
    });

    it('stays within the neighbours and keeps the start before the end', () => {
      const bounds = { notBefore: '2025-06-01T23:59:59.999+00:00', notAfter: '2025-06-03T00:00:00.5+00:00' };
      const days = { assignedAt: new Date(2025, 4, 31), returnedAt: new Date(2025, 5, 3) };
      const result = correctedPeriod(original, days, { ...bounds, notBefore: null }, now);
      expect(result.returnedAt).toBe(bounds.notAfter);

      const sameDay = correctedPeriod(
        { assignedAt: '2025-06-02T10:00:00Z', returnedAt: '2025-06-02T11:00:00Z' },
        { assignedAt: day('2025-06-02T10:00:00Z'), returnedAt: day('2025-06-02T11:00:00Z') },
        { notBefore: '2025-06-02T12:00:00Z', notAfter: null }, now);
      expect(sameDay).toEqual({ assignedAt: '2025-06-02T11:00:00Z', returnedAt: '2025-06-02T11:00:00Z' });
    });

    it('raises the start to the previous return and leaves an open period open', () => {
      const result = correctedPeriod(
        { assignedAt: original.assignedAt, returnedAt: null },
        { assignedAt: new Date(2025, 4, 20), returnedAt: null },
        { notBefore: '2025-05-25T08:00:00Z', notAfter: null }, now);

      expect(result).toEqual({ assignedAt: '2025-05-25T08:00:00Z', returnedAt: null });
    });
  });
});

describe('share links', () => {
  it('builds the public and messaging URLs', () => {
    const url = shareUrl('https://app.example', 'Ab-_9');

    expect(url).toBe('https://app.example/t/Ab-_9');
    expect(whatsAppUrl('T 1', url)).toBe('https://wa.me/?text=T%201%0Ahttps%3A%2F%2Fapp.example%2Ft%2FAb-_9');
    expect(telegramUrl('T 1', url)).toBe('https://t.me/share/url?url=https%3A%2F%2Fapp.example%2Ft%2FAb-_9&text=T%201');
  });
});

describe('AssignmentsApi', () => {
  afterEach(() => TestBed.inject(HttpTestingController).verify());

  it('calls the assignment endpoints', async () => {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    const http = TestBed.inject(HttpTestingController);
    const api = TestBed.inject(AssignmentsApi);
    const link = { assignmentId: 7, token: 't' };

    const list = api.list();
    http.expectOne({ method: 'GET', url: '/api/assignments' }).flush([]);
    expect(await list).toEqual([]);

    const assigned = api.assign({ territoryId: 1, publisherId: 2, assignedAt: 'd' });
    const post = http.expectOne({ method: 'POST', url: '/api/assignments' });
    expect(post.request.body).toEqual({ territoryId: 1, publisherId: 2, assignedAt: 'd' });
    post.flush(link);
    expect(await assigned).toEqual(link);

    const renewed = api.renewLink(7);
    http.expectOne({ method: 'POST', url: '/api/assignments/7/link' }).flush(link);
    expect(await renewed).toEqual(link);

    const returned = api.registerReturn(7, 'r');
    const ret = http.expectOne({ method: 'POST', url: '/api/assignments/7/return' });
    expect(ret.request.body).toEqual({ returnedAt: 'r' });
    ret.flush(null);
    await returned;

    const updated = api.updateDates(7, { assignedAt: 'a', returnedAt: null });
    const put = http.expectOne({ method: 'PUT', url: '/api/assignments/7' });
    expect(put.request.body).toEqual({ assignedAt: 'a', returnedAt: null });
    put.flush(null);
    await updated;

    const history = api.history(1);
    http.expectOne({ method: 'GET', url: '/api/territories/1/assignments' }).flush([]);
    expect(await history).toEqual([]);
  });
});
