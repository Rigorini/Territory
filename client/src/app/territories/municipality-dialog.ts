import { Component, inject } from '@angular/core';
import { NonNullableFormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { MAT_DIALOG_DATA, MatDialogModule } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatButtonModule } from '@angular/material/button';
import { TranslatePipe } from '@ngx-translate/core';
import { MunicipalitiesApi, MunicipalityDto } from './territories.api';
import { FormDialog } from '../shared/form-dialog';

export interface MunicipalityDialogData {
  municipality?: MunicipalityDto;
}

export const MUNICIPALITY_NAME_MAX_LENGTH = 100;

/** Create/rename dialog; closes with the saved municipality. */
@Component({
  selector: 'app-municipality-dialog',
  imports: [ReactiveFormsModule, MatDialogModule, MatFormFieldModule, MatInputModule, MatButtonModule, TranslatePipe],
  templateUrl: './municipality-dialog.html',
  styleUrl: '../shared/form-dialog.scss',
})
export class MunicipalityDialog extends FormDialog<MunicipalityDto> {
  private readonly api = inject(MunicipalitiesApi);
  protected readonly data = inject<MunicipalityDialogData>(MAT_DIALOG_DATA);

  protected readonly maxLength = MUNICIPALITY_NAME_MAX_LENGTH;
  protected readonly form = inject(NonNullableFormBuilder).group({
    name: [this.data.municipality?.name ?? '', [Validators.required, Validators.maxLength(this.maxLength)]],
  });
  protected override readonly conflictFields = { 'municipalities.nameTaken': ['name', 'nameTaken'] as const };

  protected send(): Promise<MunicipalityDto> {
    const request = { name: this.form.getRawValue().name.trim() };
    const municipality = this.data.municipality;
    return municipality ? this.api.update(municipality.id, request) : this.api.create(request);
  }
}
