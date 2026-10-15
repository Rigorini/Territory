/** Column filter values keyed by column id; empty values do not filter. */
export type FilterValues = Readonly<Record<string, string>>;

/** Extracts the display text of a column, used for case-insensitive "contains" filtering. */
export type ColumnAccessors<T> = Readonly<Record<string, (row: T) => string>>;

/** Filters rows by "contains"; columns listed in exactColumns (select filters) must match the whole value. */
export function filterRows<T>(
  rows: readonly T[],
  filters: FilterValues,
  accessors: ColumnAccessors<T>,
  exactColumns: ReadonlySet<string> = new Set(),
): T[] {
  const active = Object.entries(filters)
    .map(([column, value]) => ({
      accessor: accessors[column],
      needle: value.trim().toLocaleLowerCase(),
      exact: exactColumns.has(column),
    }))
    .filter(f => f.accessor !== undefined && f.needle !== '');
  return rows.filter(row => active.every(f => matches(f.accessor(row).toLocaleLowerCase(), f.needle, f.exact)));
}

function matches(text: string, needle: string, exact: boolean): boolean {
  return exact ? text === needle : text.includes(needle);
}

export type SortDirection = 'asc' | 'desc' | '';

/** A sort key; arrays compare element by element (e.g. municipality, then number). */
export type SortKey = string | number | readonly (string | number)[];

export function sortRows<T>(
  rows: readonly T[],
  column: string,
  direction: SortDirection,
  accessors: Readonly<Record<string, (row: T) => SortKey>>,
): T[] {
  const accessor = accessors[column];
  if (!accessor || direction === '') {
    return [...rows];
  }
  const factor = direction === 'asc' ? 1 : -1;
  return [...rows].sort((a, b) => factor * compareKeys(accessor(a), accessor(b)));
}

function compareKeys(a: SortKey, b: SortKey): number {
  const left = Array.isArray(a) ? a : [a];
  const right = Array.isArray(b) ? b : [b];
  for (let i = 0; i < Math.min(left.length, right.length); i++) {
    const result = compare(left[i], right[i]);
    if (result !== 0) {
      return result;
    }
  }
  return left.length - right.length;
}

function compare(a: string | number, b: string | number): number {
  if (typeof a === 'number' && typeof b === 'number') {
    return a - b;
  }
  return String(a).localeCompare(String(b), undefined, { sensitivity: 'base', numeric: true });
}
