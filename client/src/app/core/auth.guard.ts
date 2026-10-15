import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { AuthService } from './auth.service';

export const DEFAULT_AUTHENTICATED_URL = '/admin';
export const CHANGE_PASSWORD_URL = '/change-password';

/** Requires a session; users who still have to choose their own password are sent to the dedicated page. */
export const authGuard: CanActivateFn = async (_route, state) => {
  const router = inject(Router);
  const auth = inject(AuthService);
  if (!(await auth.loadProfile())) {
    return router.createUrlTree(['/login'], { queryParams: { returnUrl: state.url } });
  }
  return auth.user()?.mustChangePassword ? router.createUrlTree([CHANGE_PASSWORD_URL]) : true;
};

/** The forced password change page is reachable only with a session that has to change the password. */
export const passwordChangeGuard: CanActivateFn = async () => {
  const router = inject(Router);
  const auth = inject(AuthService);
  if (!(await auth.loadProfile())) {
    return router.createUrlTree(['/login']);
  }
  return auth.user()?.mustChangePassword ? true : router.createUrlTree([DEFAULT_AUTHENTICATED_URL]);
};

/** Accepts only in-app absolute paths, preventing open redirects through the returnUrl parameter. */
export function safeReturnUrl(value: string | null | undefined): string {
  const isInternalPath =
    !!value && value.startsWith('/') && !value.startsWith('//') && !value.startsWith('/\\');
  if (!isInternalPath || value.startsWith('/login')) {
    return DEFAULT_AUTHENTICATED_URL;
  }
  return value;
}
