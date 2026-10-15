import { TestBed } from '@angular/core/testing';
import { HttpErrorResponse } from '@angular/common/http';
import { MatDialog } from '@angular/material/dialog';
import { MatSnackBar } from '@angular/material/snack-bar';
import { provideTranslateService } from '@ngx-translate/core';
import { of } from 'rxjs';
import { Feedback } from './feedback';
import { ListResource } from './list-resource';
import { ConfirmDialog } from './confirm-dialog';

function setup(dialogResult?: unknown) {
  const snackBar = { open: vi.fn() };
  const dialog = { open: vi.fn().mockReturnValue({ afterClosed: () => of(dialogResult) }) };
  TestBed.configureTestingModule({
    providers: [
      provideTranslateService(),
      { provide: MatSnackBar, useValue: snackBar },
      { provide: MatDialog, useValue: dialog },
    ],
  });
  return { feedback: TestBed.inject(Feedback), snackBar, dialog };
}

const conflict = new HttpErrorResponse({ status: 409, error: { code: 'x.taken' } });

describe('Feedback', () => {
  it('confirms only on an explicit true result', async () => {
    const accepted = setup(true);
    const data = { titleKey: 't', messageKey: 'm' };
    expect(await accepted.feedback.confirm(data)).toBe(true);
    expect(accepted.dialog.open).toHaveBeenCalledWith(ConfirmDialog, { data });

    TestBed.resetTestingModule();
    expect(await setup(undefined).feedback.confirm(data)).toBe(false);
  });

  it('notifies the outcome of an action', async () => {
    const { feedback, snackBar } = setup();

    expect(await feedback.attempt(() => Promise.resolve(), 'done')).toBe(true);
    expect(snackBar.open).toHaveBeenLastCalledWith('done', undefined, { duration: 4000 });

    expect(await feedback.attempt(() => Promise.reject(conflict), 'done')).toBe(false);
    expect(snackBar.open).toHaveBeenLastCalledWith('errors.codes.x.taken', undefined, { duration: 4000 });
  });
});

describe('ListResource', () => {
  it('loads items and toggles the loading flag', async () => {
    const { feedback } = setup();
    const list = new ListResource(() => Promise.resolve([1, 2]), feedback);

    const pending = list.reload();
    expect(list.loading()).toBe(true);
    await pending;

    expect(list.items()).toEqual([1, 2]);
    expect(list.loading()).toBe(false);
  });

  it('keeps previous items and notifies on failure', async () => {
    const { feedback, snackBar } = setup();
    const fetch = vi.fn().mockResolvedValueOnce([1]).mockRejectedValueOnce(conflict);
    const list = new ListResource<number>(fetch, feedback);

    await list.reload();
    await list.reload();

    expect(list.items()).toEqual([1]);
    expect(list.loading()).toBe(false);
    expect(snackBar.open).toHaveBeenCalledWith('errors.codes.x.taken', undefined, expect.anything());
  });
});
