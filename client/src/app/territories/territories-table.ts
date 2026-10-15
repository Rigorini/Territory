import { Component, computed, inject, input, output, signal } from '@angular/core';
import { MatTableModule } from '@angular/material/table';
import { MatSortModule } from '@angular/material/sort';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatTooltipModule } from '@angular/material/tooltip';
import { TranslatePipe } from '@ngx-translate/core';
import { MunicipalityDto, TerritoriesApi, TerritoryDto } from './territories.api';
import { TerritoryDialog, TerritoryDialogData } from './territory-dialog';
import { ColumnFilter, FilterOption } from '../shared/column-filter';
import { Feedback } from '../shared/feedback';
import { TableState } from '../shared/table-state';

/** Territories with per-column filters; emits changed after a save or delete. */
@Component({
  selector: 'app-territories-table',
  imports: [MatTableModule, MatSortModule, MatButtonModule, MatIconModule, MatTooltipModule, TranslatePipe, ColumnFilter],
  templateUrl: './territories-table.html',
})
export class TerritoriesTable {
  private readonly api = inject(TerritoriesApi);
  private readonly feedback = inject(Feedback);

  readonly territories = input.required<readonly TerritoryDto[]>();
  readonly municipalities = input.required<readonly MunicipalityDto[]>();
  readonly changed = output();

  protected readonly columns = ['municipality', 'number', 'name', 'map', 'actions'];
  protected readonly filterColumns = this.columns.map(c => `f-${c}`);
  protected readonly filtersOpen = signal(false);
  protected readonly municipalityOptions = computed<FilterOption[]>(() =>
    this.municipalities().map(m => ({ value: m.name })));

  protected readonly table = new TableState(this.territories, {
    filters: signal({
      municipality: (t: TerritoryDto) => t.municipalityName,
      number: (t: TerritoryDto) => String(t.number),
      name: (t: TerritoryDto) => t.name,
    }),
    exactFilters: ['municipality', 'number'],
    sort: { municipality: t => [t.municipalityName, t.number], number: t => t.number, name: t => t.name },
    initialSort: { active: 'municipality', direction: 'asc' },
  });

  protected async openDialog(territory?: TerritoryDto): Promise<void> {
    const data: TerritoryDialogData = { territory, municipalities: this.municipalities() };
    if (await this.feedback.open<TerritoryDialog, TerritoryDialogData, TerritoryDto>(TerritoryDialog, data)) {
      this.feedback.notify(territory ? 'territories.updated' : 'territories.created');
      this.changed.emit();
    }
  }

  protected async confirmDelete(territory: TerritoryDto): Promise<void> {
    if (!(await this.confirmDeletion(territory))) {
      return;
    }
    await this.feedback.attempt(() => this.api.deleteTerritory(territory), 'territories.deleted');
    this.changed.emit();
  }

  /** A territory with assignment history needs a second, explicit confirmation. */
  private async confirmDeletion(territory: TerritoryDto): Promise<boolean> {
    const params = { number: territory.number, name: territory.name, count: territory.assignmentCount };
    const confirmed = await this.feedback.confirm({
      titleKey: 'territories.deleteTitle',
      messageKey: 'territories.deleteMessage',
      params,
      confirmKey: 'common.delete',
    });
    if (!confirmed || territory.assignmentCount === 0) {
      return confirmed;
    }
    return this.feedback.confirm({
      titleKey: 'territories.deleteHistoryTitle',
      messageKey: 'territories.deleteHistoryMessage',
      params,
      confirmKey: 'territories.deleteHistoryConfirm',
    });
  }
}
