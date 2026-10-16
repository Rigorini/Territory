import { TestBed } from '@angular/core/testing';
import { Type } from '@angular/core';
import { By } from '@angular/platform-browser';
import { MatPaginator } from '@angular/material/paginator';
import { provideTranslateService } from '@ngx-translate/core';
import { MunicipalitiesApi, MunicipalityDto, TerritoriesApi, TerritoryDto } from './territories.api';
import { TerritoriesPage } from './territories-page';
import { TerritoriesTable } from './territories-table';
import { MunicipalitiesTable } from './municipalities-table';
import { TerritoryDialog } from './territory-dialog';
import { MunicipalityDialog } from './municipality-dialog';
import { Feedback } from '../shared/feedback';
import { Paging } from '../shared/paging';
import { TableState } from '../shared/table-state';

const municipalities: MunicipalityDto[] = [
  { id: 2, name: 'Giano dell\'Umbria', territoryCount: 1 },
  { id: 1, name: 'Giano', territoryCount: 2 },
];
const territory = (id: number, municipalityName: string, number: number, assignmentCount = 0): TerritoryDto => ({
  id, municipalityId: 1, municipalityName, number, name: `T${number}`, mapUrl: 'https://www.google.com/maps/d/viewer?mid=x', assignmentCount,
});
const territories = [territory(1, 'Giano', 10), territory(2, 'Giano dell\'Umbria', 1), territory(3, 'Giano', 2, 3)];

function mockFeedback(dialogResult: unknown, confirmations: boolean[] = []) {
  const confirm = vi.fn();
  confirmations.forEach(c => confirm.mockResolvedValueOnce(c));
  return {
    open: vi.fn().mockResolvedValue(dialogResult),
    confirm,
    notify: vi.fn(),
    notifyError: vi.fn(),
    attempt: vi.fn(async (action: () => Promise<unknown>) => {
      await action();
      return true;
    }),
  };
}

function configure(feedback: ReturnType<typeof mockFeedback>) {
  const territoriesApi = { list: vi.fn().mockResolvedValue(territories), deleteTerritory: vi.fn().mockResolvedValue(undefined) };
  const municipalitiesApi = { list: vi.fn().mockResolvedValue(municipalities), delete: vi.fn().mockResolvedValue(undefined) };
  TestBed.configureTestingModule({
    providers: [
      provideTranslateService(),
      { provide: TerritoriesApi, useValue: territoriesApi },
      { provide: MunicipalitiesApi, useValue: municipalitiesApi },
      { provide: Feedback, useValue: feedback },
    ],
  });
  return { territoriesApi, municipalitiesApi };
}

interface TableApi<T> {
  table: TableState<T>;
  openDialog(row?: T): Promise<void>;
  confirmDelete(row: T): Promise<void>;
}

function createTable<C, T>(component: Type<C>, inputs: Record<string, unknown>) {
  const fixture = TestBed.createComponent(component);
  for (const [name, value] of Object.entries(inputs)) {
    fixture.componentRef.setInput(name, value);
  }
  const changed = vi.fn();
  (fixture.componentInstance as { changed: { subscribe(fn: () => void): void } }).changed.subscribe(changed);
  fixture.detectChanges();
  return { fixture, table: fixture.componentInstance as unknown as TableApi<T>, changed };
}

describe('TerritoriesPage', () => {
  it('loads both lists and renders the territories tab', async () => {
    const { territoriesApi, municipalitiesApi } = configure(mockFeedback(undefined));
    const fixture = TestBed.createComponent(TerritoriesPage);
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();

    expect(territoriesApi.list).toHaveBeenCalledTimes(1);
    expect(municipalitiesApi.list).toHaveBeenCalledTimes(1);
    const rows = fixture.nativeElement.querySelectorAll('app-territories-table tr.mat-mdc-row');
    expect(rows.length).toBe(3);
    expect(rows[0].querySelector('a').getAttribute('rel')).toBe('noopener noreferrer');
  });
});

