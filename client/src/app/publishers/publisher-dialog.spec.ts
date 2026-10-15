import { TestBed } from '@angular/core/testing';
import { HttpErrorResponse } from '@angular/common/http';
import { MAT_DIALOG_DATA, MatDialogRef } from '@angular/material/dialog';
import { provideTranslateService } from '@ngx-translate/core';
import { PublisherDto, PublishersApi } from './publishers.api';
import { PublisherDialog, PublisherDialogData } from './publisher-dialog';
import { Feedback } from '../shared/feedback';

const anna: PublisherDto = { id: 9, firstName: 'Anna', lastName: 'Rossi' };

function setup(data: PublisherDialogData) {
  const api = { create: vi.fn(), update: vi.fn() };
  const dialogRef = { close: vi.fn() };
  const feedback = { confirm: vi.fn().mockResolvedValue(true) };
  TestBed.configureTestingModule({
    imports: [PublisherDialog],
    providers: [
      provideTranslateService(),
      { provide: PublishersApi, useValue: api },
      { provide: Feedback, useValue: feedback },
      { provide: MatDialogRef, useValue: dialogRef },
      { provide: MAT_DIALOG_DATA, useValue: data },
    ],
  });
  const fixture = TestBed.createComponent(PublisherDialog);
  fixture.detectChanges();
  const dialog = fixture.componentInstance as unknown as {
    form: { controls: Record<string, { setValue(v: string): void }> };
    save(): Promise<void>;
    errorKey(): string | null;
  };
  return { api, dialogRef, feedback, dialog };
}

describe('PublisherDialog', () => {
  it('requires both names', async () => {
    const { api, dialog } = setup({});
    dialog.form.controls['lastName'].setValue('Rossi');

    await dialog.save();

    expect(api.create).not.toHaveBeenCalled();
  });

  it('confirms and reports a reactivation', async () => {
    const { api, dialog, dialogRef, feedback } = setup({});
    api.create.mockResolvedValue({ publisher: anna, reactivated: true });
    dialog.form.controls['firstName'].setValue(' Anna ');
    dialog.form.controls['lastName'].setValue('Rossi ');

    await dialog.save();

    expect(feedback.confirm).toHaveBeenCalledWith(expect.objectContaining({ params: { firstName: 'Anna', lastName: 'Rossi' } }));
    expect(api.create).toHaveBeenCalledWith({ firstName: 'Anna', lastName: 'Rossi' });
    expect(dialogRef.close).toHaveBeenCalledWith({ reactivated: true });
  });

  it('updates an existing publisher', async () => {
    const { api, dialog, dialogRef } = setup({ publisher: anna });
    api.update.mockResolvedValue(anna);

    await dialog.save();

    expect(api.update).toHaveBeenCalledWith(9, { firstName: 'Anna', lastName: 'Rossi' });
    expect(dialogRef.close).toHaveBeenCalledWith({ reactivated: false });
  });

  it('shows duplicates as a dialog error', async () => {
    const { api, dialog, dialogRef } = setup({ publisher: anna });
    api.update.mockRejectedValue(new HttpErrorResponse({ status: 409, error: { code: 'publishers.duplicateDeleted' } }));

    await dialog.save();

    expect(dialog.errorKey()).toBe('errors.codes.publishers.duplicateDeleted');
    expect(dialogRef.close).not.toHaveBeenCalled();
  });
});
