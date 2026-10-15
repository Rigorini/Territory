import { Component, inject } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { NonNullableFormBuilder, ReactiveFormsModule } from '@angular/forms';
import { MAT_DIALOG_DATA, MatDialogModule } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatButtonModule } from '@angular/material/button';
import { MatDatepickerModule } from '@angular/material/datepicker';
import { TranslatePipe } from '@ngx-translate/core';
import { AssignmentHistoryDto, AssignmentsApi, TerritoryStatusDto } from './assignments.api';
import {
  correctedPeriod, dayDate, dayRangeValidator, localDay, minDayAfter, periodEndValidator, startOfToday,
} from './event-date';
import { FormDialog } from '../shared/form-dialog';

export interface EditDatesDialogData {
  territory: TerritoryStatusDto;
  entry: AssignmentHistoryDto;
  /** Older assignment of the same territory, if any. */
  previous: AssignmentHistoryDto | null;
  /** Newer assignment of the same territory, if any. */
  next: AssignmentHistoryDto | null;
}

const dayOf = (value: string): string => localDay(new Date(value));

/** Corrects the assignment and return dates of a history entry; closes with true. */
@Component({
  selector: 'app-edit-dates-dialog',
  imports: [
    ReactiveFormsModule, MatDialogModule, MatFormFieldModule, MatInputModule, MatDatepickerModule, MatButtonModule,
    TranslatePipe,
  ],
  templateUrl: './edit-dates-dialog.html',
  styleUrl: '../shared/form-dialog.scss',
})
export class EditDatesDialog extends FormDialog<true> {
  private readonly api = inject(AssignmentsApi);
  protected readonly data = inject<EditDatesDialogData>(MAT_DIALOG_DATA);

  protected readonly today = startOfToday();
  protected readonly isReturned = this.data.entry.returnedAt !== null;
  protected readonly minAssignedDay = minDayAfter(this.data.previous?.returnedAt);
  private readonly nextDay = this.data.next ? dayOf(this.data.next.assignedAt) : null;
  protected readonly maxReturnedDay =
    this.nextDay !== null && this.nextDay < localDay(this.today) ? dayDate(this.nextDay) : this.today;

  protected readonly form = inject(NonNullableFormBuilder).group({
    assignedAt: [
      dayDate(dayOf(this.data.entry.assignedAt)) as Date | null,
      dayRangeValidator(this.minAssignedDay, 'beforeLastReturn'),
    ],
    returnedAt: [
      {
        value: this.data.entry.returnedAt ? dayDate(dayOf(this.data.entry.returnedAt)) : null,
        disabled: !this.isReturned,
      },
      periodEndValidator('assignedAt', 'beforeAssigned', this.nextDay, 'afterNextAssignment'),
    ],
  });

  constructor() {
    super();
    const returnedAt = this.form.controls.returnedAt;
    this.form.controls.assignedAt.valueChanges.pipe(takeUntilDestroyed())
      .subscribe(() => returnedAt.updateValueAndValidity());
  }

  protected async send(): Promise<true> {
    const { assignedAt, returnedAt } = this.form.getRawValue();
    const period = correctedPeriod(
      this.data.entry,
      { assignedAt: assignedAt as Date, returnedAt },
      { notBefore: this.data.previous?.returnedAt ?? null, notAfter: this.data.next?.assignedAt ?? null });
    await this.api.updateDates(this.data.entry.id, period);
    return true;
  }
}
