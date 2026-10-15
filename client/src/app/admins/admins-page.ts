import { Component, inject, OnInit, signal } from '@angular/core';
import { MatTableModule } from '@angular/material/table';
import { MatSortModule } from '@angular/material/sort';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatTooltipModule } from '@angular/material/tooltip';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { TranslatePipe } from '@ngx-translate/core';
import { AdminDto, AdminsApi } from './admins.api';
import { AdminDialog, AdminDialogData } from './admin-dialog';
import { AuthService } from '../core/auth.service';
import { ADMIN_ROLES } from '../core/roles';
import { ColumnFilter, FilterOption } from '../shared/column-filter';
import { Feedback } from '../shared/feedback';
import { formatDate } from '../shared/format';
import { ListResource } from '../shared/list-resource';
import { TableState } from '../shared/table-state';

type AdminStatus = 'active' | 'locked';

@Component({
  selector: 'app-admins-page',
  imports: [
    MatTableModule, MatSortModule, MatButtonModule, MatIconModule, MatTooltipModule,
    MatProgressBarModule, TranslatePipe, ColumnFilter,
  ],
  templateUrl: './admins-page.html',
})
export class AdminsPage implements OnInit {
  private readonly api = inject(AdminsApi);
  private readonly feedback = inject(Feedback);
  protected readonly auth = inject(AuthService);

  protected readonly columns = ['displayName', 'username', 'role', 'createdAt', 'status', 'actions'];
  protected readonly filterColumns = this.columns.map(c => `f-${c}`);
  protected readonly statusOptions: readonly FilterOption[] = (['active', 'locked'] as const)
    .map(s => ({ value: s, labelKey: `admins.statuses.${s}` }));

  protected readonly roleOptions: readonly FilterOption[] = ADMIN_ROLES
    .map(r => ({ value: String(r), labelKey: `admins.roles.${r}` }));

  protected readonly filtersOpen = signal(false);
  protected readonly admins = new ListResource(() => this.api.list(), this.feedback);

  protected readonly table = new TableState(this.admins.items, {
    filters: signal({
      displayName: (a: AdminDto) => a.displayName,
      username: (a: AdminDto) => a.username,
      role: (a: AdminDto) => String(a.role),
      createdAt: (a: AdminDto) => formatDate(a.createdAt),
      status: (a: AdminDto) => statusOf(a),
    }),
    exactFilters: ['status', 'role'],
    sort: {
      displayName: a => a.displayName, username: a => a.username, role: a => a.role,
      createdAt: a => a.createdAt, status: a => statusOf(a),
    },
    initialSort: { active: 'displayName', direction: 'asc' },
  });

  ngOnInit(): void {
    void this.admins.reload();
  }

  protected formatDate(value: string): string {
    return formatDate(value);
  }

  protected status(admin: AdminDto): AdminStatus {
    return statusOf(admin);
  }

  protected isSelf(admin: AdminDto): boolean {
    return admin.id === this.auth.user()?.id;
  }

  protected async openDialog(admin?: AdminDto): Promise<void> {
    if (await this.feedback.open<AdminDialog, AdminDialogData, AdminDto>(AdminDialog, { admin })) {
      this.feedback.notify(admin ? 'admins.updated' : 'admins.created');
      await this.admins.reload();
    }
  }

  protected async confirmDelete(admin: AdminDto): Promise<void> {
    const confirmed = await this.feedback.confirm({
      titleKey: 'admins.deleteTitle',
      messageKey: 'admins.deleteMessage',
      params: { username: admin.username },
      confirmKey: 'common.delete',
    });
    if (confirmed) {
      await this.feedback.attempt(() => this.api.delete(admin.id), 'admins.deleted');
      await this.admins.reload();
    }
  }
}

function statusOf(admin: AdminDto): AdminStatus {
  return admin.isLocked ? 'locked' : 'active';
}
