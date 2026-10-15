import { TestBed } from '@angular/core/testing';
import { HttpErrorResponse } from '@angular/common/http';
import { MatDialogRef } from '@angular/material/dialog';
import { provideTranslateService } from '@ngx-translate/core';
import { AuthService } from '../core/auth.service';
import { ChangePasswordDialog } from './change-password-dialog';

function setup() {
  const auth = { changePassword: vi.fn() };
  const dialogRef = { close: vi.fn() };
  TestBed.configureTestingModule({
    imports: [ChangePasswordDialog],
    providers: [
      provideTranslateService(),
      { provide: AuthService, useValue: auth },
      { provide: MatDialogRef, useValue: dialogRef },
    ],
  });
  const fixture = TestBed.createComponent(ChangePasswordDialog);
  fixture.detectChanges();
  const component = fixture.componentInstance as unknown as {
    form: { controls: Record<string, { setValue(v: string): void }> };
    save(): Promise<void>;
    serverErrors(c: string): string[];
  };
  const fill = (current: string, next: string, confirm: string) => {
    component.form.controls['currentPassword'].setValue(current);
    component.form.controls['password'].setValue(next);
    component.form.controls['confirmPassword'].setValue(confirm);
  };
  return { auth, dialogRef, component, fill };
}

describe('ChangePasswordDialog', () => {
  it('sends current and new password and closes with true', async () => {
    const { auth, dialogRef, component, fill } = setup();
    auth.changePassword.mockResolvedValue(undefined);
    fill('Old-Password-1', 'New-Password-22', 'New-Password-22');

    await component.save();

    expect(auth.changePassword).toHaveBeenCalledWith('Old-Password-1', 'New-Password-22');
    expect(dialogRef.close).toHaveBeenCalledWith(true);
  });

  it('does not submit when the confirmation differs', async () => {
    const { auth, component, fill } = setup();
    fill('Old-Password-1', 'New-Password-22', 'New-Password-23');

    await component.save();

    expect(auth.changePassword).not.toHaveBeenCalled();
  });

  it('does not submit a weak new password or a missing current password', async () => {
    const { auth, component, fill } = setup();
    fill('Old-Password-1', 'weak', 'weak');
    await component.save();
    fill('', 'New-Password-22', 'New-Password-22');
    await component.save();

    expect(auth.changePassword).not.toHaveBeenCalled();
  });

  it('shows a wrong current password on its field', async () => {
    const { auth, dialogRef, component, fill } = setup();
    auth.changePassword.mockRejectedValue(
      new HttpErrorResponse({ status: 400, error: { code: 'validation', errors: { currentPassword: ['wrongPassword'] } } }),
    );
    fill('Wrong-Password-1', 'New-Password-22', 'New-Password-22');

    await component.save();

    expect(component.serverErrors('currentPassword')).toEqual(['wrongPassword']);
    expect(dialogRef.close).not.toHaveBeenCalled();
  });
});