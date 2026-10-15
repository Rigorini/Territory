import { TestBed } from '@angular/core/testing';
import { provideRouter, Router, UrlTree } from '@angular/router';
import { AuthService } from './auth.service';
import { roleGuard } from './role.guard';
import { ADMINISTRATOR_ONLY, MANAGER_ROLES } from './roles';

describe('roleGuard', () => {
  function run(role: number | null, allowed: readonly (1 | 2 | 3)[]) {
    TestBed.configureTestingModule({
      providers: [
        provideRouter([]),
        { provide: AuthService, useValue: { hasRole: (r: readonly number[]) => role !== null && r.includes(role) } },
      ],
    });
    return TestBed.runInInjectionContext(() => roleGuard(allowed)({} as never, {} as never));
  }

  it('allows a listed role', () => {
    expect(run(2, MANAGER_ROLES)).toBe(true);
  });

  it('redirects to the assignments page otherwise', () => {
    const result = run(3, ADMINISTRATOR_ONLY) as UrlTree;

    expect(TestBed.inject(Router).serializeUrl(result)).toBe('/admin/assignments');
  });
});
