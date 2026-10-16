import { TestBed } from '@angular/core/testing';
import { provideTranslateService } from '@ngx-translate/core';
import { PublisherDto, PublishersApi } from './publishers.api';
import { PublishersPage } from './publishers-page';
import { PublisherDialog } from './publisher-dialog';
import { By } from '@angular/platform-browser';
import { MatPaginator } from '@angular/material/paginator';
import { Feedback } from '../shared/feedback';
import { Paging } from '../shared/paging';
import { TableState } from '../shared/table-state';

const publishers: PublisherDto[] = [
  { id: 1, firstName: 'Marco', lastName: 'Rossi' },
  { id: 2, firstName: 'Anna', lastName: 'Rossi' },
  { id: 3, firstName: 'Luca', lastName: 'Bianchi' },
];

interface PageApi {
  table: TableState<PublisherDto>;
  paging: Paging<PublisherDto>;
  openDialog(publisher?: PublisherDto): Promise<void>;
  confirmDelete(publisher: PublisherDto): Promise<void>;
}

async function setup(dialogResult: unknown, confirmed = true, items: PublisherDto[] = publishers) {
  const api = { list: vi.fn().mockResolvedValue(items), delete: vi.fn().mockResolvedValue(undefined) };
  const feedback = {
    open: vi.fn().mockResolvedValue(dialogResult),
    confirm: vi.fn().mockResolvedValue(confirmed),
    notify: vi.fn(),
    notifyError: vi.fn(),
    attempt: vi.fn(async (action: () => Promise<unknown>) => {
      await action();
      return true;
    }),
  };
  TestBed.configureTestingModule({
    imports: [PublishersPage],
    providers: [provideTranslateService(), { provide: PublishersApi, useValue: api }, { provide: Feedback, useValue: feedback }],
  });
  const fixture = TestBed.createComponent(PublishersPage);
  fixture.detectChanges();
  await fixture.whenStable();
  fixture.detectChanges();
  return { api, feedback, fixture, page: fixture.componentInstance as unknown as PageApi };
}

describe('PublishersPage', () => {
  it('lists publishers by last name, then first name', async () => {
    const { page, fixture } = await setup(undefined);

    expect(page.table.rows().map(p => p.id)).toEqual([3, 2, 1]);
    expect(fixture.nativeElement.querySelectorAll('tr.mat-mdc-row').length).toBe(3);

    page.table.onSort({ active: 'firstName', direction: 'asc' });
    expect(page.table.rows().map(p => p.id)).toEqual([2, 3, 1]);
  });

  it('paginates in pages of ten, twenty-five and fifty', async () => {
    const many = Array.from({ length: 30 }, (_, i) => ({
      id: i + 1, firstName: `Nome ${i + 1}`, lastName: `Cognome ${String(i + 1).padStart(2, '0')}`,
    }));
    const { fixture, page } = await setup(undefined, true, many);
    const paginator = fixture.debugElement.query(By.directive(MatPaginator)).injector.get(MatPaginator);
    const shown = () => (fixture.nativeElement as HTMLElement).querySelectorAll('tr.mat-mdc-row').length;

    expect(paginator.length).toBe(30);
    expect(paginator.pageSizeOptions).toEqual([10, 25, 50]);
    expect(paginator.pageSize).toBe(10);
    expect(shown()).toBe(10);

    page.paging.onPage({ pageIndex: 0, pageSize: 50 });
    fixture.detectChanges();
    expect(shown()).toBe(30);

    page.paging.onPage({ pageIndex: 2, pageSize: 10 });
    fixture.detectChanges();
    expect(shown()).toBe(10);
    expect((fixture.nativeElement as HTMLElement).textContent).toContain('Cognome 21');

    // Filtering shortens the list below the current page: it follows instead of staying empty.
    page.table.setFilter('lastName', 'Cognome 0');
    fixture.detectChanges();
    expect(page.paging.index()).toBe(0);
    expect(shown()).toBe(9);
  });

  it('filters by each column', async () => {
    const { page } = await setup(undefined);

    page.table.setFilter('lastName', 'ross');
    page.table.setFilter('firstName', 'an');
    expect(page.table.rows().map(p => p.id)).toEqual([2]);
  });

  it.each([
    [undefined, { reactivated: false }, 'publishers.created'],
    [undefined, { reactivated: true }, 'publishers.reactivated'],
    [publishers[0], { reactivated: false }, 'publishers.updated'],
  ])('notifies the save outcome', async (publisher, result, message) => {
    const { api, feedback, page } = await setup(result);

    await page.openDialog(publisher);

    expect(feedback.open).toHaveBeenCalledWith(PublisherDialog, { publisher });
    expect(feedback.notify).toHaveBeenCalledWith(message);
    expect(api.list).toHaveBeenCalledTimes(2);
  });

  it('ignores a dismissed dialog', async () => {
    const { api, feedback, page } = await setup(undefined);

    await page.openDialog();

    expect(feedback.notify).not.toHaveBeenCalled();
    expect(api.list).toHaveBeenCalledTimes(1);
  });

  it('deletes after confirmation only', async () => {
    const confirmed = await setup(undefined, true);
    await confirmed.page.confirmDelete(publishers[0]);
    expect(confirmed.api.delete).toHaveBeenCalledWith(1);
    expect(confirmed.feedback.attempt).toHaveBeenCalledWith(expect.any(Function), 'publishers.deleted');

    TestBed.resetTestingModule();
    const cancelled = await setup(undefined, false);
    await cancelled.page.confirmDelete(publishers[0]);
    expect(cancelled.api.delete).not.toHaveBeenCalled();
  });
});
