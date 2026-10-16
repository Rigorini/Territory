import { TestBed } from '@angular/core/testing';
import { By } from '@angular/platform-browser';
import { HttpErrorResponse } from '@angular/common/http';
import { MatPaginator } from '@angular/material/paginator';
import { provideTranslateService } from '@ngx-translate/core';
import { AssignmentsApi, OpenAssignmentDto, TerritoryStatusDto } from './assignments.api';
import { AssignmentsPage } from './assignments-page';
import { AssignDialog } from './assign-dialog';
import { HISTORY_DIALOG_CONFIG, HistoryDialog } from './history-dialog';
import { ReturnDialog } from './return-dialog';
import { ShareDialog } from './share-dialog';
import { PublishersApi } from '../publishers/publishers.api';
import { Feedback } from '../shared/feedback';
import { Paging } from '../shared/paging';
import { TableState } from '../shared/table-state';

const open: OpenAssignmentDto = { id: 7, publisherId: 2, publisherName: 'Rossi Mario', assignedAt: '2025-03-04T10:00:00Z' };
const statuses: TerritoryStatusDto[] = [
  { territoryId: 1, municipalityId: 1, municipalityName: 'Giano', number: 2, name: 'Centro', assignment: open, lastReturnedAt: null, assignmentsLastYear: 0 },
  { territoryId: 2, municipalityId: 1, municipalityName: 'Giano', number: 1, name: 'Borgo', assignment: null, lastReturnedAt: '2024-11-20T10:00:00Z', assignmentsLastYear: 0 },
  { territoryId: 3, municipalityId: 2, municipalityName: 'Bastardo', number: 1, name: 'Piazza', assignment: null, lastReturnedAt: null, assignmentsLastYear: 0 },
];
const publishers = [{ id: 2, firstName: 'Mario', lastName: 'Rossi' }];
const link = { assignmentId: 9, token: 'tok' };

interface PageApi {
  table: TableState<TerritoryStatusDto>;
  descending(): boolean;
  sortBy(active: string): void;
  toggleSortDirection(): void;
  assign(t: TerritoryStatusDto): Promise<void>;
  share(t: TerritoryStatusDto, a: OpenAssignmentDto): Promise<void>;
  registerReturn(t: TerritoryStatusDto, a: OpenAssignmentDto): Promise<void>;
  showHistory(t: TerritoryStatusDto): Promise<void>;
}

async function setup(
  options: { dialogResult?: unknown; confirmed?: boolean; publishers?: unknown; territories?: TerritoryStatusDto[] } = {},
) {
  const api = { list: vi.fn().mockResolvedValue(options.territories ?? statuses), renewLink: vi.fn().mockResolvedValue(link) };
  const publishersApi = { list: vi.fn().mockResolvedValue(options.publishers ?? publishers) };
  const feedback = {
    open: vi.fn().mockResolvedValue(options.dialogResult),
    confirm: vi.fn().mockResolvedValue(options.confirmed ?? true),
    notify: vi.fn(),
    notifyError: vi.fn(),
  };
  TestBed.configureTestingModule({
    imports: [AssignmentsPage],
    providers: [
      provideTranslateService(),
      { provide: AssignmentsApi, useValue: api },
      { provide: PublishersApi, useValue: publishersApi },
      { provide: Feedback, useValue: feedback },
    ],
  });
  const fixture = TestBed.createComponent(AssignmentsPage);
  fixture.detectChanges();
  await fixture.whenStable();
  fixture.detectChanges();
  return { api, publishersApi, feedback, fixture, page: fixture.componentInstance as unknown as PageApi };
}

