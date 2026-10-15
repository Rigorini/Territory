import { HttpErrorResponse, HttpInterceptorFn, HttpRequest } from '@angular/common/http';
import { inject } from '@angular/core';
import { Router } from '@angular/router';
import { catchError, throwError } from 'rxjs';
import { AuthService } from './auth.service';

// 401 from these calls is an expected outcome handled by the caller, not an expired session.
const SESSION_PROBE_URLS = ['/api/auth/login', '/api/auth/me', '/api/auth/logout'];

export const authInterceptor: HttpInterceptorFn = (req, next) => {
  const auth = inject(AuthService);
  const router = inject(Router);
  return next(req).pipe(
    catchError((error: unknown) => {
      if (isSessionExpired(req, error)) {
        auth.clear();
        void router.navigate(['/login'], { queryParams: { returnUrl: router.url } });
      }
      return throwError(() => error);
    }),
  );
};

export function isSessionExpired(req: HttpRequest<unknown>, error: unknown): boolean {
  return (
    error instanceof HttpErrorResponse &&
    error.status === 401 &&
    req.url.startsWith('/api/') &&
    !SESSION_PROBE_URLS.includes(req.url)
  );
}
