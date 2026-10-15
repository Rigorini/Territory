import { Component, inject } from '@angular/core';
import { NonNullableFormBuilder, ReactiveFormsModule } from '@angular/forms';
import { MAT_DIALOG_DATA, MatDialogModule } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatButtonModule } from '@angular/material/button';
import { MatDatepickerModule } from '@angular/material/datepicker';
import { TranslatePipe } from '@ngx-translate/core';
import { AssignmentsApi, OpenAssignmentDto, TerritoryStatusDto } from './assignments.api';
import { dayRangeValidator, eventInstant, minDayAfter, startOfToday } from './event-date';
import { FormDialog } from '../shared/form-dialog';

export interface ReturnDialogData {
  territory: TerritoryStatusDto;
  assignment: OpenAssignmentDto;
}

/** Registers the return of an assigned territory on behalf of the publisher; closes with true. */
@Component({
  selector: 'app-return-dialog',
  imports: [
    ReactiveFormsModule, MatDialogModule, MatFormFieldModule, MatInputModule, MatDatepickerModule, MatButtonModule,
    TranslatePipe,
  ],
  templateUrl: './return-dialog.html',
  styleUrl: '../shared/form-dialog.scss',
})
export class ReturnDialog extends FormDialog<true> {
  private readonly api = inject(AssignmentsApi);
  protected readonly data = inject<ReturnDialogData>(MAT_DIALOG_DATA);

  protected readonly today = startOfToday();
  protected readonly minDay = minDayAfter(this.data.assignment.assignedAt);
  protected readonly form = inject(NonNullableFormBuilder).group({
    returnedAt: [this.today as Date | null, dayRangeValidator(this.minDay, 'beforeAssigned')],
  });

  protected async send(): Promise<true> {
    await this.api.registerReturn(this.data.assignment.id, eventInstant(this.form.getRawValue().returnedAt as Date));
    return true;
  }
}
