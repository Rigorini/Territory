import { TestBed } from '@angular/core/testing';
import { HttpErrorResponse } from '@angular/common/http';
import { provideTranslateService } from '@ngx-translate/core';
import { AssignmentsApi, OpenAssignmentDto, TerritoryStatusDto } from './assignments.api';
import { AssignmentsPage } from './assignments-page';
import { AssignDialog } from './assign-dialog';
import { HistoryDialog } from './history-dialog';
import { ReturnDialog } from './return-dialog';
import { ShareDialog } from './share-dialog';
import { PublishersApi } from '../publishers/publishers.api';
import { Feedback } from '../shared/feedback';
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
  assign(t: TerritoryStatusDto): Promise<void>;
  share(t: TerritoryStatusDto, a: OpenAssignmentDto): Promise<void>;
  registerReturn(t: TerritoryStatusDto, a: OpenAssignmentDto): Promise<void>;
  showHistory(t: TerritoryStatusDto): Promise<void>;
}

async function setup(options: { dialogResult?: unknown; confirmed?: boolean; publishers?: unknown } = {}) {
  const api = { list: vi.fn().mockResolvedValue(statuses), renewLink: vi.fn().mockResolvedValue(link) };
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
  it('lists territories by municipality and number with status actions', async () => {
    const { page, fixture } = await setup();

    expect(page.table.rows().map(s => s.territoryId)).toEqual([3, 2, 1]);
    const rows = [...fixture.nativeElement.querySelectorAll('tr.mat-mdc-row')] as HTMLElement[];
    expect(rows.length).toBe(3);
    expect(rows[2].querySelector('.chip')!.classList).toContain('assigned');
    expect(rows[2].querySelectorAll('button').length).toBe(3);
    expect(rows[0].querySelectorAll('button').length).toBe(2);

    page.table.onSort({ active: 'date', direction: 'desc' });
    expect(page.table.rows().map(s => s.territoryId)).toEqual([1, 2, 3]);
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

    page.table.setFilter(column, value);

    expect(page.table.rows().map(s => s.territoryId)).toEqual(ids);
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

    expect(feedback.open).toHaveBeenCalledWith(HistoryDialog, { territory: statuses[1] });
    expect(api.list).toHaveBeenCalledTimes(loads + 1);
  });
});
