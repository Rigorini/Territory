import { Component, input, output } from '@angular/core';
import { TranslatePipe } from '@ngx-translate/core';

export interface FilterOption {
  value: string;
  /** Translation key of the label; when missing the value is shown as is. */
  labelKey?: string;
}

/** Filter cell of a table column: a select when options are given, otherwise a text search. */
@Component({
  selector: 'app-column-filter',
  imports: [TranslatePipe],
  template: `
    @if (options(); as items) {
      <select class="filter" [value]="value()" (change)="emit($event)"
              [attr.aria-label]="'common.filterBy' | translate: { column: label() }">
        <option value="">{{ 'common.all' | translate }}</option>
        @for (item of items; track item.value) {
          <option [value]="item.value" [selected]="item.value === value()">
            {{ item.labelKey ? (item.labelKey | translate) : item.value }}
          </option>
        }
      </select>
    } @else {
      <input class="filter" type="search" [value]="value()" (input)="emit($event)" [placeholder]="label()"
             [attr.aria-label]="'common.filterBy' | translate: { column: label() }" />
    }
  `,
})
export class ColumnFilter {
  readonly label = input.required<string>();
  readonly value = input('');
  readonly options = input<readonly FilterOption[]>();
  readonly valueChange = output<string>();

  protected emit(event: Event): void {
    this.valueChange.emit((event.target as HTMLInputElement | HTMLSelectElement).value);
  }
}
