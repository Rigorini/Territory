import { Component, inject, input, output, signal } from '@angular/core';
import { MatTableModule } from '@angular/material/table';
import { MatSortModule } from '@angular/material/sort';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatTooltipModule } from '@angular/material/tooltip';
import { TranslatePipe } from '@ngx-translate/core';
import { MunicipalitiesApi, MunicipalityDto } from './territories.api';
import { MunicipalityDialog, MunicipalityDialogData } from './municipality-dialog';
import { ColumnFilter } from '../shared/column-filter';
import { Feedback } from '../shared/feedback';
import { TableState } from '../shared/table-state';

/** Municipalities with territory counts; only empty municipalities can be deleted. */
@Component({
  selector: 'app-municipalities-table',
  imports: [MatTableModule, MatSortModule, MatButtonModule, MatIconModule, MatTooltipModule, TranslatePipe, ColumnFilter],
  templateUrl: './municipalities-table.html',
})
export class MunicipalitiesTable {
  private readonly api = inject(MunicipalitiesApi);
  private readonly feedback = inject(Feedback);

  readonly municipalities = input.required<readonly MunicipalityDto[]>();
  readonly changed = output();

  protected readonly columns = ['name', 'territoryCount', 'actions'];
  protected readonly filterColumns = this.columns.map(c => `f-${c}`);
  protected readonly filtersOpen = signal(false);

  protected readonly table = new TableState(this.municipalities, {
    filters: signal({
      name: (m: MunicipalityDto) => m.name,
      territoryCount: (m: MunicipalityDto) => String(m.territoryCount),
    }),
    exactFilters: ['territoryCount'],
    sort: { name: m => m.name, territoryCount: m => m.territoryCount },
    initialSort: { active: 'name', direction: 'asc' },
  });

  protected async openDialog(municipality?: MunicipalityDto): Promise<void> {
    const data: MunicipalityDialogData = { municipality };
    if (await this.feedback.open<MunicipalityDialog, MunicipalityDialogData, MunicipalityDto>(MunicipalityDialog, data)) {
      this.feedback.notify(municipality ? 'municipalities.updated' : 'municipalities.created');
      this.changed.emit();
    }
  }

  protected async confirmDelete(municipality: MunicipalityDto): Promise<void> {
    const confirmed = await this.feedback.confirm({
      titleKey: 'municipalities.deleteTitle',
      messageKey: 'municipalities.deleteMessage',
      params: { name: municipality.name },
      confirmKey: 'common.delete',
    });
    if (confirmed) {
      await this.feedback.attempt(() => this.api.delete(municipality.id), 'municipalities.deleted');
      this.changed.emit();
    }
  }
}
