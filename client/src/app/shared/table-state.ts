import { computed, Signal, signal, WritableSignal } from '@angular/core';
import { Sort } from '@angular/material/sort';
import { ColumnAccessors, FilterValues, filterRows, SortDirection, SortKey, sortRows } from './table-filter';

export type SortAccessors<T> = Readonly<Record<string, (row: T) => SortKey>>;

export interface TableConfig<T> {
  /** Display text of each filterable column (may depend on the language). */
  filters: Signal<ColumnAccessors<T>>;
  /** Columns filtered by a select: the value must match exactly. */
  exactFilters?: readonly string[];
  sort: SortAccessors<T>;
  initialSort: Sort;
}

/** Per-column filters and sorting of a client-side table; rows are recomputed when any input changes. */
export class TableState<T> {
  readonly filters = signal<FilterValues>({});
  readonly sort: WritableSignal<Sort>;
  readonly hasFilters = computed(() => Object.values(this.filters()).some(v => v.trim() !== ''));
  readonly rows: Signal<T[]>;

  constructor(source: Signal<readonly T[]>, config: TableConfig<T>) {
    this.sort = signal(config.initialSort);
    const exact = new Set(config.exactFilters ?? []);
    this.rows = computed(() => {
      const filtered = filterRows(source(), this.filters(), config.filters(), exact);
      const { active, direction } = this.sort();
      return sortRows(filtered, active, direction as SortDirection, config.sort);
    });
  }

  filter(column: string): string {
    return this.filters()[column] ?? '';
  }

  setFilter(column: string, value: string): void {
    this.filters.update(f => ({ ...f, [column]: value }));
  }

  clearFilters(): void {
    this.filters.set({});
  }

  onSort(sort: Sort): void {
    this.sort.set(sort);
  }
}
