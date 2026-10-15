import { TestBed } from '@angular/core/testing';
import { HttpErrorResponse, provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideTranslateService } from '@ngx-translate/core';
import { toApiError } from '../core/api-error';
import { Feedback } from '../shared/feedback';
import { BackupApi } from './backup.api';
import { BackupPage } from './backup-page';

describe('BackupApi', () => {
  let api: BackupApi;
  let http: HttpTestingController;
  let anchor: HTMLAnchorElement;

  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    api = TestBed.inject(BackupApi);
    http = TestBed.inject(HttpTestingController);
    anchor = document.createElement('a');
    vi.spyOn(anchor, 'click').mockImplementation(() => undefined);
    vi.spyOn(document, 'createElement').mockReturnValue(anchor);
    URL.createObjectURL = vi.fn().mockReturnValue('blob:backup');
    URL.revokeObjectURL = vi.fn();
  });

  afterEach(() => {
    http.verify();
    vi.restoreAllMocks();
  });

  it('downloads the backup with the server file name', async () => {
    const download = api.download();
    http.expectOne('/api/backup/export').flush(new Blob(['{}']), {
      headers: { 'Content-Disposition': 'attachment; filename=backup-territori-20250310-101500.ter' },
    });
    await download;

    expect(anchor.download).toBe('backup-territori-20250310-101500.ter');
    expect(anchor.click).toHaveBeenCalledOnce();
  });

  it('falls back to a default file name', async () => {
    const download = api.download();
    http.expectOne('/api/backup/export').flush(new Blob(['{}']));
    await download;

    expect(anchor.download).toBe('backup-territori.ter');
  });

  it('exposes the error code of a ProblemDetails blob', async () => {
    const download = api.download();
    const body = new Blob([JSON.stringify({ code: 'validation' })], { type: 'application/problem+json' });
    http.expectOne('/api/backup/export').flush(body, { status: 400, statusText: 'Bad Request' });

    expect(toApiError(await download.catch((e: unknown) => e)).code).toBe('validation');
  });

  it('keeps a generic error when the blob is not JSON', async () => {
    const download = api.download();
    http.expectOne('/api/backup/export').flush(new Blob(['oops']), { status: 500, statusText: 'Server Error' });

    expect(toApiError(await download.catch((e: unknown) => e)).code).toBe('generic');
  });

  it('posts the file with the overwrite confirmation', async () => {
    const file = new File(['{}'], 'backup.json', { type: 'application/json' });
    const result = api.import(file);

    const req = http.expectOne(r => r.url === '/api/backup/import');
    expect(req.request.params.get('confirmOverwrite')).toBe('true');
    expect(req.request.headers.get('Content-Type')).toBe('application/octet-stream');
    expect(req.request.body).toBe(file);
    const summary = { municipalities: 1, territories: 2, publishers: 3, assignments: 4 };
    req.flush(summary);

    expect(await result).toEqual(summary);
  });
});

describe('BackupPage', () => {
  const feedback = { confirm: vi.fn(), notify: vi.fn(), notifyError: vi.fn() };
  const api = { download: vi.fn(), import: vi.fn() };
  const file = new File(['{}'], 'backup.json');

  function create(): { page: BackupPage; input: HTMLInputElement } {
    TestBed.configureTestingModule({
      providers: [
        provideTranslateService(),
        { provide: Feedback, useValue: feedback },
        { provide: BackupApi, useValue: api },
      ],
    });
    const page = TestBed.createComponent(BackupPage).componentInstance;
    const input = document.createElement('input');
    input.type = 'file';
    Object.defineProperty(input, 'files', { value: { item: () => file } });
    return { page, input };
  }

  const importWith = (page: BackupPage, input: HTMLInputElement): Promise<void> =>
    (page as unknown as { import(i: HTMLInputElement): Promise<void> }).import(input);

  beforeEach(() => {
    vi.resetAllMocks();
    api.download.mockResolvedValue(undefined);
  });

  it('does not import when the overwrite is not confirmed', async () => {
    feedback.confirm.mockResolvedValue(false);
    const { page, input } = create();

    await importWith(page, input);

    expect(feedback.confirm).toHaveBeenCalledOnce();
    expect(api.import).not.toHaveBeenCalled();
  });

  it('imports after confirmation and reports the counts', async () => {
    feedback.confirm.mockResolvedValue(true);
    const summary = { municipalities: 1, territories: 2, publishers: 3, assignments: 4 };
    api.import.mockResolvedValue(summary);
    const { page, input } = create();

    await importWith(page, input);

    expect(api.import).toHaveBeenCalledWith(file);
    expect(feedback.notify).toHaveBeenCalledWith('backup.imported', summary);
  });

  it('shows the file error inline instead of a generic message', async () => {
    feedback.confirm.mockResolvedValue(true);
    api.import.mockRejectedValue(new HttpErrorResponse({ status: 400, error: { code: 'validation', errors: { file: ['invalidBackup'] } } }));
    const { page, input } = create();

    await importWith(page, input);

    expect((page as unknown as { importErrorKey(): string | null }).importErrorKey()).toBe('errors.fields.invalidBackup');
    expect(feedback.notifyError).not.toHaveBeenCalled();
  });

  it('reports other errors through the shared feedback', async () => {
    feedback.confirm.mockResolvedValue(true);
    const error = new Error('boom');
    api.import.mockRejectedValue(error);
    const { page, input } = create();

    await importWith(page, input);

    expect(feedback.notifyError).toHaveBeenCalledWith(error);
  });

  it('exports through the API and reports failures', async () => {
    const error = new Error('boom');
    api.download.mockRejectedValue(error);
    const { page } = create();

    await (page as unknown as { export(): Promise<void> }).export();

    expect(feedback.notifyError).toHaveBeenCalledWith(error);
  });
});
