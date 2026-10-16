import { Component, computed, inject, input, numberAttribute, OnInit } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { MatTableModule } from '@angular/material/table';
import { MatTooltipModule } from '@angular/material/tooltip';
import { Router } from '@angular/router';
import { TranslatePipe } from '@ngx-translate/core';
import { AssignmentHistoryDto, AssignmentsApi, TerritoryStatusDto } from './assignments.api';
import { HistoryList } from './history-list';
import { Feedback } from '../shared/feedback';
import { formatOptionalDate } from '../shared/format';
import { ListPaginator } from '../shared/list-paginator';
import { ListResource } from '../shared/list-resource';
import { Paging } from '../shared/paging';

/** Full assignment history of a territory, paginated, with the same actions as the dialog. */
@Component({
  selector: 'app-history-page',
  imports: [
    MatTableModule, MatButtonModule, MatIconModule, MatTooltipModule, MatProgressBarModule,
    TranslatePipe, ListPaginator,
  ],
  templateUrl: './history-page.html',
})
export class HistoryPage implements OnInit {
  private readonly api = inject(AssignmentsApi);
  private readonly router = inject(Router);
  private readonly feedback = inject(Feedback);

  /** Territory id from the route; the name shown in the title comes from the API. */
  protected readonly territoryId = input.required({ transform: numberAttribute });
  protected readonly columns = ['publisher', 'assignedAt', 'returnedAt', 'returnedBy', 'actions'];

  private readonly statuses = new ListResource<TerritoryStatusDto>(() => this.api.list(), this.feedback);
  protected readonly territory = computed(() =>
    this.statuses.items().find(t => t.territoryId === this.territoryId()) ?? null);
  protected readonly history = new HistoryList(this.territoryId, this.territory, this.api, this.feedback);
  protected readonly paging = new Paging(this.history.items);

  async ngOnInit(): Promise<void> {
    await Promise.all([this.statuses.reload(), this.history.reload()]);
  }

  protected async editDates(entry: AssignmentHistoryDto): Promise<void> {
    await this.history.editDates(entry);
  }

  protected async confirmDelete(entry: AssignmentHistoryDto): Promise<void> {
    await this.history.confirmDelete(entry);
  }

  protected formatDate(value: string | null): string {
    return formatOptionalDate(value);
  }

  protected back(): void {
    void this.router.navigate(['/admin/assignments']);
  }
}
