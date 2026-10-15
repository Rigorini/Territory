import { DOCUMENT, inject, Injectable } from '@angular/core';
import { HttpClient, HttpErrorResponse, HttpHeaders, HttpResponse } from '@angular/common/http';
import { firstValueFrom } from 'rxjs';

export interface BackupImportSummary {
  municipalities: number;
  territories: number;
  publishers: number;
  assignments: number;
}

const FALLBACK_FILE_NAME = 'backup-territori.ter';
const FILE_NAME_PATTERN = /filename="?([^";]+)"?/i;

/** Exports and restores the registry backup (publishers, territories, assignments; no users). */
@Injectable({ providedIn: 'root' })
export class BackupApi {
  private readonly http = inject(HttpClient);
  private readonly document = inject(DOCUMENT);

  async download(): Promise<void> {
    let response: HttpResponse<Blob>;
    try {
      response = await firstValueFrom(this.http.get('/api/backup/export', { observe: 'response', responseType: 'blob' }));
    } catch (error) {
      throw await withJsonBody(error);
    }
    const disposition = response.headers.get('Content-Disposition') ?? '';
    this.save(response.body ?? new Blob(), FILE_NAME_PATTERN.exec(disposition)?.[1] ?? FALLBACK_FILE_NAME);
  }

  /** Replaces every publisher, territory and assignment with the content of the file. */
  import(file: File): Promise<BackupImportSummary> {
    return firstValueFrom(
      this.http.post<BackupImportSummary>('/api/backup/import', file, {
        params: { confirmOverwrite: true },
        headers: new HttpHeaders({ 'Content-Type': 'application/octet-stream' }),
      }),
    );
  }

  private save(blob: Blob, fileName: string): void {
    const url = URL.createObjectURL(blob);
    const link = this.document.createElement('a');
    link.href = url;
    link.download = fileName;
    link.click();
    // Revoked later: some browsers start reading the object URL only after click() returns.
    setTimeout(() => URL.revokeObjectURL(url));
  }
}

/** Blob requests receive ProblemDetails as a Blob: parse it so the error code can be translated. */
async function withJsonBody(error: unknown): Promise<unknown> {
  if (!(error instanceof HttpErrorResponse) || !(error.error instanceof Blob)) {
    return error;
  }
  let body: unknown = null;
  try {
    body = JSON.parse(await error.error.text());
  } catch {
    // Not JSON: keep a null body so the status-based generic message is shown.
  }
  return new HttpErrorResponse({ error: body, status: error.status, statusText: error.statusText, url: error.url ?? undefined });
}
