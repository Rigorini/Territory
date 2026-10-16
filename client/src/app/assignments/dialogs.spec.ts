import { TestBed } from '@angular/core/testing';
import { Type } from '@angular/core';
import { By } from '@angular/platform-browser';
import { provideRouter, Router } from '@angular/router';
import { HttpErrorResponse } from '@angular/common/http';
import { Clipboard } from '@angular/cdk/clipboard';
import { MatAutocomplete } from '@angular/material/autocomplete';
import { MAT_DIALOG_DATA, MatDialogRef } from '@angular/material/dialog';
import { provideTranslateService } from '@ngx-translate/core';
import { AssignmentsApi, TerritoryStatusDto } from './assignments.api';
import { AssignDialog, AssignDialogData } from './assign-dialog';
import { ReturnDialog, ReturnDialogData } from './return-dialog';
import { ShareDialog, ShareDialogData } from './share-dialog';
import { HistoryDialog } from './history-dialog';
import { EditDatesDialog, EditDatesDialogData } from './edit-dates-dialog';
import { localDay } from './event-date';
import { Feedback } from '../shared/feedback';
import { provideDayDateAdapter } from '../shared/day-date-adapter';

const assignment = { id: 7, publisherId: 2, publisherName: 'Rossi Mario', assignedAt: '2025-03-04T10:00:00Z' };
const territory: TerritoryStatusDto = {
  territoryId: 1, municipalityId: 1, municipalityName: 'Giano', number: 3, name: 'Centro',
  assignment: null, lastReturnedAt: '2025-01-10T10:00:00Z', assignmentsLastYear: 0,
};

interface DialogApi {
  form: { controls: Record<string, { setValue(v: unknown): void; hasError(code: string): boolean }> };
  save(): Promise<void>;
  serverErrors(control: string): string[];
  errorKey(): string | null;
}

function setup<T>(component: Type<T>, data: unknown, extra: { provide: unknown; useValue: unknown }[] = []) {
  const api = {
    assign: vi.fn(), registerReturn: vi.fn().mockResolvedValue(undefined), history: vi.fn().mockResolvedValue([]),
    updateDates: vi.fn().mockResolvedValue(undefined), delete: vi.fn().mockResolvedValue(undefined),
  };
  const dialogRef = { close: vi.fn() };
  const feedback = {
    notify: vi.fn(), notifyError: vi.fn(), open: vi.fn(), confirm: vi.fn().mockResolvedValue(true),
    attempt: vi.fn(async (action: () => Promise<unknown>) => { await action(); return true; }),
  };
  TestBed.configureTestingModule({
    imports: [component],
    providers: [
      provideTranslateService(),
      provideDayDateAdapter(),
      provideRouter([]),
      { provide: AssignmentsApi, useValue: api },
      { provide: Feedback, useValue: feedback },
      { provide: MatDialogRef, useValue: dialogRef },
      { provide: MAT_DIALOG_DATA, useValue: data },
      ...extra,
    ],
  });
  const fixture = TestBed.createComponent(component);
  fixture.detectChanges();
  return { api, dialogRef, feedback, fixture, component: fixture.componentInstance };
}

