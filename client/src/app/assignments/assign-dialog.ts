import { Component, inject } from '@angular/core';
import { NonNullableFormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { MAT_DIALOG_DATA, MatDialogModule } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { MatButtonModule } from '@angular/material/button';
import { MatDatepickerModule } from '@angular/material/datepicker';
import { TranslatePipe } from '@ngx-translate/core';
import { AssignmentsApi, ShareLinkDto, TerritoryStatusDto } from './assignments.api';
import { dayRangeValidator, eventInstant, minDayAfter, startOfToday } from './event-date';
import { PublisherDto } from '../publishers/publishers.api';
import { FormDialog } from '../shared/form-dialog';

export interface AssignDialogData {
  territory: TerritoryStatusDto;
  publishers: readonly PublisherDto[];
}

/** Assigns an available territory; closes with the new share link. */
@Component({
  selector: 'app-assign-dialog',
  imports: [
    ReactiveFormsModule, MatDialogModule, MatFormFieldModule, MatInputModule, MatSelectModule,
    MatDatepickerModule, MatButtonModule, TranslatePipe,
  ],
  templateUrl: './assign-dialog.html',
  styleUrl: '../shared/form-dialog.scss',
})
export class AssignDialog extends FormDialog<ShareLinkDto> {
  private readonly api = inject(AssignmentsApi);
  protected readonly data = inject<AssignDialogData>(MAT_DIALOG_DATA);

  protected readonly today = startOfToday();
  protected readonly minDay = minDayAfter(this.data.territory.lastReturnedAt);
  protected readonly form = inject(NonNullableFormBuilder).group({
    publisherId: [null as number | null, Validators.required],
    assignedAt: [this.today as Date | null, dayRangeValidator(this.minDay, 'beforeLastReturn')],
  });

  protected send(): Promise<ShareLinkDto> {
    const { publisherId, assignedAt } = this.form.getRawValue();
    return this.api.assign({
      territoryId: this.data.territory.territoryId,
      publisherId: publisherId as number,
      assignedAt: eventInstant(assignedAt as Date),
    });
  }
}
