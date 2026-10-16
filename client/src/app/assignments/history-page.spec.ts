import { TestBed } from '@angular/core/testing';
import { By } from '@angular/platform-browser';
import { provideRouter, Router } from '@angular/router';
import { MatPaginator } from '@angular/material/paginator';
import { provideTranslateService } from '@ngx-translate/core';
import { AssignmentHistoryDto, AssignmentsApi, TerritoryStatusDto } from './assignments.api';
import { EditDatesDialog } from './edit-dates-dialog';
import { HistoryPage } from './history-page';
import { Feedback } from '../shared/feedback';

const territory: TerritoryStatusDto = {
  territoryId: 1, municipalityId: 1, municipalityName: 'Giano', number: 3, name: 'Centro',
  mapUrl: 'https://www.google.com/maps/d/viewer?mid=map-1',
  assignment: null, lastReturnedAt: null, assignmentsLastYear: 0,
};

function entries(count: number): AssignmentHistoryDto[] {
  return Array.from({ length: count }, (_, i) => ({
    id: 100 - i, publisherName: `Proclamatore ${i + 1}`, assignedAt: '2025-03-04T10:00:00Z',
    returnedAt: null, returnedBy: null, returnedByName: null,
  }));
}

interface PageApi {
  territory(): TerritoryStatusDto | null;
  paging: {
    index(): number;
    rows(): AssignmentHistoryDto[];
    onPage(event: { pageIndex: number; pageSize: number }): void;
  };
  editDates(entry: AssignmentHistoryDto): Promise<void>;
  confirmDelete(entry: AssignmentHistoryDto): Promise<void>;
  back(): void;
}

async function setup(items: AssignmentHistoryDto[]) {
  const api = {
    list: vi.fn().mockResolvedValue([territory]),
    history: vi.fn().mockResolvedValue(items),
    delete: vi.fn().mockResolvedValue(undefined),
  };
  const feedback = {
    open: vi.fn(), notify: vi.fn(), notifyError: vi.fn(), confirm: vi.fn().mockResolvedValue(true),
    attempt: vi.fn(async (action: () => Promise<unknown>) => { await action(); return true; }),
  };
  TestBed.configureTestingModule({
    imports: [HistoryPage],
    providers: [
      provideTranslateService(), provideRouter([]),
      { provide: AssignmentsApi, useValue: api },
      { provide: Feedback, useValue: feedback },
    ],
  });
  const fixture = TestBed.createComponent(HistoryPage);
  fixture.componentRef.setInput('territoryId', 1);
  fixture.detectChanges();
  await fixture.whenStable();
  fixture.detectChanges();
  return { api, feedback, fixture, page: fixture.componentInstance as unknown as PageApi };
}

function rows(fixture: { nativeElement: HTMLElement }): HTMLElement[] {
  return [...fixture.nativeElement.querySelectorAll('tr.mat-mdc-row')] as HTMLElement[];
}

describe('HistoryPage', () => {
  it('lists the whole history in pages of ten', async () => {
    const { api, fixture, page } = await setup(entries(12));
    const paginator = fixture.debugElement.query(By.directive(MatPaginator)).injector.get(MatPaginator);

    expect(api.history).toHaveBeenCalledWith(1);
    expect(page.territory()?.name).toBe('Centro');
    expect(rows(fixture).length).toBe(10);
    expect(paginator.length).toBe(12);
    expect(paginator.pageSizeOptions).toEqual([10, 25, 50]);

    page.paging.onPage({ pageIndex: 1, pageSize: 10 });
    fixture.detectChanges();

    expect(rows(fixture).length).toBe(2);
    expect(rows(fixture)[0].textContent).toContain('Proclamatore 11');

    page.paging.onPage({ pageIndex: 0, pageSize: 25 });
    fixture.detectChanges();

    expect(rows(fixture).length).toBe(12);
  });

  it('corrects dates with the neighbours as bounds and reloads on success', async () => {
    const items = entries(3);
    const { api, feedback, page } = await setup(items);
    const loads = api.history.mock.calls.length;

    feedback.open.mockResolvedValueOnce(undefined).mockResolvedValueOnce(true);
    await page.editDates(items[1]);
    expect(feedback.open).toHaveBeenCalledWith(EditDatesDialog, {
      territory, entry: items[1], previous: items[2], next: items[0],
    });
    expect(api.history).toHaveBeenCalledTimes(loads);

    await page.editDates(items[1]);

    expect(feedback.notify).toHaveBeenCalledWith('assignments.datesUpdated');
    expect(api.history).toHaveBeenCalledTimes(loads + 1);
  });

  it('falls back to the last page when a deletion empties the current one', async () => {
    const { api, feedback, fixture, page } = await setup(entries(12));
    page.paging.onPage({ pageIndex: 1, pageSize: 10 });
    fixture.detectChanges();
    api.history.mockResolvedValue(entries(10));

    await page.confirmDelete(entries(12)[10]);
    fixture.detectChanges();

    expect(feedback.confirm).toHaveBeenCalled();
    expect(api.delete).toHaveBeenCalledWith(90);
    expect(page.paging.index()).toBe(0);
    expect(rows(fixture).length).toBe(10);
  });

  it('goes back to the assignments board, from a phone-only button', async () => {
    const { fixture, page } = await setup(entries(1));
    const navigate = vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);
    const back = [...fixture.nativeElement.querySelectorAll('button')] as HTMLButtonElement[];

    // mobile-only: the shell navigation is enough on a desktop.
    expect(back.find(b => b.textContent!.includes('common.back'))!.classList).toContain('mobile-only');

    page.back();

    expect(navigate).toHaveBeenCalledWith(['/admin/assignments']);
    expect(rows(fixture).length).toBe(1);
  });
});
