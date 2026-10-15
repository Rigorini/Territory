import { signal } from '@angular/core';
import { Feedback } from './feedback';

/** Client-side copy of a server list with a loading flag; load errors are notified, keeping the last data. */
export class ListResource<T> {
  readonly items = signal<T[]>([]);
  readonly loading = signal(false);

  constructor(
    private readonly fetch: () => Promise<T[]>,
    private readonly feedback: Feedback,
  ) {}

  async reload(): Promise<void> {
    this.loading.set(true);
    try {
      this.items.set(await this.fetch());
    } catch (error) {
      this.feedback.notifyError(error);
    } finally {
      this.loading.set(false);
    }
  }
}
