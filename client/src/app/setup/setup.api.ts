import { HttpClient } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { AuthService } from '../core/auth.service';

export interface FirstAdminRequest {
  username: string;
  displayName: string;
  password: string;
}

/** First-access setup: available only while the installation has no administrator. */
@Injectable({ providedIn: 'root' })
export class SetupApi {
  private readonly http = inject(HttpClient);
  private readonly auth = inject(AuthService);

  async isRequired(): Promise<boolean> {
    return (await firstValueFrom(this.http.get<{ required: boolean }>('/api/setup/status'))).required;
  }

  async createFirstAdmin(request: FirstAdminRequest): Promise<void> {
    await this.auth.refreshXsrfToken();
    await firstValueFrom(this.http.post('/api/setup', request));
  }
}
