import { TestBed } from '@angular/core/testing';
import { HttpErrorResponse } from '@angular/common/http';
import { MAT_DIALOG_DATA, MatDialogRef } from '@angular/material/dialog';
import { provideTranslateService } from '@ngx-translate/core';
import { AdminDialog, AdminDialogData } from './admin-dialog';
import { AdminDto, AdminsApi } from './admins.api';

const existing: AdminDto = { id: 7, username: 'mario', displayName: 'Mario Rossi', createdAt: '2025-01-01T00:00:00Z', isLocked: false, role: 1 };

function setup(data: AdminDialogData) {
  const api = { create: vi.fn(), update: vi.fn() };
  const dialogRef = { close: vi.fn() };
  TestBed.configureTestingModule({
    imports: [AdminDialog],
    providers: [
      provideTranslateService(),
      { provide: AdminsApi, useValue: api },
      { provide: MatDialogRef, useValue: dialogRef },
      { provide: MAT_DIALOG_DATA, useValue: data },
    ],
  });
  const fixture = TestBed.createComponent(AdminDialog);
  fixture.detectChanges();
  const component = fixture.componentInstance as unknown as {
    form: { controls: Record<string, { setValue(v: string): void }> };
    save(): Promise<void>;
    serverErrors(c: string): string[];
    errorKey(): string | null;
  };
  return { api, dialogRef, component };
}

describe('AdminDialog', () => {
  it('does not submit when the new admin password breaks the policy', async () => {
    const { api, component } = setup({});
    component.form.controls['displayName'].setValue('Anna Verdi');
    component.form.controls['username'].setValue('anna');
    component.form.controls['password'].setValue('weak');

    await component.save();

    expect(api.create).not.toHaveBeenCalled();
  });

  it('creates an admin and closes with the result', async () => {
    const { api, dialogRef, component } = setup({});
    api.create.mockResolvedValue(existing);
    component.form.controls['displayName'].setValue('  Anna Verdi ');
    component.form.controls['username'].setValue('anna');
    component.form.controls['password'].setValue('Correct-Horse-1');
    component.form.controls['confirmPassword'].setValue('Correct-Horse-1');

    await component.save();

    expect(api.create).toHaveBeenCalledWith({ username: 'anna', displayName: 'Anna Verdi', password: 'Correct-Horse-1', role: 1 });
    expect(dialogRef.close).toHaveBeenCalledWith(existing);
  });

  it('sends the selected role', async () => {
    const { api, component } = setup({});
    api.create.mockResolvedValue(existing);
    component.form.controls['displayName'].setValue('Anna Verdi');
    component.form.controls['username'].setValue('anna');
    component.form.controls['password'].setValue('Correct-Horse-1');
    component.form.controls['confirmPassword'].setValue('Correct-Horse-1');
    (component.form.controls['role'] as unknown as { setValue(v: number): void }).setValue(3);

    await component.save();

    expect(api.create).toHaveBeenCalledWith(expect.objectContaining({ role: 3 }));
  });

  it('does not submit when the confirmation differs from the password', async () => {
    const { api, component } = setup({});
    component.form.controls['displayName'].setValue('Anna Verdi');
    component.form.controls['username'].setValue('anna');
    component.form.controls['password'].setValue('Correct-Horse-1');
    component.form.controls['confirmPassword'].setValue('Correct-Horse-2');

    await component.save();

    expect(api.create).not.toHaveBeenCalled();
  });

  it('updates without password when left empty', async () => {
    const { api, component } = setup({ admin: existing });
    api.update.mockResolvedValue(existing);

    await component.save();

    expect(api.update).toHaveBeenCalledWith(7, { username: 'mario', displayName: 'Mario Rossi', password: null, role: 1 });
  });

  it('sends the new password on edit when it is confirmed', async () => {
    const { api, component } = setup({ admin: existing });
    api.update.mockResolvedValue(existing);
    component.form.controls['password'].setValue('New-Password-22');
    component.form.controls['confirmPassword'].setValue('New-Password-22');

    await component.save();

    expect(api.update).toHaveBeenCalledWith(7, {
      username: 'mario', displayName: 'Mario Rossi', password: 'New-Password-22', role: 1,
    });
  });

  it.each(['', '   ', 'x'.repeat(101)])('does not submit an invalid display name (%#)', async value => {
    const { api, component } = setup({ admin: existing });
    component.form.controls['displayName'].setValue(value);

    await component.save();

    expect(api.update).not.toHaveBeenCalled();
  });

  it('shows server errors on the display name', async () => {
    const { api, component } = setup({ admin: existing });
    api.update.mockRejectedValue(
      new HttpErrorResponse({ status: 400, error: { code: 'validation', errors: { displayName: ['invalidFormat'] } } }),
    );

    await component.save();

    expect(component.serverErrors('displayName')).toEqual(['invalidFormat']);
  });

  it('shows a username conflict on the field', async () => {
    const { api, dialogRef, component } = setup({ admin: existing });
    api.update.mockRejectedValue(new HttpErrorResponse({ status: 409, error: { code: 'admins.usernameTaken' } }));

    await component.save();

    expect(component.serverErrors('username')).toEqual(['usernameTaken']);
    expect(dialogRef.close).not.toHaveBeenCalled();
  });

  it('maps server field errors and generic errors', async () => {
    const { api, component } = setup({ admin: existing });
    api.update.mockRejectedValueOnce(
      new HttpErrorResponse({ status: 400, error: { code: 'validation', errors: { password: ['tooShort'], other: ['x'] } } }),
    );
    await component.save();
    expect(component.serverErrors('password')).toEqual(['tooShort']);

    component.form.controls['password'].setValue('');
    api.update.mockRejectedValueOnce(new HttpErrorResponse({ status: 404, error: { code: 'admins.notFound' } }));
    await component.save();
    expect(component.errorKey()).toBe('errors.codes.admins.notFound');
  });
});