describe('AssignDialog', () => {
  const data: AssignDialogData = { territory, publishers: [{ id: 2, firstName: 'Mario', lastName: 'Rossi' }] };
  const many: AssignDialogData = {
    ...data,
    publishers: [
      { id: 2, firstName: 'Mario', lastName: 'Rossi' },
      { id: 3, firstName: 'Anna', lastName: 'Bianchi' },
      { id: 4, firstName: 'Luca', lastName: 'Verdi' },
    ],
  };

  function setupWithPublishers(dialogData: AssignDialogData) {
    const result = setup(AssignDialog, dialogData);
    const element = result.fixture.nativeElement as HTMLElement;
    const input = element.querySelector('input') as HTMLInputElement;
    const autocomplete = result.fixture.debugElement.query(By.directive(MatAutocomplete))
      .injector.get(MatAutocomplete);
    return {
      ...result,
      input,
      autocomplete,
      options: () => (result.component as unknown as { publishers(): readonly { id: number }[] })
        .publishers().map(p => p.id),
      type: (text: string) => {
        input.value = text;
        input.dispatchEvent(new Event('input'));
        result.fixture.detectChanges();
      },
    };
  }

  it('filters the publishers as the user types', () => {
    const { autocomplete, options, type } = setupWithPublishers(many);

    expect(autocomplete.requireSelection).toBe(true);
    expect(options()).toEqual([2, 3, 4]);
    type('anna');
    expect(options()).toEqual([3]);
    type('  RossI  ');
    expect(options()).toEqual([2]);
    type('nessuno');
    expect(options()).toEqual([]);
  });

  it('shows the selected publisher in the field', async () => {
    const { component, fixture, input } = setupWithPublishers(data);

    (component as unknown as DialogApi).form.controls['publisherId'].setValue(2);
    await fixture.whenStable();
    fixture.detectChanges();

    expect(input.value).toBe('Rossi Mario');
  });

  it('requires a publisher and rejects days before the last return', async () => {
    const { api, component } = setup(AssignDialog, data);
    const dialog = component as unknown as DialogApi;

    await dialog.save();
    dialog.form.controls['publisherId'].setValue(2);
    dialog.form.controls['assignedAt'].setValue(new Date(2025, 0, 9));
    await dialog.save();

    expect(dialog.form.controls['assignedAt'].hasError('beforeLastReturn')).toBe(true);
    expect(api.assign).not.toHaveBeenCalled();
  });

  it('assigns today and closes with the share link', async () => {
    const { api, component, dialogRef } = setup(AssignDialog, data);
    const dialog = component as unknown as DialogApi;
    const link = { assignmentId: 9, token: 'tok' };
    api.assign.mockResolvedValue(link);
    dialog.form.controls['publisherId'].setValue(2);

    await dialog.save();

    expect(api.assign).toHaveBeenCalledWith({ territoryId: 1, publisherId: 2, assignedAt: expect.any(String) });
    expect(localDay(new Date(api.assign.mock.calls[0][0].assignedAt))).toBe(localDay(new Date()));
    expect(dialogRef.close).toHaveBeenCalledWith(link);
  });

  it('shows a conflict as a dialog error', async () => {
    const { api, component } = setup(AssignDialog, { ...data, territory: { ...territory, lastReturnedAt: null } });
    const dialog = component as unknown as DialogApi;
    api.assign.mockRejectedValue(new HttpErrorResponse({ status: 409, error: { code: 'assignments.alreadyAssigned' } }));
    dialog.form.controls['publisherId'].setValue(2);

    await dialog.save();

    expect(dialog.errorKey()).toBe('errors.codes.assignments.alreadyAssigned');
  });
});

describe('ReturnDialog', () => {
  const data: ReturnDialogData = { territory: { ...territory, assignment }, assignment };

  it('registers the return of a past day', async () => {
    const { api, component, dialogRef } = setup(ReturnDialog, data);
    const dialog = component as unknown as DialogApi;
    dialog.form.controls['returnedAt'].setValue(new Date(2025, 2, 5));

    await dialog.save();

    expect(api.registerReturn).toHaveBeenCalledWith(7, new Date(2025, 2, 5, 23, 59, 59).toISOString());
    expect(dialogRef.close).toHaveBeenCalledWith(true);
  });

  it('reads typed days as dd/MM/yyyy and rejects impossible ones', async () => {
    const { api, component, fixture } = setup(ReturnDialog, data);
    const dialog = component as unknown as DialogApi;
    const input = (fixture.nativeElement as HTMLElement).querySelector('input') as HTMLInputElement;
    const type = (text: string) => {
      input.value = text;
      input.dispatchEvent(new Event('input'));
      fixture.detectChanges();
    };

    type('31/02/2025');
    await dialog.save();
    fixture.detectChanges();
    expect(dialog.form.controls['returnedAt'].hasError('matDatepickerParse')).toBe(true);
    expect((fixture.nativeElement as HTMLElement).textContent).toContain('errors.fields.invalidDate');
    expect(api.registerReturn).not.toHaveBeenCalled();

    type('05/03/2025');
    await dialog.save();
    expect(api.registerReturn).toHaveBeenCalledWith(7, new Date(2025, 2, 5, 23, 59, 59).toISOString());
  });

  it('shows server field errors', async () => {
    const { api, component } = setup(ReturnDialog, data);
    const dialog = component as unknown as DialogApi;
    api.registerReturn.mockRejectedValue(new HttpErrorResponse({
      status: 400, error: { code: 'validation', errors: { returnedAt: ['beforeAssigned'] } },
    }));

    await dialog.save();

    expect(dialog.serverErrors('returnedAt')).toEqual(['beforeAssigned']);
  });
});

