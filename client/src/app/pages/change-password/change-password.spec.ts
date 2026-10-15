import { TestBed } from '@angular/core/testing';
import { Router } from '@angular/router';
import { provideTranslateService } from '@ngx-translate/core';
import { HttpErrorResponse } from '@angular/common/http';
import { AdminProfile, AuthService } from '../../core/auth.service';
import { ChangePasswordPage } from './change-password';

const profile: AdminProfile = { id: 1, username: 'u', displayName: 'U', theme: 0, language: null, role: 3, mustChangePassword: true };

function setup() {
  const auth = {
    changePassword: vi.fn().mockResolvedValue(undefined),
    updateProfile: vi.fn(),
    logout: vi.fn().mockResolvedValue(undefined),
    user: () => profile,
  };
  TestBed.configureTestingModule({
    imports: [ChangePasswordPage],
    providers: [provideTranslateService(), { provide: AuthService, useValue: auth }],
  });
  const navigate = vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);
  const navigateByUrl = vi.spyOn(TestBed.inject(Router), 'navigateByUrl').mockResolvedValue(true);
  const fixture = TestBed.createComponent(ChangePasswordPage);
  fixture.detectChanges();
  const page = fixture.componentInstance as unknown as {
    form: { controls: Record<string, { setValue(v: string): void }> };
    submit(): Promise<void>;
    logout(): Promise<void>;
    errorKey(): string | null;
    serverFields(): Record<string, string[]>;
  };
  const fill = (current: string, next: string, confirm: string) => {
    page.form.controls['currentPassword'].setValue(current);
    page.form.controls['password'].setValue(next);
    page.form.controls['confirmPassword'].setValue(confirm);
  };
  return { auth, page, fill, navigate, navigateByUrl };
}

describe('ChangePasswordPage', () => {
  it('changes the password, clears the flag and enters the app', async () => {
    const { auth, page, fill, navigateByUrl } = setup();
    fill('Temp-Pass-1', 'New-Password-22', 'New-Password-22');

    await page.submit();

    expect(auth.changePassword).toHaveBeenCalledWith('Temp-Pass-1', 'New-Password-22');
    expect(auth.updateProfile).toHaveBeenCalledWith({ ...profile, mustChangePassword: false });
    expect(navigateByUrl).toHaveBeenCalledWith('/admin');
  });

  it('does not send when the confirmation differs', async () => {
    const { auth, page, fill } = setup();
    fill('Temp-Pass-1', 'New-Password-22', 'Other-Password-22');

    await page.submit();

    expect(auth.changePassword).not.toHaveBeenCalled();
  });

  it('shows server field errors', async () => {
    const { auth, page, fill } = setup();
    auth.changePassword.mockRejectedValue(new HttpErrorResponse({
      status: 400,
      error: { code: 'validation', errors: { currentPassword: ['wrongPassword'] } },
    }));
    fill('Wrong-Pass-1', 'New-Password-22', 'New-Password-22');

    await page.submit();

    expect(page.serverFields()['currentPassword']).toEqual(['wrongPassword']);
  });

  it('logs out to the login page', async () => {
    const { auth, page, navigate } = setup();

    await page.logout();

    expect(auth.logout).toHaveBeenCalled();
    expect(navigate).toHaveBeenCalledWith(['/login']);
  });
});
