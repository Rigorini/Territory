import { HttpClient } from '@angular/common/http';
import { inject, Injectable, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';

interface VersionResponse {
  version: string | null;
}

/** Version of the deployed software, configured on the server (appsettings.json). */
@Injectable({ providedIn: 'root' })
export class AppInfoService {
  private readonly http = inject(HttpClient);
  private readonly current = signal<string | null>(null);

  readonly version = this.current.asReadonly();

  async loadVersion(): Promise<void> {
    try {
      const { version } = await firstValueFrom(this.http.get<VersionResponse>('/api/version'));
      this.current.set(version?.trim() || null);
    } catch {
      // The version is only displayed: when it cannot be read the menu omits it.
    }
  }
}
