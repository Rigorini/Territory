import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { AdminProfile, AuthService } from './auth.service';

const profile: AdminProfile = { id: 1, username: 'admin', displayName: 'Mario Rossi', theme: 0, language: null, role: 1 };

describe('AuthService', () => {
  let service: AuthService;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    service = TestBed.inject(AuthService);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  async function flushAntiforgeryThenLogin(): Promise<void> {
    await Promise.resolve();
    http.expectOne('/api/auth/antiforgery').flush(null);
    await Promise.resolve();
  }

  it('refreshes the antiforgery token before logging in', async () => {
    const login = service.login('admin', 'pw');
    await flushAntiforgeryThenLogin();
    const req = http.expectOne('/api/auth/login');
    expect(req.request.body).toEqual({ username: 'admin', password: 'pw' });
    req.flush(profile);

    await expect(login).resolves.toEqual(profile);
    expect(service.isAuthenticated()).toBe(true);
  });

  it('changes the own password through the dedicated endpoint', async () => {
    const change = service.changePassword('old', 'new');
    const req = http.expectOne('/api/auth/me/password');
    expect(req.request.method).toBe('PUT');
    expect(req.request.body).toEqual({ currentPassword: 'old', password: 'new' });
    req.flush(null, { status: 204, statusText: 'No Content' });

    await expect(change).resolves.toBeUndefined();
  });

  it('loads the profile once and reports missing sessions', async () => {
    const first = service.loadProfile();
    http.expectOne('/api/auth/me').flush(profile);
    await expect(first).resolves.toBe(true);
    await expect(service.loadProfile()).resolves.toBe(true);

    service.clear();
    const second = service.loadProfile();
    http.expectOne('/api/auth/me').flush(null, { status: 401, statusText: 'Unauthorized' });
    await expect(second).resolves.toBe(false);
  });

  it('propagates unexpected errors while loading the profile', async () => {
    const result = service.loadProfile();
    http.expectOne('/api/auth/me').flush(null, { status: 500, statusText: 'Error' });
    await expect(result).rejects.toBeTruthy();
  });

  it('clears the session on logout even when it had already expired', async () => {
    const result = service.logout();
    http.expectOne('/api/auth/logout').flush(null, { status: 401, statusText: 'Unauthorized' });
    await expect(result).resolves.toBeUndefined();
    expect(service.user()).toBeNull();
  });

  it('rethrows unexpected logout failures', async () => {
    const result = service.logout();
    http.expectOne('/api/auth/logout').flush(null, { status: 500, statusText: 'Error' });
    await expect(result).rejects.toBeTruthy();
  });
});
