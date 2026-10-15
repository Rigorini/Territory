import { effect, EnvironmentProviders, inject, Injectable, makeEnvironmentProviders } from '@angular/core';
import { DateAdapter, MAT_DATE_FORMATS, MAT_NATIVE_DATE_FORMATS, MatDateFormats, NativeDateAdapter } from '@angular/material/core';
import { Language, LanguageService } from '../core/language.service';
import { formatDay, parseDay } from './format';

/** Marker of the typed/displayed day format: always dd/MM/yyyy, whatever the browser or OS locale. */
export const DAY_INPUT_FORMAT = 'dd/MM/yyyy';

/** Locales used only for calendar labels (month and weekday names); both start the week on Monday. */
const CALENDAR_LOCALES: Record<Language, string> = { it: 'it-IT', en: 'en-GB' };

export const DAY_DATE_FORMATS: MatDateFormats = {
  parse: { ...MAT_NATIVE_DATE_FORMATS.parse, dateInput: DAY_INPUT_FORMAT },
  display: { ...MAT_NATIVE_DATE_FORMATS.display, dateInput: DAY_INPUT_FORMAT },
};

/**
 * Native date adapter with a culture-independent text format: the base adapter parses with Date.parse,
 * which reads 03/04/2025 as March 4th in some browsers and April 3rd in others.
 */
@Injectable()
export class DayDateAdapter extends NativeDateAdapter {
  constructor() {
    super();
    const language = inject(LanguageService);
    effect(() => this.setLocale(CALENDAR_LOCALES[language.active()]));
  }

  override parse(value: unknown): Date | null {
    if (typeof value !== 'string' || value.trim() === '') {
      return null;
    }
    return parseDay(value) ?? this.invalid();
  }

  override format(date: Date, displayFormat: object | string): string {
    if (displayFormat === DAY_INPUT_FORMAT && this.isValid(date)) {
      return formatDay(date);
    }
    return super.format(date, displayFormat as object);
  }

  override getFirstDayOfWeek(): number {
    return 1;
  }
}

export function provideDayDateAdapter(): EnvironmentProviders {
  return makeEnvironmentProviders([
    { provide: DateAdapter, useClass: DayDateAdapter },
    { provide: MAT_DATE_FORMATS, useValue: DAY_DATE_FORMATS },
  ]);
}
