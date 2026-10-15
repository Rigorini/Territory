import { Component, inject, OnInit, signal } from '@angular/core';
import { MAT_DIALOG_DATA, MatDialogModule } from '@angular/material/dialog';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { MatTooltipModule } from '@angular/material/tooltip';
import { TranslatePipe } from '@ngx-translate/core';
import { AssignmentHistoryDto, AssignmentsApi, TerritoryStatusDto } from './assignments.api';
import { EditDatesDialog, EditDatesDialogData } from './edit-dates-dialog';
import { Feedback } from '../shared/feedback';
import { formatDate } from '../shared/format';
import { ListResource } from '../shared/list-resource';

export interface HistoryDialogData {
  territory: TerritoryStatusDto;
}

/** Assignment history of a territory, newest first, with date corrections. */
@Component({
  selector: 'app-history-dialog',
  imports: [MatDialogModule, MatButtonModule, MatIconModule, MatProgressBarModule, MatTooltipModule, TranslatePipe],
  templateUrl: './history-dialog.html',
  styleUrl: './assignment-dialogs.scss',
})
export class HistoryDialog implements OnInit {
  private readonly api = inject(AssignmentsApi);
  protected readonly data = inject<HistoryDialogData>(MAT_DIALOG_DATA);
  private readonly feedback = inject(Feedback);
  protected readonly history = new ListResource<AssignmentHistoryDto>(
    () => this.api.history(this.data.territory.territoryId), this.feedback);
  protected readonly loaded = signal(false);

  async ngOnInit(): Promise<void> {
    await this.history.reload();
    this.loaded.set(true);
  }

  /** Neighbours bound the correction: items are newest first. */
  protected async editDates(index: number): Promise<void> {
    const items = this.history.items();
    const data: EditDatesDialogData = {
      territory: this.data.territory,
      entry: items[index],
      previous: items[index + 1] ?? null,
      next: items[index - 1] ?? null,
    };
    if (await this.feedback.open<EditDatesDialog, EditDatesDialogData, true>(EditDatesDialog, data)) {
      this.feedback.notify('assignments.datesUpdated');
      await this.history.reload();
    }
  }

  protected async confirmDelete(entry: AssignmentHistoryDto): Promise<void> {
    const confirmed = await this.feedback.confirm({
      titleKey: 'assignments.deleteTitle',
      messageKey: 'assignments.deleteMessage',
      params: {
        number: this.data.territory.number,
        name: this.data.territory.name,
        publisher: entry.publisherName,
        assignedAt: formatDate(entry.assignedAt),
      },
      confirmKey: 'common.delete',
    });
    if (confirmed) {
      await this.feedback.attempt(() => this.api.delete(entry.id), 'assignments.deleted');
      await this.history.reload();
    }
  }

  protected formatDate(value: string | null): string {
    return value ? formatDate(value) : '—';
  }
}