describe('AssignmentsPage', () => {
  it('lists the most recently moved territories first, with status actions', async () => {
    const { page, fixture } = await setup();

    expect(page.table.sort()).toEqual({ active: 'date', direction: 'desc' });
    expect(page.table.rows().map(s => s.territoryId)).toEqual([1, 2, 3]);
    const rows = [...fixture.nativeElement.querySelectorAll('tr.mat-mdc-row')] as HTMLElement[];
    expect(rows.length).toBe(3);
    expect(rows[0].querySelector('.chip')!.classList).toContain('assigned');
    expect(rows[0].querySelectorAll('button').length).toBe(3);
    expect(rows[2].querySelectorAll('button').length).toBe(2);

    page.table.onSort({ active: 'municipality', direction: 'asc' });
    expect(page.table.rows().map(s => s.territoryId)).toEqual([3, 2, 1]);
    page.table.onSort({ active: 'status', direction: 'asc' });
    expect(page.table.rows().map(s => s.territoryId)).toEqual([1, 3, 2]);
  });

  it.each([
    ['status', 'available', [3, 2]],
    ['municipality', 'Giano', [2, 1]],
    ['publisher', 'ross', [1]],
    ['date', '2024', [2]],
    ['name', 'piaz', [3]],
    ['number', '1', [3, 2]],
  ])('filters by %s', async (column, value, ids) => {
    const { page } = await setup();

    // The expected order is by municipality, independent of the default sorting.
    page.table.onSort({ active: 'municipality', direction: 'asc' });
    page.table.setFilter(column, value);

    expect(page.table.rows().map(s => s.territoryId)).toEqual(ids);
  });

  it('shows the time elapsed since the date, between the date and the average', async () => {
    const { fixture } = await setup();
    const rows = [...fixture.nativeElement.querySelectorAll('tr.mat-mdc-row')] as HTMLElement[];
    const labels = (row: HTMLElement) =>
      [...row.querySelectorAll('td')].map(td => td.getAttribute('data-label'));
    const cells = [...fixture.nativeElement.querySelectorAll('td[data-label="assignments.elapsed"]')] as HTMLElement[];

    expect(labels(rows[0]).indexOf('assignments.elapsed'))
      .toBe(labels(rows[0]).indexOf('assignments.frequency') - 1);
    expect(labels(rows[0]).indexOf('assignments.elapsed'))
      .toBeGreaterThan(labels(rows[0]).indexOf('assignments.date'));
    // Translations are not loaded in tests, so the cells show the key; the last territory has no date.
    expect(cells.map(cell => cell.textContent!.trim()))
      .toEqual(['assignments.elapsedValue', 'assignments.elapsedValue', '—']);
  });

  it('sorts by the time elapsed', async () => {
    const { page } = await setup();

    page.table.onSort({ active: 'elapsed', direction: 'asc' });

    expect(page.table.rows().map(s => s.territoryId)).toEqual([1, 2, 3]);
    page.table.onSort({ active: 'elapsed', direction: 'desc' });
    expect(page.table.rows()[0].territoryId).toBe(3);
  });

  it('paginates in pages of ten, twenty-five and fifty', async () => {
    const many = Array.from({ length: 24 }, (_, i) => ({
      ...statuses[0], territoryId: i + 1, number: i + 1, name: `T${i + 1}`,
    }));
    const { fixture, page } = await setup({ territories: many });
    const paginator = fixture.debugElement.query(By.directive(MatPaginator)).injector.get(MatPaginator);
    const paging = (page as unknown as { paging: Paging<TerritoryStatusDto> }).paging;
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
    page.table.setFilter('name', 'T1');
    fixture.detectChanges();
    expect(paging.index()).toBe(1);
    expect(shown()).toBe(1);
  });

  it('shows the sorting and filter controls only on phones', async () => {
    const { fixture } = await setup();
    const head = (fixture.nativeElement as HTMLElement).querySelector('.page-head') as HTMLElement;
    const controls = head.querySelector('.mobile-sort') as HTMLElement;

    // mobile-flex hides the block by default: only the phone media query shows it.
    expect(controls.classList).toContain('mobile-flex');
    expect(controls.querySelector('mat-select')).not.toBeNull();
    expect(controls.textContent).toContain('common.filters');
    expect(head.querySelectorAll(':scope > button').length).toBe(0);
  });

  it('keeps the municipality and status filters visible when the phone panel is collapsed', async () => {
    const { fixture } = await setup();
    const table = fixture.nativeElement.querySelector('table') as HTMLElement;
    const pinned = [...fixture.nativeElement.querySelectorAll('th.pinned')] as HTMLElement[];

    expect(table.classList).toContain('pinned-filters');
    expect(pinned.map(th => th.getAttribute('data-label')))
      .toEqual(['territories.municipality', 'assignments.status']);
    // Both are full width on a phone: the municipality select and the row of status buttons.
    expect(pinned.every(th => th.classList.contains('wide'))).toBe(true);
  });

  it('filters the status from the phone buttons', async () => {
    const { fixture, page } = await setup();
    const buttons = [...fixture.nativeElement.querySelectorAll('.status-button')] as HTMLButtonElement[];
    const selected = () => buttons.filter(b => b.classList.contains('selected')).map(b => b.getAttribute('data-status'));
    const click = (status: string) => {
      buttons.find(b => b.getAttribute('data-status') === status)!.click();
      fixture.detectChanges();
    };

    expect(buttons.map(b => b.getAttribute('data-status'))).toEqual(['all', 'assigned', 'available']);
    expect(buttons.map(b => b.getAttribute('aria-pressed'))).toEqual(['true', 'false', 'false']);

    click('assigned');
    expect(page.table.filter('status')).toBe('assigned');
    expect(selected()).toEqual(['assigned']);
    expect(page.table.rows().map(s => s.territoryId)).toEqual([1]);

    click('all');
    expect(page.table.filter('status')).toBe('');
    expect(selected()).toEqual(['all']);
    expect(page.table.rows().length).toBe(3);
  });

  it('sorts by date by default and flips the direction from the phone controls', async () => {
    const { page } = await setup();

    expect(page.descending()).toBe(true);

    page.toggleSortDirection();
    expect(page.table.sort()).toEqual({ active: 'date', direction: 'asc' });
    expect(page.table.rows().map(s => s.territoryId)).toEqual([3, 2, 1]);
    expect(page.descending()).toBe(false);

    page.sortBy('municipality');
    expect(page.table.sort()).toEqual({ active: 'municipality', direction: 'asc' });
  });

  it('sorts by the remaining columns', async () => {
    const { page } = await setup();

    page.table.onSort({ active: 'publisher', direction: 'desc' });
    expect(page.table.rows()[0].territoryId).toBe(1);
    page.table.onSort({ active: 'name', direction: 'asc' });
    expect(page.table.rows()[0].territoryId).toBe(2);
    page.table.onSort({ active: 'number', direction: 'desc' });
    expect(page.table.rows()[0].territoryId).toBe(1);
  });

  it('assigns, reloads and opens the share dialog', async () => {
    const { api, feedback, page } = await setup({ dialogResult: link });

    await page.assign(statuses[1]);

    expect(feedback.open).toHaveBeenNthCalledWith(1, AssignDialog, { territory: statuses[1], publishers });
    expect(feedback.notify).toHaveBeenCalledWith('assignments.assigned');
    expect(api.list).toHaveBeenCalledTimes(2);
    expect(feedback.open).toHaveBeenNthCalledWith(2, ShareDialog, { territory: statuses[1], token: 'tok' });
  });

  it('does not assign without publishers or when cancelled', async () => {
    const empty = await setup({ publishers: [] });
    await empty.page.assign(statuses[1]);
    expect(empty.feedback.notify).toHaveBeenCalledWith('assignments.noPublishers');
    expect(empty.feedback.open).not.toHaveBeenCalled();

    TestBed.resetTestingModule();
    const failing = await setup();
    const error = new HttpErrorResponse({ status: 500 });
    failing.publishersApi.list.mockRejectedValue(error);
    await failing.page.assign(statuses[1]);
    expect(failing.feedback.notifyError).toHaveBeenCalledWith(error);

    TestBed.resetTestingModule();
    const cancelled = await setup();
    await cancelled.page.assign(statuses[1]);
    expect(cancelled.feedback.open).toHaveBeenCalledTimes(1);
    expect(cancelled.api.list).toHaveBeenCalledTimes(1);
  });

  it('renews the link after confirmation before sharing', async () => {
    const { api, feedback, page } = await setup();

    await page.share(statuses[0], open);

    expect(feedback.confirm).toHaveBeenCalledWith(expect.objectContaining({ titleKey: 'assignments.renewTitle' }));
    expect(api.renewLink).toHaveBeenCalledWith(7);
    expect(feedback.open).toHaveBeenCalledWith(ShareDialog, { territory: statuses[0], token: 'tok' });
  });

  it('keeps the current link when the renewal is cancelled, and reloads on errors', async () => {
    const cancelled = await setup({ confirmed: false });
    await cancelled.page.share(statuses[0], open);
    expect(cancelled.api.renewLink).not.toHaveBeenCalled();

    TestBed.resetTestingModule();
    const failing = await setup();
    const error = new HttpErrorResponse({ status: 409, error: { code: 'assignments.alreadyReturned' } });
    failing.api.renewLink.mockRejectedValue(error);
    await failing.page.share(statuses[0], open);
    expect(failing.feedback.notifyError).toHaveBeenCalledWith(error);
    expect(failing.api.list).toHaveBeenCalledTimes(2);
  });

  it.each([[true, 2], [undefined, 1]])('registers a return', async (result, loads) => {
    const { api, feedback, page } = await setup({ dialogResult: result });

    await page.registerReturn(statuses[0], open);

    expect(feedback.open).toHaveBeenCalledWith(ReturnDialog, { territory: statuses[0], assignment: open });
    expect(api.list).toHaveBeenCalledTimes(loads);
  });

  it('opens the history and reloads the statuses afterwards', async () => {
    const { api, feedback, page } = await setup();
    const loads = api.list.mock.calls.length;

    await page.showHistory(statuses[1]);

    expect(feedback.open).toHaveBeenCalledWith(HistoryDialog, { territory: statuses[1] }, HISTORY_DIALOG_CONFIG);
    expect(api.list).toHaveBeenCalledTimes(loads + 1);
  });
});
