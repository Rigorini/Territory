import { Component, inject, OnInit, signal } from '@angular/core';
import { AbstractControl, FormBuilder, ReactiveFormsModule, ValidationErrors, Validators } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatDatepickerModule } from '@angular/material/datepicker';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { MatSelectModule } from '@angular/material/select';
import { TranslatePipe } from '@ngx-translate/core';
import { dayDate, localDay, MIN_EVENT_DAY } from '../assignments/event-date';
import { LanguageService } from '../core/language.service';
import { Feedback } from '../shared/feedback';
import { ListResource } from '../shared/list-resource';
import { MunicipalitiesApi } from '../territories/territories.api';
import { currentServiceYear, ExportApi, ExportFormat } from './export.api';

/** Period end before its start; the datepicker yields local midnights, so dates compare as days. */
export function periodValidator(group: AbstractControl): ValidationErrors | null {
  const { from, to } = group.value as { from: Date | null; to: Date | null };
  return from && to && to < from ? { periodOrder: true } : null;
}

export const MIN_SERVICE_YEAR = 2001;
export const MAX_SERVICE_YEAR = 2100;

function optionalDay(date: Date | null): string {
  return date ? localDay(date) : '';
}

/** Register export: optional municipality and period filters, downloaded as Excel or Word. */
@Component({
  selector: 'app-export-page',
  imports: [
    ReactiveFormsModule, MatButtonModule, MatDatepickerModule, MatFormFieldModule, MatIconModule, MatInputModule,
    MatProgressBarModule, MatSelectModule, TranslatePipe,
  ],
  templateUrl: './export-page.html',
  styleUrl: './export-page.scss',
})
export class ExportPage implements OnInit {
  private readonly api = inject(ExportApi);
  private readonly feedback = inject(Feedback);
  private readonly language = inject(LanguageService);
  private readonly municipalitiesApi = inject(MunicipalitiesApi);
  private readonly fb = inject(FormBuilder).nonNullable;

  protected readonly minDay = dayDate(MIN_EVENT_DAY);
  protected readonly busy = signal(false);
  protected readonly municipalities = new ListResource(() => this.municipalitiesApi.list(), this.feedback);
  protected readonly form = this.fb.group(
    {
      municipalityId: this.fb.control<number | null>(null),
      from: this.fb.control<Date | null>(null),
      to: this.fb.control<Date | null>(null),
    },
    { validators: periodValidator },
  );

  protected readonly minServiceYear = MIN_SERVICE_YEAR;
  protected readonly maxServiceYear = MAX_SERVICE_YEAR;
  protected readonly serviceYear = this.fb.control<number | null>(currentServiceYear(), [
    Validators.required,
    Validators.min(MIN_SERVICE_YEAR),
    Validators.max(MAX_SERVICE_YEAR),
    Validators.pattern(/^\d{4}$/),
  ]);

  ngOnInit(): void {
    void this.municipalities.reload();
  }

  protected async downloadS13(): Promise<void> {
    const year = this.serviceYear.value;
    if (this.serviceYear.invalid || year === null || this.busy()) {
      return;
    }
    await this.run(() => this.api.downloadS13(year, this.language.active()));
  }

  protected async download(format: ExportFormat): Promise<void> {
    if (this.form.invalid || this.busy()) {
      return;
    }
    const { municipalityId, from, to } = this.form.getRawValue();
    await this.run(() => this.api.download({
      format, language: this.language.active(), municipalityId, from: optionalDay(from), to: optionalDay(to),
    }));
  }

  private async run(action: () => Promise<void>): Promise<void> {
    this.busy.set(true);
    try {
      await action();
    } catch (error) {
      this.feedback.notifyError(error);
    } finally {
      this.busy.set(false);
    }
  }
}
