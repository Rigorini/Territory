import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { AuthService } from './auth.service';
import { AdminRoleValue } from './roles';

/** UX-only guard: the API enforces the same rules. Redirects to the assignments page when not allowed. */
export function roleGuard(allowed: readonly AdminRoleValue[]): CanActivateFn {
  return () => {
    const auth = inject(AuthService);
    return auth.hasRole(allowed) || inject(Router).createUrlTree(['/admin/assignments']);
  };
}
