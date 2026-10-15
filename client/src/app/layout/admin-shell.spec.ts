import { TestBed } from '@angular/core/testing';
import { provideRouter, Router } from '@angular/router';
import { provideTranslateService } from '@ngx-translate/core';
import { AdminShell } from './admin-shell';
import { AdminProfile, AuthService } from '../core/auth.service';
import { PreferencesService } from '../core/preferences.service';

const profile: AdminProfile = { id: 1, username: 'admin', displayName: 'Mario Rossi', theme: 2, language: 'it', role: 1 };

describe('AdminShell', () => {
  async function setup(user: AdminProfile | null) {
    const auth = {
      user: () => user,
      hasRole: (roles: readonly number[]) => user !== null && roles.includes(user.role),
      logout: vi.fn().mockResolvedValue(undefined),
    };
    const preferences = { applyProfile: vi.fn(), setTheme: vi.fn(), setLanguage: vi.fn() };
    TestBed.configureTestingModule({
      imports: [AdminShell],
      providers: [
        provideRouter([]),
        provideTranslateService(),
        { provide: AuthService, useValue: auth },
        { provide: PreferencesService, useValue: preferences },
      ],
    });
    const fixture = TestBed.createComponent(AdminShell);
    fixture.detectChanges();
    await fixture.whenStable();
    return { fixture, auth, preferences };
  }

  it('applies the preferences saved on the profile', async () => {
    const { preferences, fixture } = await setup(profile);

    expect(preferences.applyProfile).toHaveBeenCalledWith(profile);
    expect((fixture.nativeElement as HTMLElement).textContent).toContain('Mario Rossi');
  });

  it('shows only the sections allowed by the role', async () => {
    const links = async (role: 1 | 2 | 3) => {
      TestBed.resetTestingModule();
      const { fixture } = await setup({ ...profile, role });
      return Array.from((fixture.nativeElement as HTMLElement).querySelectorAll('a[href]'))
        .map(a => a.getAttribute('href'));
    };

    expect(await links(1)).toEqual(expect.arrayContaining(['/admins', '/backup']));
    const manager = await links(2);
    expect(manager).toEqual(expect.arrayContaining(['/publishers', '/export', '/backup']));
    expect(manager).not.toContain('/admins');
    expect(manager).not.toContain('/territories');
    const assigner = await links(3);
    expect(assigner).toContain('/assignments');
    expect(assigner).not.toContain('/publishers');
    expect(assigner).not.toContain('/backup');
  });

  it('skips the profile preferences without a session and logs out to the login page', async () => {
    const { preferences, auth, fixture } = await setup(null);
    const navigate = vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);

    await (fixture.componentInstance as unknown as { logout(): Promise<void> }).logout();

    expect(preferences.applyProfile).not.toHaveBeenCalled();
    expect(auth.logout).toHaveBeenCalledOnce();
    expect(navigate).toHaveBeenCalledWith(['/login']);
  });
});
