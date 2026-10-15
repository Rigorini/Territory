import { TestBed } from '@angular/core/testing';
import { ActivatedRouteSnapshot, provideRouter, Router, RouterStateSnapshot, UrlTree } from '@angular/router';
import { AuthService } from './auth.service';
import { authGuard, DEFAULT_AUTHENTICATED_URL, passwordChangeGuard, safeReturnUrl } from './auth.guard';

describe('password change guards', () => {
  function run(guard: typeof authGuard, loaded: boolean, mustChangePassword: boolean | undefined) {
    TestBed.configureTestingModule({
      providers: [
        provideRouter([]),
        { provide: AuthService, useValue: { loadProfile: async () => loaded, user: () => ({ mustChangePassword }) } },
      ],
    });
    return TestBed.runInInjectionContext(() =>
      guard({} as ActivatedRouteSnapshot, { url: '/admin/x' } as RouterStateSnapshot));
  }
  const url = (result: unknown) => TestBed.inject(Router).serializeUrl(result as UrlTree);

  it('sends users who must change the password to the dedicated page', async () => {
    expect(url(await run(authGuard, true, true))).toBe('/change-password');
  });

  it('lets users with a normal session through', async () => {
    expect(await run(authGuard, true, false)).toBe(true);
  });

  it('allows the dedicated page while the change is pending', async () => {
    expect(await run(passwordChangeGuard, true, true)).toBe(true);
  });

  it('leaves the dedicated page once the password is changed', async () => {
    expect(url(await run(passwordChangeGuard, true, false))).toBe('/admin');
  });

  it('requires a session for the dedicated page', async () => {
    expect(url(await run(passwordChangeGuard, false, undefined))).toBe('/login');
  });
});

describe('safeReturnUrl', () => {
  it('keeps internal absolute paths', () => {
    expect(safeReturnUrl('/admin/admins?x=1')).toBe('/admin/admins?x=1');
  });

  it.each([
    [undefined],
    [null],
    [''],
    ['https://evil.example'],
    ['//evil.example'],
    ['/\\evil.example'],
    ['javascript:alert(1)'],
    ['admin'],
    ['/login?returnUrl=/admin'],
  ])('falls back to the default url for %s', value => {
    expect(safeReturnUrl(value)).toBe(DEFAULT_AUTHENTICATED_URL);
  });
});

