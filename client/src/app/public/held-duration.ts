export interface HeldDuration {
  months: number;
  days: number;
}

interface LocalDay {
  year: number;
  month: number;
  day: number;
  /** UTC midnight of the local date: day arithmetic without daylight-saving shifts. */
  value: number;
}

const MS_PER_DAY = 86_400_000;

/**
 * Whole months and remaining days between two instants, on the local calendar.
 * A month is counted when the same day number is reached (clamped to shorter months: 31 Jan → 28 Feb).
 */
export function heldDuration(from: Date, to: Date): HeldDuration {
  const start = localDay(from);
  const end = localDay(to);
  if (end.value <= start.value) {
    return { months: 0, days: 0 };
  }
  let months = (end.year - start.year) * 12 + end.month - start.month;
  if (addMonths(start, months) > end.value) {
    months--;
  }
  return { months, days: Math.round((end.value - addMonths(start, months)) / MS_PER_DAY) };
}

/** Translation key for a counted unit, e.g. ("public.months", 1, "it") → "public.months.one". */
export function pluralKey(base: string, count: number, language: string): string {
  return `${base}.${new Intl.PluralRules(language).select(count) === 'one' ? 'one' : 'other'}`;
}

function localDay(date: Date): LocalDay {
  const year = date.getFullYear();
  const month = date.getMonth();
  const day = date.getDate();
  return { year, month, day, value: Date.UTC(year, month, day) };
}

function addMonths(start: LocalDay, months: number): number {
  const lastDay = new Date(Date.UTC(start.year, start.month + months + 1, 0)).getUTCDate();
  return Date.UTC(start.year, start.month + months, Math.min(start.day, lastDay));
}
