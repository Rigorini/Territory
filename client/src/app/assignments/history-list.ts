import { Signal } from '@angular/core';
import { AssignmentHistoryDto, AssignmentsApi, TerritoryStatusDto } from './assignments.api';
import { EditDatesDialog, EditDatesDialogData } from './edit-dates-dialog';
import { Feedback } from '../shared/feedback';
import { formatOptionalDate } from '../shared/format';
import { ListResource } from '../shared/list-resource';

/**
 * Assignment history of a territory, newest first: the loaded list plus the date
 * correction and deletion of a single entry. Both are read lazily, since the
 * history page learns the territory from the API while the dialog already has it.
 */
export class HistoryList {
  readonly items: Signal<AssignmentHistoryDto[]>;
  readonly loading: Signal<boolean>;
  private readonly resource: ListResource<AssignmentHistoryDto>;

  constructor(
    private readonly territoryId: () => number,
    private readonly territory: () => TerritoryStatusDto | null,
    private readonly api: AssignmentsApi,
    private readonly feedback: Feedback,
  ) {
    this.resource = new ListResource(() => this.api.history(this.territoryId()), feedback);
    this.items = this.resource.items.asReadonly();
    this.loading = this.resource.loading.asReadonly();
  }

  reload(): Promise<void> {
    return this.resource.reload();
  }

  /** Corrects the dates of an entry: its neighbours bound the allowed period. */
  async editDates(entry: AssignmentHistoryDto): Promise<void> {
    const items = this.items();
    const index = Math.max(0, items.findIndex(i => i.id === entry.id));
    const territory = this.territory();
    if (!territory) {
      return;
    }
    const data: EditDatesDialogData = {
      territory,
      entry,
      previous: items[index + 1] ?? null,
      next: items[index - 1] ?? null,
    };
    if (await this.feedback.open<EditDatesDialog, EditDatesDialogData, true>(EditDatesDialog, data)) {
      this.feedback.notify('assignments.datesUpdated');
      await this.reload();
    }
  }

  async confirmDelete(entry: AssignmentHistoryDto): Promise<void> {
    const territory = this.territory();
    if (!territory) {
      return;
    }
    const confirmed = await this.feedback.confirm({
      titleKey: 'assignments.deleteTitle',
      messageKey: 'assignments.deleteMessage',
      params: {
        number: territory.number,
        name: territory.name,
        publisher: entry.publisherName,
        assignedAt: formatOptionalDate(entry.assignedAt),
      },
      confirmKey: 'common.delete',
    });
    if (confirmed) {
      await this.feedback.attempt(() => this.api.delete(entry.id), 'assignments.deleted');
      await this.reload();
    }
  }
}
