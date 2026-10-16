import { averageFrequency, elapsedDays, elapsedTime, frequencyDays } from './frequency';

describe('average assignment frequency', () => {
  it('is null when the territory was not assigned in the last year', () => {
    expect(averageFrequency(0)).toBeNull();
  });

  it('splits the average interval into months and days', () => {
    expect(averageFrequency(1)).toEqual({ months: 12, days: 5 });
    expect(averageFrequency(2)).toEqual({ months: 6, days: 3 });
    expect(averageFrequency(4)).toEqual({ months: 3, days: 1 });
  });

  it('never goes below one day', () => {
    expect(averageFrequency(1000)).toEqual({ months: 0, days: 1 });
  });

  it('sorts never-assigned territories last', () => {
    expect(frequencyDays(2)).toBeLessThan(frequencyDays(1));
    expect(frequencyDays(0)).toBeGreaterThan(frequencyDays(1));
  });
});

describe('elapsed time', () => {
  const today = new Date(2026, 9, 16, 9, 30);

  it('is null without a date', () => {
    expect(elapsedTime(null, today)).toBeNull();
    expect(elapsedDays(null, today)).toBe(Number.MAX_SAFE_INTEGER);
  });

  it('counts whole calendar days, so the day of the assignment counts as zero', () => {
    expect(elapsedTime(new Date(2026, 9, 16, 23, 59, 59).toISOString(), today)).toEqual({ months: 0, days: 0 });
    expect(elapsedTime(new Date(2026, 9, 15, 23, 59, 59).toISOString(), today)).toEqual({ months: 0, days: 1 });
  });

  it('splits the elapsed days into months of 30 days and days', () => {
    expect(elapsedTime(new Date(2026, 8, 16).toISOString(), today)).toEqual({ months: 1, days: 0 });
    expect(elapsedTime(new Date(2026, 9, 6).toISOString(), today)).toEqual({ months: 0, days: 10 });
    expect(elapsedTime(new Date(2025, 9, 16).toISOString(), today)).toEqual({ months: 12, days: 5 });
    expect(elapsedTime(new Date(2025, 9, 15).toISOString(), today)).toEqual({ months: 12, days: 6 });
  });

  it('never goes negative for a future date and sorts by elapsed days', () => {
    expect(elapsedTime(new Date(2026, 11, 1).toISOString(), today)).toEqual({ months: 0, days: 0 });
    expect(elapsedDays(new Date(2026, 9, 6).toISOString(), today)).toBe(10);
    expect(elapsedDays(new Date(2025, 9, 16).toISOString(), today))
      .toBeGreaterThan(elapsedDays(new Date(2026, 9, 16).toISOString(), today));
  });
});
