import { TestBed } from '@angular/core/testing';
import { Type } from '@angular/core';
import { HttpErrorResponse } from '@angular/common/http';
import { MAT_DIALOG_DATA, MatDialogRef } from '@angular/material/dialog';
import { provideTranslateService } from '@ngx-translate/core';
import { MunicipalitiesApi, MunicipalityDto, TerritoriesApi, TerritoryDto } from './territories.api';
import { TerritoryDialog, TerritoryDialogData } from './territory-dialog';
import { MunicipalityDialog, MunicipalityDialogData } from './municipality-dialog';
import { Feedback } from '../shared/feedback';

const municipalities: MunicipalityDto[] = [{ id: 1, name: 'Giano', territoryCount: 1 }];
const mapUrl = 'https://www.google.com/maps/d/viewer?mid=1AbC_def-GHIjkl';
const territory: TerritoryDto = {
  id: 4, municipalityId: 1, municipalityName: 'Giano', number: 3, name: 'Centro', mapUrl, assignmentCount: 0,
};

interface DialogApi {
  form: { controls: Record<string, { setValue(v: unknown): void }> };
  save(): Promise<void>;
  serverErrors(control: string): string[];
  errorKey(): string | null;
}

function setup<T>(component: Type<T>, data: unknown, confirmed = true) {
  const api = { create: vi.fn(), update: vi.fn() };
  const dialogRef = { close: vi.fn() };
  const feedback = { confirm: vi.fn().mockResolvedValue(confirmed) };
  TestBed.configureTestingModule({
    imports: [component],
    providers: [
      provideTranslateService(),
      { provide: TerritoriesApi, useValue: api },
      { provide: MunicipalitiesApi, useValue: api },
      { provide: Feedback, useValue: feedback },
      { provide: MatDialogRef, useValue: dialogRef },
      { provide: MAT_DIALOG_DATA, useValue: data },
    ],
  });
  const fixture = TestBed.createComponent(component);
  fixture.detectChanges();
  return { api, dialogRef, feedback, dialog: fixture.componentInstance as unknown as DialogApi, fixture };
}

describe('TerritoryDialog', () => {
  const create = () => setup(TerritoryDialog, { municipalities } satisfies TerritoryDialogData);

  function fill(dialog: DialogApi, values: Record<string, unknown>): void {
    for (const [name, value] of Object.entries(values)) {
      dialog.form.controls[name].setValue(value);
    }
  }

  it('does not submit invalid values', async () => {
    const { api, dialog, feedback } = create();
    fill(dialog, { municipalityId: 1, number: 10000, name: 'Centro', mapUrl });
    await dialog.save();

    fill(dialog, { number: 3, mapUrl: 'https://example.com/maps/d/edit?mid=1AbC_def-GHIjkl' });
    await dialog.save();

    expect(feedback.confirm).not.toHaveBeenCalled();
    expect(api.create).not.toHaveBeenCalled();
  });

  it('confirms with a summary, then creates trimmed values', async () => {
    const { api, dialog, dialogRef, feedback } = create();
    api.create.mockResolvedValue(territory);
    fill(dialog, { municipalityId: 1, number: 3, name: ' Centro ', mapUrl: ` ${mapUrl} ` });

    await dialog.save();

    expect(feedback.confirm).toHaveBeenCalledWith(expect.objectContaining({
      params: { number: 3, name: 'Centro', municipality: 'Giano' },
    }));
    expect(api.create).toHaveBeenCalledWith({ municipalityId: 1, number: 3, name: 'Centro', mapUrl });
    expect(dialogRef.close).toHaveBeenCalledWith(territory);
  });

  it('does nothing when the confirmation is cancelled', async () => {
    const { api, dialog } = setup(TerritoryDialog, { territory, municipalities: [] } satisfies TerritoryDialogData, false);

    await dialog.save();

    expect(api.update).not.toHaveBeenCalled();
  });

  it('updates and shows a number conflict on the field', async () => {
    const { api, dialog, dialogRef, fixture } = setup(TerritoryDialog, { territory, municipalities });
    api.update.mockRejectedValue(new HttpErrorResponse({ status: 409, error: { code: 'territories.numberTaken' } }));

    await dialog.save();
    fixture.detectChanges();

    expect(api.update).toHaveBeenCalledWith(4, { municipalityId: 1, number: 3, name: 'Centro', mapUrl });
    expect(dialog.serverErrors('number')).toEqual(['numberTaken']);
    expect(dialogRef.close).not.toHaveBeenCalled();
  });

  it('shows server field errors', async () => {
    const { api, dialog } = setup(TerritoryDialog, { territory, municipalities });
    api.update.mockRejectedValue(new HttpErrorResponse({
      status: 400, error: { code: 'validation', errors: { municipalityId: ['notFound'] } },
    }));

    await dialog.save();

    expect(dialog.serverErrors('municipalityId')).toEqual(['notFound']);
  });
});

describe('MunicipalityDialog', () => {
  it('creates without confirmation', async () => {
    const { api, dialog, dialogRef, feedback } = setup(MunicipalityDialog, {} satisfies MunicipalityDialogData);
    api.create.mockResolvedValue(municipalities[0]);
    dialog.form.controls['name'].setValue(' Giano ');

    await dialog.save();

    expect(feedback.confirm).not.toHaveBeenCalled();
    expect(api.create).toHaveBeenCalledWith({ name: 'Giano' });
    expect(dialogRef.close).toHaveBeenCalledWith(municipalities[0]);
  });

  it('renames and maps the name conflict and other errors', async () => {
    const { api, dialog } = setup(MunicipalityDialog, { municipality: municipalities[0] } satisfies MunicipalityDialogData);
    api.update.mockRejectedValueOnce(new HttpErrorResponse({ status: 409, error: { code: 'municipalities.nameTaken' } }));
    await dialog.save();
    expect(api.update).toHaveBeenCalledWith(1, { name: 'Giano' });
    expect(dialog.serverErrors('name')).toEqual(['nameTaken']);

    dialog.form.controls['name'].setValue('Giano');
    api.update.mockRejectedValueOnce(new HttpErrorResponse({ status: 404, error: { code: 'municipalities.notFound' } }));
    await dialog.save();
    expect(dialog.errorKey()).toBe('errors.codes.municipalities.notFound');
  });
});
