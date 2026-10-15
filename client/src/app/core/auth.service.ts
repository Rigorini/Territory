import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { computed, inject, Injectable, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { AdminRoleValue } from './roles';

export interface AdminProfile {
  id: number;
  username: string;
  /** Name of the person. */
  displayName: string;
  theme: number;
  language: string | null;
  role: AdminRoleValue;
  /** True until the user replaces the password chosen by the administrator. */
  mustChangePassword?: boolean;
}

/** Session state of the logged-in administrator; the session itself lives in an HttpOnly cookie. */
@Injectable({ providedIn: 'root' })
export class AuthService {
  private readonly http = inject(HttpClient);
  private readonly profile = signal<AdminProfile | null>(null);

  readonly user = this.profile.asReadonly();
  readonly isAuthenticated = computed(() => this.profile() !== null);

  hasRole(allowed: readonly AdminRoleValue[]): boolean {
    const role = this.profile()?.role;
    return role !== undefined && allowed.includes(role);
  }

  /** Obtains a fresh antiforgery token cookie (required before the first state-changing call). */
  refreshXsrfToken(): Promise<void> {
    return firstValueFrom(this.http.get<void>('/api/auth/antiforgery'));
  }

  async login(username: string, password: string): Promise<AdminProfile> {
    await this.refreshXsrfToken();
    const profile = await firstValueFrom(
      this.http.post<AdminProfile>('/api/auth/login', { username, password }),
    );
    this.profile.set(profile);
    return profile;
  }

  async logout(): Promise<void> {
    try {
      await firstValueFrom(this.http.post<void>('/api/auth/logout', null));
    } catch (error) {
      // 400/401 mean the session (and its antiforgery token) had already expired: nothing left to end.
      if (!isExpiredSessionError(error)) {
        throw error;
      }
    } finally {
      this.profile.set(null);
    }
  }

  /** Returns true when a valid session exists, loading the profile on first use. */
  async loadProfile(): Promise<boolean> {
    if (this.profile()) {
      return true;
    }
    try {
      this.profile.set(await firstValueFrom(this.http.get<AdminProfile>('/api/auth/me')));
      return true;
    } catch (error) {
      if (error instanceof HttpErrorResponse && error.status === 401) {
        return false;
      }
      throw error;
    }
  }

  /** Changes the own password; the server keeps this session alive and signs out the others. */
  async changePassword(currentPassword: string, password: string): Promise<void> {
    await firstValueFrom(this.http.put<void>('/api/auth/me/password', { currentPassword, password }));
  }

  /** Replaces the cached profile after the server returned an updated copy. */
  updateProfile(profile: AdminProfile): void {
    this.profile.set(profile);
  }

  clear(): void {
    this.profile.set(null);
  }
}

function isExpiredSessionError(error: unknown): boolean {
  return error instanceof HttpErrorResponse && (error.status === 400 || error.status === 401);
}
