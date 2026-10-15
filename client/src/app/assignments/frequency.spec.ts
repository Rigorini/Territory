import { averageFrequency, frequencyDays } from './frequency';

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
