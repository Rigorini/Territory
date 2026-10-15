import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { Router, UrlTree, provideRouter } from '@angular/router';
import { provideTranslateService } from '@ngx-translate/core';
import { AuthService } from '../core/auth.service';
import { SetupApi } from './setup.api';
import { loginGuard, setupGuard } from './setup.guard';
import { SetupPage } from './setup-page';

const STRONG = 'Correct-Horse-1';

describe('setup guards', () => {
  const api = { isRequired: vi.fn() };

  beforeEach(() => {
    vi.resetAllMocks();
    TestBed.configureTestingModule({ providers: [provideRouter([]), { provide: SetupApi, useValue: api }] });
  });

  const run = (guard: typeof loginGuard) =>
    TestBed.runInInjectionContext(() => guard({} as never, {} as never)) as Promise<boolean | UrlTree>;

  it('sends the login to the setup while no admin exists', async () => {
    api.isRequired.mockResolvedValue(true);
    const router = TestBed.inject(Router);
    expect(router.serializeUrl((await run(loginGuard)) as UrlTree)).toBe('/setup');
    expect(await run(setupGuard)).toBe(true);
  });

  it('keeps the setup closed once an admin exists', async () => {
    api.isRequired.mockResolvedValue(false);
    expect(await run(loginGuard)).toBe(true);
    const router = TestBed.inject(Router);
    expect(router.serializeUrl((await run(setupGuard)) as UrlTree)).toBe('/login');
  });

  it('does not block the login when the status cannot be read', async () => {
    api.isRequired.mockRejectedValue(new Error('offline'));
    expect(await run(loginGuard)).toBe(true);
  });
});

describe('SetupApi', () => {
  it('refreshes the antiforgery token before creating the admin', async () => {
    const auth = { refreshXsrfToken: vi.fn().mockResolvedValue(undefined) };
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting(), { provide: AuthService, useValue: auth }],
    });
    const http = TestBed.inject(HttpTestingController);
    const request = { username: 'mario', displayName: 'Mario Rossi', password: STRONG };

    const done = TestBed.inject(SetupApi).createFirstAdmin(request);
    await vi.waitFor(() => http.expectOne('/api/setup'));
    expect(auth.refreshXsrfToken).toHaveBeenCalledOnce();
    done.catch(() => undefined);
  });
});

describe('SetupPage', () => {
  const api = { createFirstAdmin: vi.fn() };
  const auth = { login: vi.fn() };
  let page: SetupPage;
  let router: Router;

  const form = () => (page as unknown as { form: SetupPage['form' & keyof SetupPage] }).form as never as import('@angular/forms').FormGroup;
  const submit = () => (page as unknown as { submit(): Promise<void> }).submit();
  const fill = (password = STRONG, confirm = STRONG) =>
    form().setValue({ displayName: 'Mario Rossi', username: 'mario', password, confirmPassword: confirm });

  beforeEach(() => {
    vi.resetAllMocks();
    TestBed.configureTestingModule({
      providers: [
        provideRouter([]), provideTranslateService(),
        { provide: SetupApi, useValue: api }, { provide: AuthService, useValue: auth },
      ],
    });
    router = TestBed.inject(Router);
    vi.spyOn(router, 'navigateByUrl').mockResolvedValue(true);
    page = TestBed.createComponent(SetupPage).componentInstance;
  });

  it('does not send the form when the passwords differ', async () => {
    fill(STRONG, 'Different-1-x');
    await submit();
    expect(api.createFirstAdmin).not.toHaveBeenCalled();
  });

  it('does not send a password that breaks the rules', async () => {
    fill('weak', 'weak');
    await submit();
    expect(api.createFirstAdmin).not.toHaveBeenCalled();
  });

  it('creates the admin, signs in and opens the app', async () => {
    api.createFirstAdmin.mockResolvedValue(undefined);
    auth.login.mockResolvedValue({});
    fill();

    await submit();

    expect(api.createFirstAdmin).toHaveBeenCalledWith({ displayName: 'Mario Rossi', username: 'mario', password: STRONG });
    expect(auth.login).toHaveBeenCalledWith('mario', STRONG);
    expect(router.navigateByUrl).toHaveBeenCalledWith('/admin');
  });

  it('goes to the login when another admin has been created meanwhile', async () => {
    api.createFirstAdmin.mockRejectedValue(
      new (await import('@angular/common/http')).HttpErrorResponse({ status: 409, error: { code: 'setup.alreadyDone' } }),
    );
    fill();

    await submit();

    expect(router.navigateByUrl).toHaveBeenCalledWith('/login');
    expect(auth.login).not.toHaveBeenCalled();
  });

  it('shows the validation code returned by the server', async () => {
    api.createFirstAdmin.mockRejectedValue(
      new (await import('@angular/common/http')).HttpErrorResponse({ status: 400, error: { code: 'validation', errors: { username: ['invalidFormat'] } } }),
    );
    fill();

    await submit();

    expect((page as unknown as { errorKey(): string }).errorKey()).toBe('errors.fields.invalidFormat');
  });
});
