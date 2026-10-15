import { TestBed } from '@angular/core/testing';
import { HttpErrorResponse } from '@angular/common/http';
import { MatDialog } from '@angular/material/dialog';
import { MatSnackBar } from '@angular/material/snack-bar';
import { provideTranslateService } from '@ngx-translate/core';
import { of } from 'rxjs';
import { AdminsPage } from './admins-page';
import { AdminDto, AdminsApi } from './admins.api';
import { AuthService } from '../core/auth.service';
import { TableState } from '../shared/table-state';

const admins: AdminDto[] = [
  { id: 1, username: 'zeta', displayName: 'Bruno Neri', createdAt: '2025-03-01T10:00:00Z', isLocked: false, role: 1 },
  { id: 2, username: 'alfa', displayName: 'Carla Bianchi', createdAt: '2025-01-15T10:00:00Z', isLocked: true, role: 2 },
];

interface PageApi {
  table: TableState<AdminDto>;
  isSelf(admin: AdminDto): boolean;
  openDialog(admin?: AdminDto): Promise<void>;
  confirmDelete(admin: AdminDto): Promise<void>;
}

async function setup(dialogResult: unknown) {
  const api = { list: vi.fn().mockResolvedValue(admins), delete: vi.fn().mockResolvedValue(undefined) };
  const dialog = { open: vi.fn().mockReturnValue({ afterClosed: () => of(dialogResult) }) };
  const snackBar = { open: vi.fn() };
  TestBed.configureTestingModule({
    imports: [AdminsPage],
    providers: [
      provideTranslateService(),
      { provide: AdminsApi, useValue: api },
      { provide: MatDialog, useValue: dialog },
      { provide: MatSnackBar, useValue: snackBar },
      { provide: AuthService, useValue: { user: () => ({ id: 1, username: 'zeta', displayName: 'Bruno Neri' }) } },
    ],
  });
  const fixture = TestBed.createComponent(AdminsPage);
  fixture.detectChanges();
  await fixture.whenStable();
  return { api, dialog, snackBar, page: fixture.componentInstance as unknown as PageApi, fixture };
}

describe('AdminsPage', () => {
  it('lists admins sorted by display name and renders them', async () => {
    const { page, fixture } = await setup(undefined);
    fixture.detectChanges();

    expect(page.table.rows().map(a => a.username)).toEqual(['zeta', 'alfa']);
    const rows = fixture.nativeElement.querySelectorAll('tr.mat-mdc-row') as NodeListOf<HTMLElement>;
    expect(rows.length).toBe(2);
    expect(rows[0].textContent).toContain('Bruno Neri');
    expect(rows[0].textContent).toContain('zeta');
  });

  it('filters by every column and clears filters', async () => {
    const { page } = await setup(undefined);

    page.table.setFilter('status', 'locked');
    expect(page.table.rows().map(a => a.username)).toEqual(['alfa']);
    expect(page.table.hasFilters()).toBe(true);

    page.table.clearFilters();
    page.table.setFilter('username', 'ZE');
    expect(page.table.rows().map(a => a.username)).toEqual(['zeta']);

    page.table.clearFilters();
    page.table.setFilter('displayName', 'carla');
    expect(page.table.rows().map(a => a.username)).toEqual(['alfa']);

    page.table.clearFilters();
    page.table.setFilter('createdAt', '2025');
    expect(page.table.rows()).toHaveLength(2);
  });

  it('sorts on demand', async () => {
    const { page } = await setup(undefined);

    page.table.onSort({ active: 'createdAt', direction: 'desc' });
    expect(page.table.rows().map(a => a.username)).toEqual(['zeta', 'alfa']);

    page.table.onSort({ active: 'username', direction: 'asc' });
    expect(page.table.rows().map(a => a.username)).toEqual(['alfa', 'zeta']);
  });

  it('identifies the current admin', async () => {
    const { page } = await setup(undefined);

    expect(page.isSelf(admins[0])).toBe(true);
    expect(page.isSelf(admins[1])).toBe(false);
  });

  it('reloads after a saved dialog', async () => {
    const { api, page, snackBar } = await setup(admins[0]);

    await page.openDialog();

    expect(api.list).toHaveBeenCalledTimes(2);
    expect(snackBar.open).toHaveBeenCalledWith('admins.created', undefined, expect.anything());
  });

  it('deletes only after confirmation', async () => {
    const confirmed = await setup(true);
    await confirmed.page.confirmDelete(admins[1]);
    expect(confirmed.api.delete).toHaveBeenCalledWith(2);

    TestBed.resetTestingModule();
    const cancelled = await setup(false);
    await cancelled.page.confirmDelete(admins[1]);
    expect(cancelled.api.delete).not.toHaveBeenCalled();
  });

  it('reports delete errors with their code', async () => {
    const { api, page, snackBar } = await setup(true);
    api.delete.mockRejectedValue(new HttpErrorResponse({ status: 422, error: { code: 'admins.cannotDeleteSelf' } }));

    await page.confirmDelete(admins[0]);

    expect(snackBar.open).toHaveBeenCalledWith('errors.codes.admins.cannotDeleteSelf', undefined, expect.anything());
  });
});