describe('ShareDialog', () => {
  const data: ShareDialogData = { territory, token: 'Ab-_9' };
  interface ShareApi { url: string; canShare: boolean; share(): Promise<void>; copy(): void }

  const browserNavigator = document.defaultView!.navigator;
  afterEach(() => Reflect.deleteProperty(browserNavigator, 'share'));

  function create(copyResult = true) {
    const clipboard = { copy: vi.fn().mockReturnValue(copyResult) };
    const result = setup(ShareDialog, data, [{ provide: Clipboard, useValue: clipboard }]);
    return { ...result, clipboard, dialog: result.component as unknown as ShareApi };
  }

  it('shows the link and messaging buttons', () => {
    const { dialog, fixture } = create();
    const links = [...fixture.nativeElement.querySelectorAll('a')] as HTMLAnchorElement[];

    expect(dialog.url).toBe(`${location.origin}/t/Ab-_9`);
    expect(links.map(a => a.rel)).toEqual(['noopener noreferrer', 'noopener noreferrer']);
    expect(links[0].href).toContain('https://wa.me/');
    expect(links[1].href).toContain('https://t.me/share/url');
  });

  it.each([[true, 'assignments.copied'], [false, 'assignments.copyFailed']])('copies the link', (ok, message) => {
    const { clipboard, dialog, feedback } = create(ok);

    dialog.copy();

    expect(clipboard.copy).toHaveBeenCalledWith(dialog.url);
    expect(feedback.notify).toHaveBeenCalledWith(message);
  });

  it('uses the system share sheet and ignores a cancelled one', async () => {
    const share = vi.fn()
      .mockResolvedValueOnce(undefined)
      .mockRejectedValueOnce(new DOMException('cancel', 'AbortError'))
      .mockRejectedValueOnce(new Error('denied'));
    Object.defineProperty(browserNavigator, 'share', { value: share, configurable: true });
    const { dialog, feedback } = create();

    expect(dialog.canShare).toBe(true);
    await dialog.share();
    await dialog.share();
    expect(feedback.notify).not.toHaveBeenCalled();

    await dialog.share();
    expect(share).toHaveBeenCalledWith({ text: expect.any(String), url: dialog.url });
    expect(feedback.notify).toHaveBeenCalledWith('assignments.shareFailed');
  });
});

interface HistoryDialogApi {
  history: {
    editDates(entry: unknown): Promise<void>;
    confirmDelete(entry: unknown): Promise<void>;
  };
  ngOnInit(): Promise<void>;
}

