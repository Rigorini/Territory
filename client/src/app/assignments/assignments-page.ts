import { Component, computed, inject, OnInit, signal } from '@angular/core';
import { MatTableModule } from '@angular/material/table';
import { MatSortModule } from '@angular/material/sort';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatTooltipModule } from '@angular/material/tooltip';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { TranslatePipe } from '@ngx-translate/core';
import { AssignmentsApi, OpenAssignmentDto, ShareLinkDto, TerritoryStatusDto } from './assignments.api';
import { AssignDialog, AssignDialogData } from './assign-dialog';
import { averageFrequency, Frequency, frequencyDays } from './frequency';
import { HistoryDialog, HistoryDialogData } from './history-dialog';
import { ReturnDialog, ReturnDialogData } from './return-dialog';
import { ShareDialog, ShareDialogData } from './share-dialog';
import { PublisherDto, PublishersApi } from '../publishers/publishers.api';
import { ColumnFilter, FilterOption } from '../shared/column-filter';
import { Feedback } from '../shared/feedback';
import { formatDate } from '../shared/format';
import { ListResource } from '../shared/list-resource';
import { TableState } from '../shared/table-state';

type TerritoryStatus = 'assigned' | 'available';

/** Status board: who has each territory, with assign, share, return and history actions. */
@Component({
  selector: 'app-assignments-page',
  imports: [
    MatTableModule, MatSortModule, MatButtonModule, MatIconModule, MatTooltipModule,
    MatProgressBarModule, TranslatePipe, ColumnFilter,
  ],
  templateUrl: './assignments-page.html',
})
export class AssignmentsPage implements OnInit {
  private readonly api = inject(AssignmentsApi);
  private readonly publishersApi = inject(PublishersApi);
  private readonly feedback = inject(Feedback);

  protected readonly columns = ['municipality', 'number', 'name', 'status', 'publisher', 'date', 'frequency', 'actions'];
  protected readonly filterColumns = this.columns.map(c => `f-${c}`);
  protected readonly filtersOpen = signal(false);
  protected readonly statusOptions: readonly FilterOption[] = (['assigned', 'available'] as const)
    .map(s => ({ value: s, labelKey: `assignments.statuses.${s}` }));
  protected readonly statuses = new ListResource(() => this.api.list(), this.feedback);
  protected readonly municipalityOptions = computed<FilterOption[]>(() =>
    [...new Set(this.statuses.items().map(s => s.municipalityName))].map(value => ({ value })));

  protected readonly table = new TableState(this.statuses.items, {
    filters: signal({
      municipality: (s: TerritoryStatusDto) => s.municipalityName,
      number: (s: TerritoryStatusDto) => String(s.number),
      name: (s: TerritoryStatusDto) => s.name,
      status: (s: TerritoryStatusDto) => statusOf(s),
      publisher: (s: TerritoryStatusDto) => s.assignment?.publisherName ?? '',
      date: (s: TerritoryStatusDto) => formatOptionalDate(dateOf(s)),
    }),
    exactFilters: ['municipality', 'number', 'status'],
    sort: {
      municipality: s => [s.municipalityName, s.number],
      number: s => s.number,
      name: s => s.name,
      status: s => [statusOf(s), s.municipalityName, s.number],
      publisher: s => s.assignment?.publisherName ?? '',
      date: s => dateOf(s) ?? '',
      frequency: s => frequencyDays(s.assignmentsLastYear),
    },
    initialSort: { active: 'municipality', direction: 'asc' },
  });

  ngOnInit(): void {
    void this.statuses.reload();
  }

  protected status(row: TerritoryStatusDto): TerritoryStatus {
    return statusOf(row);
  }

  protected date(row: TerritoryStatusDto): string {
    return formatOptionalDate(dateOf(row));
  }

  protected frequency(row: TerritoryStatusDto): Frequency | null {
    return averageFrequency(row.assignmentsLastYear);
  }

  protected async assign(territory: TerritoryStatusDto): Promise<void> {
    const publishers = await this.activePublishers();
    if (!publishers) {
      return;
    }
    if (publishers.length === 0) {
      this.feedback.notify('assignments.noPublishers');
      return;
    }
    const data: AssignDialogData = { territory, publishers };
    const link = await this.feedback.open<AssignDialog, AssignDialogData, ShareLinkDto>(AssignDialog, data);
    if (!link) {
      return;
    }
    this.feedback.notify('assignments.assigned');
    await this.statuses.reload();
    await this.openShare(territory, link);
  }

  /** The server keeps only a hash of the link, so sharing again means issuing a new one. */
  protected async share(territory: TerritoryStatusDto, assignment: OpenAssignmentDto): Promise<void> {
    const confirmed = await this.feedback.confirm({
      titleKey: 'assignments.renewTitle',
      messageKey: 'assignments.renewMessage',
      params: { number: territory.number, name: territory.name, publisher: assignment.publisherName },
      confirmKey: 'assignments.renewConfirm',
    });
    if (!confirmed) {
      return;
    }
    try {
      await this.openShare(territory, await this.api.renewLink(assignment.id));
    } catch (error) {
      this.feedback.notifyError(error);
      await this.statuses.reload();
    }
  }

  protected async registerReturn(territory: TerritoryStatusDto, assignment: OpenAssignmentDto): Promise<void> {
    const data: ReturnDialogData = { territory, assignment };
    if (await this.feedback.open<ReturnDialog, ReturnDialogData, true>(ReturnDialog, data)) {
      this.feedback.notify('assignments.returned');
      await this.statuses.reload();
    }
  }

  protected async showHistory(territory: TerritoryStatusDto): Promise<void> {
    await this.feedback.open<HistoryDialog, HistoryDialogData, void>(HistoryDialog, { territory });
    // Dates may have been corrected in the history.
    await this.statuses.reload();
  }

  private async openShare(territory: TerritoryStatusDto, link: ShareLinkDto): Promise<void> {
    await this.feedback.open<ShareDialog, ShareDialogData, void>(ShareDialog, { territory, token: link.token });
  }

  private async activePublishers(): Promise<PublisherDto[] | null> {
    try {
      return await this.publishersApi.list();
    } catch (error) {
      this.feedback.notifyError(error);
      return null;
    }
  }
}

function statusOf(row: TerritoryStatusDto): TerritoryStatus {
  return row.assignment ? 'assigned' : 'available';
}

/** Assignment date when assigned, otherwise the date of the last return. */
function dateOf(row: TerritoryStatusDto): string | null {
  return row.assignment?.assignedAt ?? row.lastReturnedAt;
}

function formatOptionalDate(value: string | null): string {
  return value ? formatDate(value) : '';
}
