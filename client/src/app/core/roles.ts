/** Mirrors the numeric AdminRole enum of the API. */
export const AdminRole = { Administrator: 1, Manager: 2, Assigner: 3 } as const;
export type AdminRoleValue = (typeof AdminRole)[keyof typeof AdminRole];

export const ADMIN_ROLES: readonly AdminRoleValue[] = [AdminRole.Administrator, AdminRole.Manager, AdminRole.Assigner];

/** Roles allowed to manage publishers, export the register and back up. */
export const MANAGER_ROLES: readonly AdminRoleValue[] = [AdminRole.Administrator, AdminRole.Manager];

/** Only administrators. */
export const ADMINISTRATOR_ONLY: readonly AdminRoleValue[] = [AdminRole.Administrator];