describe('HistoryDialog', () => {
  it('loads the history and formats dates', async () => {
    const { api, component, fixture } = setup(HistoryDialog, { territory });
    await (component as unknown as { ngOnInit(): Promise<void> }).ngOnInit();
    fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toContain('assignments.historyEmpty');

    TestBed.resetTestingModule();
    const filled = setup(HistoryDialog, { territory });
    filled.api.history.mockResolvedValue([
      { id: 2, publisherName: 'Rossi Mario', assignedAt: '2025-03-04T10:00:00Z', returnedAt: null, returnedBy: null, returnedByName: null },
      { id: 1, publisherName: 'Bianchi Anna', assignedAt: '2025-01-04T10:00:00Z', returnedAt: '2025-01-10T10:00:00Z', returnedBy: 'admin', returnedByName: 'Mario Rossi' },
      { id: 0, publisherName: 'Neri Carla', assignedAt: '2024-12-01T10:00:00Z', returnedAt: '2024-12-10T10:00:00Z', returnedBy: 'admin', returnedByName: null },
    ]);
    await (filled.component as unknown as { ngOnInit(): Promise<void> }).ngOnInit();
    filled.fixture.detectChanges();

    expect(api.history).toHaveBeenCalledWith(1);
    const rows = [...filled.fixture.nativeElement.querySelectorAll('tbody tr')] as HTMLElement[];
    expect(rows.length).toBe(3);
    expect(rows[0].textContent).toContain('—');
    expect(rows[1].textContent).toContain('Mario Rossi');
    expect(rows[1].textContent).not.toContain('assignments.returnedByValues.admin');
    expect(rows[2].textContent).toContain('assignments.returnedByValues.admin');
  });

  it('corrects dates with the neighbours as bounds and reloads on success', async () => {
    const items = [
      { id: 3, publisherName: 'Rossi Mario', assignedAt: '2025-03-04T10:00:00Z', returnedAt: null, returnedBy: null, returnedByName: null },
      { id: 2, publisherName: 'Verdi Luca', assignedAt: '2025-02-01T10:00:00Z', returnedAt: '2025-02-10T10:00:00Z', returnedBy: 'admin', returnedByName: null },
      { id: 1, publisherName: 'Bianchi Anna', assignedAt: '2025-01-04T10:00:00Z', returnedAt: '2025-01-10T10:00:00Z', returnedBy: 'publisher', returnedByName: null },
    ];
    const { api, component, feedback, fixture } = setup(HistoryDialog, { territory });
    api.history.mockResolvedValue(items);
    const dialog = component as unknown as HistoryDialogApi;
    await dialog.ngOnInit();
    fixture.detectChanges();
    feedback.open.mockResolvedValueOnce(undefined).mockResolvedValueOnce(true);
    const loads = api.history.mock.calls.length;

    await dialog.history.editDates(items[1]);
    expect(feedback.open).toHaveBeenCalledWith(EditDatesDialog, { territory, entry: items[1], previous: items[2], next: items[0] });
    expect(api.history).toHaveBeenCalledTimes(loads);

    (fixture.nativeElement.querySelectorAll('tbody button') as NodeListOf<HTMLButtonElement>)[0].click();
    await fixture.whenStable();
    expect(feedback.open).toHaveBeenLastCalledWith(EditDatesDialog, { territory, entry: items[0], previous: items[1], next: null });
    expect(feedback.notify).toHaveBeenCalledWith('assignments.datesUpdated');
    expect(api.history).toHaveBeenCalledTimes(loads + 1);
  });

  it('deletes an assignment only after confirmation and reloads', async () => {
    const items = [
      { id: 3, publisherName: 'Rossi Mario', assignedAt: '2025-03-04T10:00:00Z', returnedAt: null, returnedBy: null, returnedByName: null },
    ];
    const { api, component, feedback, fixture } = setup(HistoryDialog, { territory });
    api.history.mockResolvedValue(items);
    const dialog = component as unknown as HistoryDialogApi;
    await dialog.ngOnInit();
    fixture.detectChanges();
    const loads = api.history.mock.calls.length;

    feedback.confirm.mockResolvedValueOnce(false);
    await dialog.history.confirmDelete(items[0]);
    expect(api.delete).not.toHaveBeenCalled();

    await dialog.history.confirmDelete(items[0]);
    expect(api.delete).toHaveBeenCalledWith(3);
    expect(feedback.attempt).toHaveBeenCalledWith(expect.any(Function), 'assignments.deleted');
    expect(api.history).toHaveBeenCalledTimes(loads + 1);
  });

  it('shows only the latest assignments, with the link to the full history', async () => {
    const items = Array.from({ length: 7 }, (_, i) => ({
      id: 7 - i, publisherName: `Proclamatore ${i + 1}`, assignedAt: '2025-03-04T10:00:00Z',
      returnedAt: null, returnedBy: null, returnedByName: null,
    }));
    const { api, component, dialogRef, fixture } = setup(HistoryDialog, { territory });
    api.history.mockResolvedValue(items);
    await (component as unknown as HistoryDialogApi).ngOnInit();
    fixture.detectChanges();

    const rows = [...fixture.nativeElement.querySelectorAll('tbody tr')] as HTMLElement[];
    expect(rows.length).toBe(5);
    expect(rows[0].textContent).toContain('Proclamatore 1');
    expect(rows[4].textContent).toContain('Proclamatore 5');

    const router = TestBed.inject(Router);
    const navigate = vi.spyOn(router, 'navigate').mockResolvedValue(true);
    const seeAll = [...fixture.nativeElement.querySelectorAll('button')] as HTMLButtonElement[];
    seeAll.find(b => b.textContent!.includes('assignments.seeAll'))!.click();

    expect(navigate).toHaveBeenCalledWith(['/admin/assignments', 1, 'history']);
    expect(dialogRef.close).toHaveBeenCalled();
  });
});

