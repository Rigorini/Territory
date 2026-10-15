import { Component, computed, inject } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { BreakpointObserver } from '@angular/cdk/layout';
import { MatSidenavModule } from '@angular/material/sidenav';
import { MatToolbarModule } from '@angular/material/toolbar';
import { MatListModule } from '@angular/material/list';
import { MatIconModule } from '@angular/material/icon';
import { MatButtonModule } from '@angular/material/button';
import { MatMenuModule } from '@angular/material/menu';
import { MatTooltipModule } from '@angular/material/tooltip';
import { Router, RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { TranslatePipe } from '@ngx-translate/core';
import { map } from 'rxjs';
import { THEMES, ThemeService } from '../core/theme.service';
import { LanguageService, SUPPORTED_LANGUAGES } from '../core/language.service';
import { AuthService } from '../core/auth.service';
import { AppInfoService } from '../core/app-info.service';
import { ADMIN_ROLES, ADMINISTRATOR_ONLY, AdminRoleValue, MANAGER_ROLES } from '../core/roles';
import { Feedback } from '../shared/feedback';
import { ChangePasswordDialog } from './change-password-dialog';
import { PreferencesService } from '../core/preferences.service';

interface NavItem {
  path: string;
  icon: string;
  labelKey: string;
  roles: readonly AdminRoleValue[];
}

const COMPACT_LAYOUT_QUERY = '(max-width: 800px)';

@Component({
  selector: 'app-admin-shell',
  imports: [
    RouterOutlet, RouterLink, RouterLinkActive, MatSidenavModule, MatToolbarModule,
    MatListModule, MatIconModule, MatButtonModule, MatMenuModule, MatTooltipModule, TranslatePipe,
  ],
  templateUrl: './admin-shell.html',
  styleUrl: './admin-shell.scss',
})
export class AdminShell {
  private readonly router = inject(Router);
  private readonly feedback = inject(Feedback);
  private readonly appInfo = inject(AppInfoService);
  protected readonly version = this.appInfo.version;
  protected readonly auth = inject(AuthService);
  protected readonly theme = inject(ThemeService);
  protected readonly language = inject(LanguageService);
  protected readonly languages = SUPPORTED_LANGUAGES;
  protected readonly preferences = inject(PreferencesService);
  protected readonly themes = THEMES;

  private readonly allNavItems: readonly NavItem[] = [
    { path: 'admins', icon: 'admin_panel_settings', labelKey: 'nav.admins', roles: ADMINISTRATOR_ONLY },
    { path: 'territories', icon: 'map', labelKey: 'nav.territories', roles: ADMINISTRATOR_ONLY },
    { path: 'publishers', icon: 'groups', labelKey: 'nav.publishers', roles: MANAGER_ROLES },
    { path: 'assignments', icon: 'assignment', labelKey: 'nav.assignments', roles: ADMIN_ROLES },
    { path: 'export', icon: 'download', labelKey: 'nav.export', roles: MANAGER_ROLES },
    { path: 'backup', icon: 'backup', labelKey: 'nav.backup', roles: MANAGER_ROLES },
  ];
  protected readonly navItems = computed(() => this.allNavItems.filter(i => this.auth.hasRole(i.roles)));

  protected readonly compact = toSignal(
    inject(BreakpointObserver).observe(COMPACT_LAYOUT_QUERY).pipe(map(r => r.matches)),
    { initialValue: false },
  );
  protected readonly sidenavMode = computed(() => (this.compact() ? 'over' : 'side'));

  constructor() {
    const user = this.auth.user();
    if (user) {
      this.preferences.applyProfile(user);
    }
    void this.appInfo.loadVersion();
  }

  protected async changePassword(): Promise<void> {
    if (await this.feedback.open<ChangePasswordDialog, null, boolean>(ChangePasswordDialog, null)) {
      this.feedback.notify('account.passwordChanged');
    }
  }

  protected async logout(): Promise<void> {
    try {
      await this.auth.logout();
    } finally {
      await this.router.navigate(['/login']);
    }
  }
}
