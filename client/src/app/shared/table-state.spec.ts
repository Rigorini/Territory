import { signal } from '@angular/core';
import { TableState } from './table-state';

interface Row {
  name: string;
  kind: string;
}

const source = signal<Row[]>([
  { name: 'beta', kind: 'a' },
  { name: 'Alfa', kind: 'ab' },
]);

function create() {
  return new TableState(source, {
    filters: signal({ name: (r: Row) => r.name, kind: (r: Row) => r.kind }),
    exactFilters: ['kind'],
    sort: { name: r => r.name },
    initialSort: { active: 'name', direction: 'asc' },
  });
}

describe('TableState', () => {
  it('sorts with the initial sort and on demand', () => {
    const table = create();
    expect(table.rows().map(r => r.name)).toEqual(['Alfa', 'beta']);

    table.onSort({ active: 'name', direction: 'desc' });
    expect(table.rows().map(r => r.name)).toEqual(['beta', 'Alfa']);
  });

  it('filters, reports active filters and clears them', () => {
    const table = create();
    expect(table.hasFilters()).toBe(false);

    table.setFilter('kind', 'a');
    expect(table.filter('kind')).toBe('a');
    expect(table.filter('name')).toBe('');
    expect(table.rows().map(r => r.name)).toEqual(['beta']);
    expect(table.hasFilters()).toBe(true);

    table.clearFilters();
    expect(table.rows()).toHaveLength(2);
  });

  it('works without exact columns', () => {
    const table = new TableState(source, {
      filters: signal({ kind: (r: Row) => r.kind }),
      sort: {},
      initialSort: { active: '', direction: '' },
    });
    table.setFilter('kind', 'a');
    expect(table.rows()).toHaveLength(2);
  });
});
