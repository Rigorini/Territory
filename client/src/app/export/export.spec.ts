import { TestBed } from '@angular/core/testing';
import { By } from '@angular/platform-browser';
import { MatDatepicker } from '@angular/material/datepicker';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideTranslateService } from '@ngx-translate/core';
import { currentServiceYear, ExportApi, ExportQuery, s13Params, toParams } from './export.api';
import { ExportPage, periodValidator } from './export-page';
import { FormControl, FormGroup } from '@angular/forms';
import { toApiError } from '../core/api-error';
import { LanguageService } from '../core/language.service';
import { Feedback } from '../shared/feedback';
import { provideDayDateAdapter } from '../shared/day-date-adapter';
import { MunicipalitiesApi } from '../territories/territories.api';

const query: ExportQuery = { format: 'xlsx', municipalityId: null, from: '', to: '', language: 'it' };

describe('toParams', () => {
  it('sends only the filters that are set, with the browser time zone', () => {
    const all = toParams(query);
    expect(all.keys()).toEqual(['format', 'timeZone', 'language']);
    expect(all.get('timeZone')).toBe(Intl.DateTimeFormat().resolvedOptions().timeZone);

    const filtered = toParams({ ...query, format: 'docx', municipalityId: 3, from: '2025-01-01', to: '2025-12-31' });
    expect(filtered.toString()).toContain('format=docx');
    expect([filtered.get('municipalityId'), filtered.get('from'), filtered.get('to')]).toEqual(['3', '2025-01-01', '2025-12-31']);
  });
});

describe('ExportApi', () => {
  let api: ExportApi;
  let http: HttpTestingController;
  let anchor: HTMLAnchorElement;

  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    api = TestBed.inject(ExportApi);
    http = TestBed.inject(HttpTestingController);
    anchor = document.createElement('a');
    vi.spyOn(anchor, 'click').mockImplementation(() => undefined);
    vi.spyOn(document, 'createElement').mockReturnValue(anchor);
    URL.createObjectURL = vi.fn().mockReturnValue('blob:register');
    URL.revokeObjectURL = vi.fn();
  });

  afterEach(() => {
    http.verify();
    vi.restoreAllMocks();
  });

  it('downloads the register with a dated file name', async () => {
    vi.useFakeTimers();
    const download = api.download(query, new Date(2025, 2, 10));

    const req = http.expectOne(r => r.url === '/api/export/register');
    expect(req.request.responseType).toBe('blob');
    req.flush(new Blob(['data']));
    await download;

    expect(anchor.download).toBe('registro-territori-2025-03-10.xlsx');
    expect(anchor.href).toBe('blob:register');
    expect(anchor.click).toHaveBeenCalledOnce();
    vi.runAllTimers();
    expect(URL.revokeObjectURL).toHaveBeenCalledWith('blob:register');
    vi.useRealTimers();
  });

  it('downloads the S-13 register with the year and language in the file name', async () => {
    vi.useFakeTimers();
    const download = api.downloadS13(2026, 'it');

    const req = http.expectOne(r => r.url === '/api/export/s13');
    expect(req.request.params.get('serviceYear')).toBe('2026');
    req.flush(new Blob(['data']));
    await download;

    expect(anchor.download).toBe('S-13-2026-it.docx');
    expect(anchor.click).toHaveBeenCalledOnce();
    vi.runAllTimers();
    vi.useRealTimers();
  });

  it('exposes the error code of a ProblemDetails blob', async () => {
    const download = api.download(query);
    const body = new Blob([JSON.stringify({ code: 'validation' })], { type: 'application/problem+json' });
    http.expectOne(r => r.url === '/api/export/register').flush(body, { status: 400, statusText: 'Bad Request' });

    const error = await download.catch((e: unknown) => e);
    expect(toApiError(error).code).toBe('validation');
  });

  it('falls back to a generic error for non-JSON blobs and network errors', async () => {
    const first = api.download(query);
    http.expectOne(r => r.url === '/api/export/register').flush(new Blob(['<html>']), { status: 502, statusText: 'Bad Gateway' });
    expect(toApiError(await first.catch((e: unknown) => e)).code).toBe('generic');

    const second = api.download(query);
    http.expectOne(r => r.url === '/api/export/register').error(new ProgressEvent('error'));
    expect(toApiError(await second.catch((e: unknown) => e)).code).toBe('network');
  });
});

describe('service year', () => {
  it('starts in September and is named after the year it ends in', () => {
    expect(currentServiceYear(new Date(2025, 7, 31))).toBe(2025);
    expect(currentServiceYear(new Date(2025, 8, 1))).toBe(2026);
    expect(currentServiceYear(new Date(2026, 0, 15))).toBe(2026);
  });

  it('sends the year, the time zone and the language', () => {
    const params = s13Params(2026, 'it');
    expect([params.get('serviceYear'), params.get('language'), params.get('timeZone')]).toEqual(
      ['2026', 'it', Intl.DateTimeFormat().resolvedOptions().timeZone]);
  });
});

describe('periodValidator', () => {
  const check = (from: Date | null, to: Date | null) =>
    periodValidator(new FormGroup({ from: new FormControl(from), to: new FormControl(to) }));

  it('rejects only an end day before the start day', () => {
    expect(check(new Date(2025, 1, 1), new Date(2025, 0, 31))).toEqual({ periodOrder: true });
    expect(check(new Date(2025, 0, 1), new Date(2025, 0, 1))).toBeNull();
    expect(check(null, new Date(2025, 0, 1))).toBeNull();
    expect(check(new Date(2025, 0, 1), null)).toBeNull();
  });
});

