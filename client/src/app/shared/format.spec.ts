import { TestBed } from '@angular/core/testing';
import { DateAdapter, MAT_DATE_FORMATS, MatDateFormats } from '@angular/material/core';
import { provideTranslateService } from '@ngx-translate/core';
import { LanguageService } from '../core/language.service';
import { DAY_INPUT_FORMAT, provideDayDateAdapter } from './day-date-adapter';
import { formatDate, formatDay, parseDay } from './format';

describe('day formatting', () => {
  it('formats dd/MM/yyyy whatever the language', () => {
    expect(formatDay(new Date(2025, 2, 1))).toBe('01/03/2025');
    expect(formatDay(new Date(2025, 11, 31, 23, 59))).toBe('31/12/2025');
    const instant = new Date(2025, 2, 1, 10).toISOString();
    expect(formatDate(instant)).toBe('01/03/2025');
  });

  it.each([
    ['05/03/2025', new Date(2025, 2, 5)],
    ['5/3/2025', new Date(2025, 2, 5)],
    [' 05.03.2025 ', new Date(2025, 2, 5)],
    ['05-03-2025', new Date(2025, 2, 5)],
    ['29/02/2024', new Date(2024, 1, 29)],
  ])('parses %s as day/month/year', (text, expected) => {
    expect(parseDay(text)).toEqual(expected);
  });

  it.each(['31/02/2025', '29/02/2025', '00/01/2025', '01/13/2025', '2025-03-05', '03/05/25', '01/01/0099', 'abc', ''])(
    'rejects %s', text => {
      expect(parseDay(text)).toBeNull();
    });
});

describe('DayDateAdapter', () => {
  function setup() {
    TestBed.configureTestingModule({ providers: [provideTranslateService(), provideDayDateAdapter()] });
    return {
      adapter: TestBed.inject(DateAdapter<Date>),
      formats: TestBed.inject<MatDateFormats>(MAT_DATE_FORMATS),
      language: TestBed.inject(LanguageService),
    };
  }

  it('parses typed text as dd/MM/yyyy, flags invalid text and ignores empty input', () => {
    const { adapter, formats } = setup();
    const parse = (text: unknown) => adapter.parse(text, formats.parse.dateInput);

    expect(parse('03/04/2025')).toEqual(new Date(2025, 3, 3));
    expect(adapter.isValid(parse('31/04/2025') as Date)).toBe(false);
    expect(parse('  ')).toBeNull();
    expect(parse(null)).toBeNull();
  });

  it('displays the input as dd/MM/yyyy and other labels with the calendar locale', () => {
    const { adapter, formats, language } = setup();

    language.use('en');
    TestBed.tick();
    expect(adapter.format(new Date(2025, 3, 3), formats.display.dateInput)).toBe('03/04/2025');
    expect(adapter.format(new Date(2025, 3, 3), formats.display.monthYearA11yLabel)).toBe('April 2025');

    language.use('it');
    TestBed.tick();
    expect(adapter.format(new Date(2025, 3, 3), DAY_INPUT_FORMAT)).toBe('03/04/2025');
    expect(adapter.format(new Date(2025, 3, 3), formats.display.monthYearA11yLabel)).toBe('aprile 2025');
    expect(adapter.getFirstDayOfWeek()).toBe(1);
  });
});
