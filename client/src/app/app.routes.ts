import { Routes } from '@angular/router';
import { authGuard, passwordChangeGuard } from './core/auth.guard';
import { roleGuard } from './core/role.guard';
import { ADMINISTRATOR_ONLY, MANAGER_ROLES } from './core/roles';
import { loginGuard, setupGuard } from './setup/setup.guard';

export const routes: Routes = [
  {
    path: 't/:token',
    // The public map pulls in OpenLayers (~370 kB): load it only when the link is opened.
    data: { preload: false },
    loadComponent: () => import('./public/territory-view').then(m => m.TerritoryView),
  },
  {
    path: 'login',
    canActivate: [loginGuard],
    loadComponent: () => import('./pages/login/login').then(m => m.LoginPage),
  },
  {
    path: 'change-password',
    canActivate: [passwordChangeGuard],
    loadComponent: () => import('./pages/change-password/change-password').then(m => m.ChangePasswordPage),
  },
  {
    path: 'setup',
    canActivate: [setupGuard],
    loadComponent: () => import('./setup/setup-page').then(m => m.SetupPage),
  },
  {
    path: 'admin',
    canActivate: [authGuard],
    loadComponent: () => import('./layout/admin-shell').then(m => m.AdminShell),
    children: [
      { path: '', pathMatch: 'full', redirectTo: 'assignments' },
      {
        path: 'admins', canActivate: [roleGuard(ADMINISTRATOR_ONLY)],
        loadComponent: () => import('./admins/admins-page').then(m => m.AdminsPage),
      },
      {
        path: 'territories', canActivate: [roleGuard(ADMINISTRATOR_ONLY)],
        loadComponent: () => import('./territories/territories-page').then(m => m.TerritoriesPage),
      },
      {
        path: 'publishers', canActivate: [roleGuard(MANAGER_ROLES)],
        loadComponent: () => import('./publishers/publishers-page').then(m => m.PublishersPage),
      },
      {
        path: 'assignments/:territoryId/history',
        loadComponent: () => import('./assignments/history-page').then(m => m.HistoryPage),
      },
      { path: 'assignments', loadComponent: () => import('./assignments/assignments-page').then(m => m.AssignmentsPage) },
      {
        path: 'export', canActivate: [roleGuard(MANAGER_ROLES)],
        loadComponent: () => import('./export/export-page').then(m => m.ExportPage),
      },
      {
        path: 'backup', canActivate: [roleGuard(MANAGER_ROLES)],
        loadComponent: () => import('./backup/backup-page').then(m => m.BackupPage),
      },
    ],
  },
  { path: '', pathMatch: 'full', redirectTo: 'admin' },
  { path: '**', loadComponent: () => import('./pages/not-found').then(m => m.NotFound) },
];