describe('EditDatesDialog', () => {
  const entry = { id: 5, publisherName: 'Verdi Luca', assignedAt: '2025-02-01T10:00:00Z', returnedAt: '2025-02-10T10:00:00Z', returnedBy: 'admin' as const, returnedByName: 'Mario Rossi' };
  const previous = { id: 4, publisherName: 'Bianchi Anna', assignedAt: '2025-01-04T10:00:00Z', returnedAt: '2025-01-10T10:00:00Z', returnedBy: 'publisher' as const, returnedByName: null };
  const next = { id: 6, publisherName: 'Rossi Mario', assignedAt: '2025-03-04T10:00:00Z', returnedAt: null, returnedBy: null, returnedByName: null };
  const data: EditDatesDialogData = { territory, entry, previous, next };

  it('saves unchanged dates with their original instants', async () => {
    const { api, component, dialogRef } = setup(EditDatesDialog, data);

    await (component as unknown as DialogApi).save();

    expect(api.updateDates).toHaveBeenCalledWith(5, { assignedAt: entry.assignedAt, returnedAt: entry.returnedAt });
    expect(dialogRef.close).toHaveBeenCalledWith(true);
  });

  it('validates the period against the neighbours', async () => {
    const { api, component } = setup(EditDatesDialog, data);
    const dialog = component as unknown as DialogApi;

    dialog.form.controls['assignedAt'].setValue(new Date(2025, 0, 9));
    dialog.form.controls['returnedAt'].setValue(new Date(2025, 2, 5));
    await dialog.save();
    expect(dialog.form.controls['assignedAt'].hasError('beforeLastReturn')).toBe(true);
    expect(dialog.form.controls['returnedAt'].hasError('afterNextAssignment')).toBe(true);

    dialog.form.controls['returnedAt'].setValue(new Date(2025, 1, 5));
    dialog.form.controls['assignedAt'].setValue(new Date(2025, 1, 7));
    expect(dialog.form.controls['returnedAt'].hasError('beforeAssigned')).toBe(true);
    expect(api.updateDates).not.toHaveBeenCalled();
  });

  it('sends changed days and shows server errors', async () => {
    const { api, component } = setup(EditDatesDialog, data);
    const dialog = component as unknown as DialogApi;
    api.updateDates.mockRejectedValue(new HttpErrorResponse({
      status: 400, error: { code: 'validation', errors: { returnedAt: ['afterNextAssignment'] } },
    }));
    dialog.form.controls['assignedAt'].setValue(new Date(2025, 0, 20));

    await dialog.save();

    expect(api.updateDates).toHaveBeenCalledWith(5, {
      assignedAt: new Date(2025, 0, 20, 23, 59, 59).toISOString(), returnedAt: entry.returnedAt,
    });
    expect(dialog.serverErrors('returnedAt')).toEqual(['afterNextAssignment']);
  });

  it('edits only the assignment date of an open assignment', async () => {
    const { api, component, fixture } = setup(EditDatesDialog, { territory, entry: next, previous: entry, next: null });

    expect(fixture.nativeElement.querySelectorAll('input').length).toBe(1);
    await (component as unknown as DialogApi).save();

    expect(api.updateDates).toHaveBeenCalledWith(6, { assignedAt: next.assignedAt, returnedAt: null });
  });
});