describe('ExportPage', () => {
  interface PageApi {
    form: FormGroup;
    busy(): boolean;
    download(format: 'xlsx' | 'docx'): Promise<void>;
    downloadS13(): Promise<void>;
    serviceYear: FormControl<number | null>;
  }

  async function setup(downloadResult: Promise<void> = Promise.resolve()) {
    const exportApi = { download: vi.fn().mockReturnValue(downloadResult), downloadS13: vi.fn().mockReturnValue(downloadResult) };
    const feedback = { notifyError: vi.fn() };
    TestBed.configureTestingModule({
      imports: [ExportPage],
      providers: [
        provideTranslateService(),
        provideDayDateAdapter(),
        { provide: ExportApi, useValue: exportApi },
        { provide: MunicipalitiesApi, useValue: { list: vi.fn().mockResolvedValue([{ id: 3, name: 'Giano', territoryCount: 2 }]) } },
        { provide: Feedback, useValue: feedback },
      ],
    });
    TestBed.inject(LanguageService).use('en');
    const fixture = TestBed.createComponent(ExportPage);
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
    return { exportApi, feedback, fixture, page: fixture.componentInstance as unknown as PageApi };
  }

  it('downloads with the chosen filters and the active language', async () => {
    const { page, exportApi, fixture } = await setup();
    page.form.setValue({ municipalityId: 3, from: new Date(2025, 0, 1), to: new Date(2025, 5, 30) });

    const buttons = (fixture.nativeElement as HTMLElement).querySelector('form')!.querySelectorAll('.actions button');
    expect(buttons).toHaveLength(2);
    (buttons[1] as HTMLButtonElement).click();
    await fixture.whenStable();

    expect(exportApi.download).toHaveBeenCalledWith(
      { format: 'docx', language: 'en', municipalityId: 3, from: '2025-01-01', to: '2025-06-30' });
    expect(page.busy()).toBe(false);
  });

  it('sends no period when the dates are empty', async () => {
    const { page, exportApi } = await setup();

    await page.download('xlsx');

    expect(exportApi.download).toHaveBeenCalledWith(
      { format: 'xlsx', language: 'en', municipalityId: null, from: '', to: '' });
  });

  it('flags typed dates that are not dd/MM/yyyy', async () => {
    const { page, exportApi, fixture } = await setup();
    const element = fixture.nativeElement as HTMLElement;
    const [from, to] = [...element.querySelectorAll('input')] as HTMLInputElement[];
    for (const input of [from, to]) {
      input.value = '2025-13-01';
      input.dispatchEvent(new Event('input'));
      input.dispatchEvent(new Event('blur'));
    }
    page.form.markAllAsTouched();
    fixture.detectChanges();

    await page.download('xlsx');

    expect(element.querySelectorAll('mat-error')).toHaveLength(2);
    expect(element.textContent).toContain('errors.fields.invalidDate');
    expect(exportApi.download).not.toHaveBeenCalled();
  });

  it('takes the period from the calendar, never from the keyboard', async () => {
    const { fixture } = await setup();
    const [from, to] = [...(fixture.nativeElement as HTMLElement).querySelectorAll('input')] as HTMLInputElement[];

    expect([from.readOnly, to.readOnly]).toEqual([true, true]);
  });

  it('opens the calendar from anywhere in a period field', async () => {
    const { fixture } = await setup();
    const element = fixture.nativeElement as HTMLElement;
    const from = element.querySelector('input[formcontrolname="from"]')!.closest('mat-form-field') as HTMLElement;
    const pickers = fixture.debugElement.queryAll(By.directive(MatDatepicker)).map(d => d.injector.get(MatDatepicker));

    from.click();

    expect(pickers.map(p => p.opened)).toEqual([true, false]);
  });

  it('shows the period error and does not download an invalid period', async () => {
    const { page, exportApi, fixture } = await setup();
    page.form.patchValue({ from: new Date(2025, 1, 1), to: new Date(2025, 0, 1) });
    fixture.detectChanges();

    await page.download('xlsx');

    expect((fixture.nativeElement as HTMLElement).querySelector('[role=alert]')).not.toBeNull();
    expect(exportApi.download).not.toHaveBeenCalled();
  });

  it('downloads the S-13 of the chosen service year in the active language', async () => {
    const { page, exportApi } = await setup();
    page.serviceYear.setValue(2025);

    await page.downloadS13();

    expect(exportApi.downloadS13).toHaveBeenCalledWith(2025, 'en');
  });

  it.each([null, 1999, 2101, 20.5, 202])('does not download the S-13 for the invalid year %s', async year => {
    const { page, exportApi } = await setup();
    page.serviceYear.setValue(year);

    await page.downloadS13();

    expect(exportApi.downloadS13).not.toHaveBeenCalled();
  });

  it('ignores clicks while busy and notifies download errors', async () => {
    let fail: (reason: unknown) => void = () => undefined;
    const { page, exportApi, feedback } = await setup(new Promise<void>((_, reject) => (fail = reject)));

    const first = page.download('xlsx');
    await page.download('docx');
    expect(exportApi.download).toHaveBeenCalledOnce();

    fail(new Error('boom'));
    await first;
    expect(feedback.notifyError).toHaveBeenCalledOnce();
    expect(page.busy()).toBe(false);
  });
});
