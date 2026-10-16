const pad = (n: number) => String(n).padStart(2, '0');
const DAY_PATTERN = /^\s*(\d{1,2})[/.-](\d{1,2})[/.-](\d{4})\s*$/;

/**
 * Local calendar day as dd/MM/yyyy in every language: no locale (en-US would swap day and month).
 * The 4-digit year lets table filters match a year.
 */
export function formatDay(date: Date): string {
  return `${pad(date.getDate())}/${pad(date.getMonth() + 1)}/${date.getFullYear()}`;
}

/** Day of an ISO instant from the API, in the browser time zone. */
export function formatDate(value: string): string {
  return formatDay(new Date(value));
}

/** Day of an optional instant: a dash when the API has no value (e.g. an open assignment). */
export function formatOptionalDate(value: string | null): string {
  return value ? formatDate(value) : '—';
}

/** Strict dd/MM/yyyy (also with "." or "-"); null for other formats and impossible days such as 31/02. */
export function parseDay(text: string): Date | null {
  const match = DAY_PATTERN.exec(text);
  if (!match) {
    return null;
  }
  const [day, month, year] = match.slice(1).map(Number);
  const date = new Date(year, month - 1, day);
  const sameDay = date.getFullYear() === year && date.getMonth() === month - 1 && date.getDate() === day;
  return sameDay ? date : null;
}
