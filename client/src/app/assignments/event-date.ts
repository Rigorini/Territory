import { AbstractControl, ValidationErrors, ValidatorFn } from '@angular/forms';

/** Earliest day accepted by the server (DateRules.MinDate). */
export const MIN_EVENT_DAY = '2000-01-01';

/** Local calendar day as yyyy-MM-dd: the culture-independent format of days sent to the API. */
export function localDay(date: Date): string {
  const pad = (n: number) => String(n).padStart(2, '0');
  return `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())}`;
}

/** Local midnight of a yyyy-MM-dd day, as used by the datepicker. */
export function dayDate(day: string): Date {
  const [year, month, date] = day.split('-').map(Number);
  return new Date(year, month - 1, date);
}

/** Local midnight of today. */
export function startOfToday(now: Date = new Date()): Date {
  return dayDate(localDay(now));
}

/**
 * Instant sent for a picked day: the end of that local day, capped at now.
 * Today therefore means "now", and a past day stays after any earlier event of the same day.
 */
export function eventInstant(day: Date, now: Date = new Date()): string {
  const endOfDay = new Date(day.getFullYear(), day.getMonth(), day.getDate(), 23, 59, 59);
  return (endOfDay < now ? endOfDay : now).toISOString();
}

/**
 * Day between a lower bound and today. Errors: required, inFuture, or the given code when too early.
 * Days compare as yyyy-MM-dd text, which sorts chronologically and ignores the time of day.
 */
export function dayRangeValidator(minDay: Date, tooEarlyCode: string, now: () => Date = () => new Date()): ValidatorFn {
  return (control: AbstractControl<Date | null>): ValidationErrors | null => {
    if (!control.value) {
      return { required: true };
    }
    const day = localDay(control.value);
    if (day > localDay(now())) {
      return { inFuture: true };
    }
    return day < localDay(minDay) ? { [tooEarlyCode]: true } : null;
  };
}

/**
 * End day of a period: not before the day of the start control (tooEarlyCode), not after today (inFuture)
 * and, when given, not after latestDay (tooLateCode). The start is read from the sibling control.
 */
export function periodEndValidator(
  startControl: string, tooEarlyCode: string, latestDay: string | null, tooLateCode: string,
  now: () => Date = () => new Date(),
): ValidatorFn {
  return (control: AbstractControl<Date | null>): ValidationErrors | null => {
    const start = (control.parent?.get(startControl)?.value as Date | null | undefined) ?? dayDate(MIN_EVENT_DAY);
    const errors = dayRangeValidator(start, tooEarlyCode, now)(control);
    if (errors || latestDay === null) {
      return errors;
    }
    return localDay(control.value as Date) > latestDay ? { [tooLateCode]: true } : null;
  };
}

/** Stored period of an assignment: ISO instants as returned by the API. */
export interface EventPeriod {
  assignedAt: string;
  returnedAt: string | null;
}

/** Instants of the neighbouring periods: the previous return and the next assignment. Periods may touch. */
export interface PeriodBounds {
  notBefore: string | null;
  notAfter: string | null;
}

interface Instant {
  readonly time: number;
  readonly text: string;
}

const instant = (text: string): Instant => ({ time: Date.parse(text), text });

/** Keeps the original instant (with its full precision) when the day did not change. */
function correctedInstant(original: string, day: Date, now: Date): Instant {
  return localDay(new Date(original)) === localDay(day) ? instant(original) : instant(eventInstant(day, now));
}

/**
 * Instants sent for a corrected period. A changed day becomes eventInstant; the result is then kept
 * within the neighbours and with the start not after the end, so same-day corrections never overlap.
 */
export function correctedPeriod(
  original: EventPeriod, days: { assignedAt: Date; returnedAt: Date | null }, bounds: PeriodBounds,
  now: Date = new Date(),
): EventPeriod {
  let start = correctedInstant(original.assignedAt, days.assignedAt, now);
  if (bounds.notBefore !== null && Date.parse(bounds.notBefore) > start.time) {
    start = instant(bounds.notBefore);
  }
  if (original.returnedAt === null || days.returnedAt === null) {
    return { assignedAt: start.text, returnedAt: null };
  }
  let end = correctedInstant(original.returnedAt, days.returnedAt, now);
  if (bounds.notAfter !== null && Date.parse(bounds.notAfter) < end.time) {
    end = instant(bounds.notAfter);
  }
  return { assignedAt: (start.time > end.time ? end : start).text, returnedAt: end.text };
}

/** Lower bound for a new event: the later of the server minimum and the day of a previous event. */
export function minDayAfter(previous: string | null | undefined): Date {
  const day = previous ? localDay(new Date(previous)) : MIN_EVENT_DAY;
  return dayDate(day > MIN_EVENT_DAY ? day : MIN_EVENT_DAY);
}
