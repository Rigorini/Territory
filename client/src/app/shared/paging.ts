import { computed, effect, Signal, signal } from '@angular/core';

/** Page sizes offered by the paginated lists. */
export const PAGE_SIZES = [10, 25, 50];

/** The part of a paginator event that pagination needs. */
export interface PageChange {
  pageIndex: number;
  pageSize: number;
}

/** Client-side pagination of a list: the rows of the current page plus the paginator state. */
export class Paging<T> {
  readonly pageSizeOptions = PAGE_SIZES;
  readonly length: Signal<number>;
  readonly index: Signal<number>;
  readonly size: Signal<number>;
  readonly rows: Signal<T[]>;

  private readonly state = signal({ index: 0, size: PAGE_SIZES[0] });

  constructor(private readonly source: Signal<readonly T[]>) {
    this.length = computed(() => source().length);
    this.index = computed(() => this.state().index);
    this.size = computed(() => this.state().size);
    this.rows = computed(() => {
      const { index, size } = this.state();
      return source().slice(index * size, (index + 1) * size);
    });
    // Filtering and deleting shorten the list: fall back to the last page with rows
    // instead of leaving the current one empty.
    effect(() => {
      const last = Math.max(0, Math.ceil(this.length() / this.size()) - 1);
      if (this.index() > last) {
        this.state.set({ index: last, size: this.size() });
      }
    });
  }

  onPage(event: PageChange): void {
    this.state.set({ index: event.pageIndex, size: event.pageSize });
  }
}
