import { Injectable } from '@angular/core';
import { CrudApi } from '../shared/crud-api';
import { AdminRoleValue } from '../core/roles';

export interface AdminDto {
  id: number;
  /** Login name. */
  username: string;
  /** Name of the person, shown in the history. */
  displayName: string;
  createdAt: string;
  isLocked: boolean;
  role: AdminRoleValue;
}

export interface SaveAdminRequest {
  username: string;
  displayName: string;
  /** Required on create; empty on update keeps the current password. */
  password: string | null;
  role?: AdminRoleValue;
}

@Injectable({ providedIn: 'root' })
export class AdminsApi extends CrudApi<AdminDto, SaveAdminRequest> {
  constructor() {
    super('/api/admins');
  }
}