describe('TerritoriesTable', () => {
  const create = (feedback: ReturnType<typeof mockFeedback>) => {
    const apis = configure(feedback);
    return { ...apis, ...createTable<TerritoriesTable, TerritoryDto>(TerritoriesTable, { territories, municipalities }) };
  };

  it('sorts by municipality then number and filters municipality exactly', () => {
    const { table } = create(mockFeedback(undefined));
    expect(table.table.rows().map(t => t.id)).toEqual([3, 1, 2]);

    table.table.setFilter('municipality', 'Giano');
    expect(table.table.rows().map(t => t.id)).toEqual([3, 1]);

    table.table.clearFilters();
    table.table.setFilter('number', '1');
    expect(table.table.rows().map(t => t.id)).toEqual([2]);

    table.table.onSort({ active: 'name', direction: 'desc' });
    table.table.setFilter('number', '');
    expect(table.table.rows().map(t => t.name)).toEqual(['T10', 'T2', 'T1']);
  });

  it('paginates in pages of ten, twenty-five and fifty', () => {
    const many = Array.from({ length: 24 }, (_, i) => territory(i + 1, 'Giano', i + 1));
    configure(mockFeedback(undefined));
    const { fixture } = createTable<TerritoriesTable, TerritoryDto>(
      TerritoriesTable, { territories: many, municipalities });
    const paging = (fixture.componentInstance as unknown as { paging: Paging<TerritoryDto> }).paging;
    const paginator = fixture.debugElement.query(By.directive(MatPaginator)).injector.get(MatPaginator);
    const shown = () => (fixture.nativeElement as HTMLElement).querySelectorAll('tr.mat-mdc-row').length;

    expect(paginator.length).toBe(24);
    expect(paginator.pageSizeOptions).toEqual([10, 25, 50]);
    expect(shown()).toBe(10);

    paging.onPage({ pageIndex: 2, pageSize: 10 });
    fixture.detectChanges();
    expect(shown()).toBe(4);

    paging.onPage({ pageIndex: 0, pageSize: 25 });
    fixture.detectChanges();
    expect(shown()).toBe(24);

    // Filtering shortens the list below the current page: it follows instead of staying empty.
    paging.onPage({ pageIndex: 2, pageSize: 10 });
    fixture.detectChanges();
    (fixture.componentInstance as unknown as { table: TableState<TerritoryDto> }).table.setFilter('name', 'T1');
    fixture.detectChanges();
    expect(paging.index()).toBe(1);
    expect(shown()).toBe(1);
  });

  it('opens the dialog with the municipalities and notifies a save', async () => {
    const feedback = mockFeedback(territories[0]);
    const { table, changed } = create(feedback);

    await table.openDialog(territories[0]);

    expect(feedback.open).toHaveBeenCalledWith(TerritoryDialog, { territory: territories[0], municipalities });
    expect(feedback.notify).toHaveBeenCalledWith('territories.updated');
    expect(changed).toHaveBeenCalled();
  });

  it('does nothing when the dialog is dismissed', async () => {
    const feedback = mockFeedback(undefined);
    const { table, changed } = create(feedback);

    await table.openDialog();

    expect(feedback.notify).not.toHaveBeenCalled();
    expect(changed).not.toHaveBeenCalled();
  });

  it('deletes a territory without history after one confirmation', async () => {
    const feedback = mockFeedback(undefined, [true]);
    const { table, territoriesApi, changed } = create(feedback);

    await table.confirmDelete(territories[0]);

    expect(feedback.confirm).toHaveBeenCalledTimes(1);
    expect(territoriesApi.deleteTerritory).toHaveBeenCalledWith(territories[0]);
    expect(feedback.attempt).toHaveBeenCalledWith(expect.any(Function), 'territories.deleted');
    expect(changed).toHaveBeenCalled();
  });

  it('asks a second confirmation before deleting the history', async () => {
    const declined = mockFeedback(undefined, [true, false]);
    const first = create(declined);
    await first.table.confirmDelete(territories[2]);
    expect(declined.confirm).toHaveBeenCalledTimes(2);
    expect(declined.confirm).toHaveBeenLastCalledWith(expect.objectContaining({ params: { number: 2, name: 'T2', count: 3 } }));
    expect(first.territoriesApi.deleteTerritory).not.toHaveBeenCalled();

    TestBed.resetTestingModule();
    const accepted = mockFeedback(undefined, [true, true]);
    const second = create(accepted);
    await second.table.confirmDelete(territories[2]);
    expect(second.territoriesApi.deleteTerritory).toHaveBeenCalledWith(territories[2]);
  });

  it('stops when the first confirmation is declined', async () => {
    const feedback = mockFeedback(undefined, [false]);
    const { table, territoriesApi } = create(feedback);

    await table.confirmDelete(territories[2]);

    expect(feedback.confirm).toHaveBeenCalledTimes(1);
    expect(territoriesApi.deleteTerritory).not.toHaveBeenCalled();
  });
});

describe('MunicipalitiesTable', () => {
  const create = (feedback: ReturnType<typeof mockFeedback>) => {
    const apis = configure(feedback);
    return { ...apis, ...createTable<MunicipalitiesTable, MunicipalityDto>(MunicipalitiesTable, { municipalities }) };
  };

  it('sorts, filters and disables deleting municipalities with territories', () => {
    const { table, fixture } = create(mockFeedback(undefined));
    expect(table.table.rows().map(m => m.id)).toEqual([1, 2]);

    table.table.setFilter('territoryCount', '1');
    expect(table.table.rows().map(m => m.id)).toEqual([2]);
    table.table.setFilter('name', 'xyz');
    expect(table.table.rows()).toEqual([]);

    const deleteButtons = fixture.nativeElement.querySelectorAll('td.actions button[disabled]');
    expect(deleteButtons.length).toBe(2);
  });

  it('creates through the dialog', async () => {
    const feedback = mockFeedback(municipalities[0]);
    const { table, changed } = create(feedback);

    await table.openDialog();

    expect(feedback.open).toHaveBeenCalledWith(MunicipalityDialog, { municipality: undefined });
    expect(feedback.notify).toHaveBeenCalledWith('municipalities.created');
    expect(changed).toHaveBeenCalled();
  });

  it('deletes after confirmation only', async () => {
    const declined = mockFeedback(undefined, [false]);
    const first = create(declined);
    await first.table.confirmDelete(municipalities[0]);
    expect(first.municipalitiesApi.delete).not.toHaveBeenCalled();

    TestBed.resetTestingModule();
    const accepted = mockFeedback(undefined, [true]);
    const second = create(accepted);
    await second.table.confirmDelete(municipalities[0]);
    expect(second.municipalitiesApi.delete).toHaveBeenCalledWith(2);
    expect(second.changed).toHaveBeenCalled();
  });
});
