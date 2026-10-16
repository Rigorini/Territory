import { Component, computed, inject, signal } from '@angular/core';
import { NonNullableFormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatAutocompleteModule } from '@angular/material/autocomplete';
import { MAT_DIALOG_DATA, MatDialogModule } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
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
    ReactiveFormsModule, MatAutocompleteModule, MatDialogModule, MatFormFieldModule, MatInputModule,
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

  private readonly names = new Map<number, string>(
    this.data.publishers.map(p => [p.id, `${p.lastName} ${p.firstName}`]));
  /** Text typed in the publisher field, which keeps the selected id as its value. */
  protected readonly query = signal('');
  protected readonly publishers = computed(() => filterPublishers(this.data.publishers, this.query()));

  /** Shows the selected publisher's name in the field, which holds its id. */
  protected readonly publisherName = (id: number | null): string => (id == null ? '' : this.names.get(id) ?? '');

  protected onQuery(event: Event): void {
    this.query.set((event.target as HTMLInputElement).value);
  }

  protected send(): Promise<ShareLinkDto> {
    const { publisherId, assignedAt } = this.form.getRawValue();
    return this.api.assign({
      territoryId: this.data.territory.territoryId,
      publisherId: publisherId as number,
      assignedAt: eventInstant(assignedAt as Date),
    });
  }
}

/** Publishers whose "lastName firstName" contains the typed text, case-insensitively. */
function filterPublishers(publishers: readonly PublisherDto[], query: string): readonly PublisherDto[] {
  const needle = query.trim().toLocaleLowerCase();
  return needle === ''
    ? publishers
    : publishers.filter(p => `${p.lastName} ${p.firstName}`.toLocaleLowerCase().includes(needle));
}
