import { Component, inject, OnInit, signal } from '@angular/core';
import { MatTableModule } from '@angular/material/table';
import { MatSortModule } from '@angular/material/sort';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatTooltipModule } from '@angular/material/tooltip';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { TranslatePipe } from '@ngx-translate/core';
import { PublisherDto, PublishersApi } from './publishers.api';
import { PublisherDialog, PublisherDialogData, PublisherSaved } from './publisher-dialog';
import { ColumnFilter } from '../shared/column-filter';
import { Feedback } from '../shared/feedback';
import { ListPaginator } from '../shared/list-paginator';
import { ListResource } from '../shared/list-resource';
import { Paging } from '../shared/paging';
import { TableState } from '../shared/table-state';

@Component({
  selector: 'app-publishers-page',
  imports: [
    MatTableModule, MatSortModule, MatButtonModule, MatIconModule, MatTooltipModule,
    MatProgressBarModule, TranslatePipe, ColumnFilter, ListPaginator,
  ],
  templateUrl: './publishers-page.html',
})
export class PublishersPage implements OnInit {
  private readonly api = inject(PublishersApi);
  private readonly feedback = inject(Feedback);

  protected readonly columns = ['lastName', 'firstName', 'actions'];
  protected readonly filterColumns = this.columns.map(c => `f-${c}`);
  protected readonly filtersOpen = signal(false);
  protected readonly publishers = new ListResource(() => this.api.list(), this.feedback);

  protected readonly table = new TableState(this.publishers.items, {
    filters: signal({
      lastName: (p: PublisherDto) => p.lastName,
      firstName: (p: PublisherDto) => p.firstName,
    }),
    sort: { lastName: p => [p.lastName, p.firstName], firstName: p => [p.firstName, p.lastName] },
    initialSort: { active: 'lastName', direction: 'asc' },
  });
  protected readonly paging = new Paging(this.table.rows);

  ngOnInit(): void {
    void this.publishers.reload();
  }

  protected async openDialog(publisher?: PublisherDto): Promise<void> {
    const result = await this.feedback.open<PublisherDialog, PublisherDialogData, PublisherSaved>(PublisherDialog, { publisher });
    if (result) {
      this.feedback.notify(savedMessage(publisher, result));
      await this.publishers.reload();
    }
  }

  protected async confirmDelete(publisher: PublisherDto): Promise<void> {
    const confirmed = await this.feedback.confirm({
      titleKey: 'publishers.deleteTitle',
      messageKey: 'publishers.deleteMessage',
      params: { firstName: publisher.firstName, lastName: publisher.lastName },
      confirmKey: 'common.delete',
    });
    if (confirmed) {
      await this.feedback.attempt(() => this.api.delete(publisher.id), 'publishers.deleted');
      await this.publishers.reload();
    }
  }
}

function savedMessage(publisher: PublisherDto | undefined, result: PublisherSaved): string {
  if (publisher) {
    return 'publishers.updated';
  }
  return result.reactivated ? 'publishers.reactivated' : 'publishers.created';
}
