import { TestBed } from '@angular/core/testing';
import { Component, signal } from '@angular/core';
import { provideTranslateService } from '@ngx-translate/core';
import { ColumnFilter, FilterOption } from './column-filter';

@Component({
  imports: [ColumnFilter],
  template: `<app-column-filter label="Stato" [value]="value()" [options]="options()" (valueChange)="changed($event)" />`,
})
class Host {
  readonly value = signal('');
  readonly options = signal<readonly FilterOption[] | undefined>(undefined);
  readonly received: string[] = [];
  changed(value: string): void {
    this.received.push(value);
  }
}

function setup(options?: readonly FilterOption[], value = '') {
  TestBed.configureTestingModule({ imports: [Host], providers: [provideTranslateService()] });
  const fixture = TestBed.createComponent(Host);
  fixture.componentInstance.options.set(options);
  fixture.componentInstance.value.set(value);
  fixture.detectChanges();
  return { host: fixture.componentInstance, element: fixture.nativeElement as HTMLElement };
}

describe('ColumnFilter', () => {
  it('renders a search input and emits typed text', () => {
    const { host, element } = setup();
    const input = element.querySelector('input') as HTMLInputElement;
    expect(input.placeholder).toBe('Stato');

    input.value = 'abc';
    input.dispatchEvent(new Event('input'));

    expect(host.received).toEqual(['abc']);
  });

  it('renders a select with translated or raw labels and emits the choice', () => {
    const { host, element } = setup([{ value: 'x', labelKey: 'key.x' }, { value: 'y' }], 'y');
    const select = element.querySelector('select') as HTMLSelectElement;
    const labels = Array.from(select.options).map(o => o.textContent?.trim());
    expect(labels).toEqual(['common.all', 'key.x', 'y']);
    expect(select.options[2].selected).toBe(true);

    select.value = 'x';
    select.dispatchEvent(new Event('change'));

    expect(host.received).toEqual(['x']);
  });
});
