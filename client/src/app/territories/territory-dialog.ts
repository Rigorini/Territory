import { Component, inject } from '@angular/core';
import { NonNullableFormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { MAT_DIALOG_DATA, MatDialogModule } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { MatButtonModule } from '@angular/material/button';
import { TranslatePipe } from '@ngx-translate/core';
import { MunicipalityDto, TerritoriesApi, TerritoryDto } from './territories.api';
import { MYMAPS_URL_MAX_LENGTH, myMapsUrlValidator } from './mymaps-link';
import { Feedback } from '../shared/feedback';
import { FormDialog } from '../shared/form-dialog';

export interface TerritoryDialogData {
  territory?: TerritoryDto;
  municipalities: readonly MunicipalityDto[];
}

export const TERRITORY_NUMBER_MAX = 9999;
export const TERRITORY_NAME_MAX_LENGTH = 100;

/** Create/edit dialog with a confirmation step; closes with the saved territory. */
@Component({
  selector: 'app-territory-dialog',
  imports: [
    ReactiveFormsModule, MatDialogModule, MatFormFieldModule, MatInputModule, MatSelectModule,
    MatButtonModule, TranslatePipe,
  ],
  templateUrl: './territory-dialog.html',
  styleUrl: '../shared/form-dialog.scss',
})
export class TerritoryDialog extends FormDialog<TerritoryDto> {
  private readonly api = inject(TerritoriesApi);
  private readonly feedback = inject(Feedback);
  protected readonly data = inject<TerritoryDialogData>(MAT_DIALOG_DATA);

  protected readonly maxNumber = TERRITORY_NUMBER_MAX;
  protected readonly maxNameLength = TERRITORY_NAME_MAX_LENGTH;
  protected readonly maxUrlLength = MYMAPS_URL_MAX_LENGTH;
  protected readonly form = inject(NonNullableFormBuilder).group({
    municipalityId: [this.data.territory?.municipalityId ?? null as number | null, Validators.required],
    number: [this.data.territory?.number ?? null as number | null,
      [Validators.required, Validators.min(1), Validators.max(this.maxNumber), Validators.pattern(/^\d+$/)]],
    name: [this.data.territory?.name ?? '', [Validators.required, Validators.maxLength(this.maxNameLength)]],
    mapUrl: [this.data.territory?.mapUrl ?? '',
      [Validators.required, Validators.maxLength(this.maxUrlLength), myMapsUrlValidator]],
  });
  protected override readonly conflictFields = { 'territories.numberTaken': ['number', 'numberTaken'] as const };

  protected override confirmSave(): Promise<boolean> {
    const { number, name } = this.form.getRawValue();
    return this.feedback.confirm({
      titleKey: 'territories.confirmSaveTitle',
      messageKey: 'territories.confirmSaveMessage',
      params: { number, name: name.trim(), municipality: this.municipalityName() },
      confirmKey: 'common.save',
    });
  }

  protected send(): Promise<TerritoryDto> {
    const { municipalityId, number, name, mapUrl } = this.form.getRawValue();
    const request = { municipalityId, number, name: name.trim(), mapUrl: mapUrl.trim() };
    const territory = this.data.territory;
    return territory ? this.api.update(territory.id, request) : this.api.create(request);
  }

  private municipalityName(): string {
    const id = this.form.controls.municipalityId.value;
    return this.data.municipalities.find(m => m.id === id)?.name ?? '';
  }
}
