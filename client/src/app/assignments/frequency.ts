const WINDOW_DAYS = 365;
const DAYS_PER_MONTH = 30;
const MILLIS_PER_DAY = 24 * 60 * 60 * 1000;

export interface Frequency {
  months: number;
  days: number;
}

/** Average interval between assignments over the last year (months of 30 days plus days); null when never assigned. */
export function averageFrequency(assignmentsLastYear: number): Frequency | null {
  if (assignmentsLastYear <= 0) {
    return null;
  }
  const totalDays = Math.max(1, Math.round(WINDOW_DAYS / assignmentsLastYear));
  return { months: Math.floor(totalDays / DAYS_PER_MONTH), days: totalDays % DAYS_PER_MONTH };
}

/** Sort key: total days of the average interval; territories never assigned go last. */
export function frequencyDays(assignmentsLastYear: number): number {
  const frequency = averageFrequency(assignmentsLastYear);
  return frequency ? frequency.months * DAYS_PER_MONTH + frequency.days : Number.MAX_SAFE_INTEGER;
}

/** Time elapsed from a date to today (months of 30 days plus days); null without a date. */
export function elapsedTime(from: string | null, today: Date = new Date()): Frequency | null {
  const days = elapsedDayCount(from, today);
  return days === null ? null : { months: Math.floor(days / DAYS_PER_MONTH), days: days % DAYS_PER_MONTH };
}

/** Sort key: total days elapsed from a date; territories without a date go last. */
export function elapsedDays(from: string | null, today: Date = new Date()): number {
  return elapsedDayCount(from, today) ?? Number.MAX_SAFE_INTEGER;
}

/** Whole calendar days from a date to today, counting a date at 23:59 as that same day. */
function elapsedDayCount(from: string | null, today: Date): number | null {
  return from ? Math.max(0, calendarDay(today) - calendarDay(new Date(from))) : null;
}

/** Day number of a local calendar day; UTC keeps it free of DST shifts. */
function calendarDay(date: Date): number {
  return Math.round(Date.UTC(date.getFullYear(), date.getMonth(), date.getDate()) / MILLIS_PER_DAY);
}
