const WINDOW_DAYS = 365;
const DAYS_PER_MONTH = 30;

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
