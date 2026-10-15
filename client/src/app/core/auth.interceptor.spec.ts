import { HttpErrorResponse, HttpRequest } from '@angular/common/http';
import { isSessionExpired } from './auth.interceptor';

const unauthorized = new HttpErrorResponse({ status: 401 });

describe('isSessionExpired', () => {
  it('is true for 401 on protected API calls', () => {
    expect(isSessionExpired(new HttpRequest('GET', '/api/admins'), unauthorized)).toBe(true);
  });

  it.each(['/api/auth/login', '/api/auth/me', '/api/auth/logout'])('ignores session probe %s', url => {
    expect(isSessionExpired(new HttpRequest('GET', url), unauthorized)).toBe(false);
  });

  it('ignores other statuses, non-API urls and non-http errors', () => {
    expect(isSessionExpired(new HttpRequest('GET', '/api/admins'), new HttpErrorResponse({ status: 403 }))).toBe(false);
    expect(isSessionExpired(new HttpRequest('GET', '/i18n/it.json'), unauthorized)).toBe(false);
    expect(isSessionExpired(new HttpRequest('GET', '/api/admins'), new Error('x'))).toBe(false);
  });
});
