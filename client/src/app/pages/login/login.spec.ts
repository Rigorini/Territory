import { TestBed } from '@angular/core/testing';
import { HttpErrorResponse } from '@angular/common/http';
import { Router } from '@angular/router';
import { provideTranslateService } from '@ngx-translate/core';
import { LoginPage } from './login';
import { AuthService } from '../../core/auth.service';

interface LoginApi {
  form: { setValue(v: { username: string; password: string }): void; controls: { password: { value: string } } };
  submit(): Promise<void>;
  errorKey(): string | null;
}

function setup(returnUrl?: string) {
  const auth = { login: vi.fn() };
  const router = { navigateByUrl: vi.fn().mockResolvedValue(true) };
  TestBed.configureTestingModule({
    imports: [LoginPage],
    providers: [
      provideTranslateService(),
      { provide: AuthService, useValue: auth },
      { provide: Router, useValue: router },
    ],
  });
  const fixture = TestBed.createComponent(LoginPage);
  if (returnUrl !== undefined) {
    fixture.componentRef.setInput('returnUrl', returnUrl);
  }
  fixture.detectChanges();
  return { auth, router, page: fixture.componentInstance as unknown as LoginApi };
}

describe('LoginPage', () => {
  it('does not call the server when the form is empty', async () => {
    const { auth, page } = setup();

    await page.submit();

    expect(auth.login).not.toHaveBeenCalled();
  });

  it('navigates to a safe return url after login', async () => {
    const { auth, router, page } = setup('//evil.example');
    auth.login.mockResolvedValue({});
    page.form.setValue({ username: 'admin', password: 'secret' });

    await page.submit();

    expect(auth.login).toHaveBeenCalledWith('admin', 'secret');
    expect(router.navigateByUrl).toHaveBeenCalledWith('/admin');
  });

  it('shows the translated error and clears the password on failure', async () => {
    const { auth, page } = setup('/admin/admins');
    auth.login.mockRejectedValue(new HttpErrorResponse({ status: 401, error: { code: 'auth.invalidCredentials' } }));
    page.form.setValue({ username: 'admin', password: 'wrong' });

    await page.submit();

    expect(page.errorKey()).toBe('errors.codes.auth.invalidCredentials');
    expect(page.form.controls.password.value).toBe('');
  });
});
