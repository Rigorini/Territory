import { filterRows, sortRows } from './table-filter';

interface Row {
  name: string;
  n: number;
}

const rows: Row[] = [
  { name: 'Bravo', n: 10 },
  { name: 'alfa', n: 2 },
  { name: 'Charlie', n: 1 },
];
const accessors = { name: (r: Row) => r.name, n: (r: Row) => String(r.n) };

describe('filterRows', () => {
  it('matches case-insensitively on every active column', () => {
    expect(filterRows(rows, { name: 'AL', n: '' }, accessors).map(r => r.name)).toEqual(['alfa']);
    expect(filterRows(rows, { name: 'a', n: '1' }, accessors).map(r => r.name)).toEqual(['Bravo', 'Charlie']);
  });

  it('ignores blank values and unknown columns', () => {
    expect(filterRows(rows, { name: '  ', other: 'x' }, accessors)).toHaveLength(3);
  });

  it('requires a whole-value match on exact columns', () => {
    expect(filterRows(rows, { n: '1' }, accessors, new Set(['n'])).map(r => r.name)).toEqual(['Charlie']);
    expect(filterRows(rows, { name: 'BRAVO' }, accessors, new Set(['name'])).map(r => r.name)).toEqual(['Bravo']);
  });
});

describe('sortRows', () => {
  const sortAccessors = { name: (r: Row) => r.name, n: (r: Row) => r.n };

  it('sorts strings ignoring case and numbers numerically', () => {
    expect(sortRows(rows, 'name', 'asc', sortAccessors).map(r => r.name)).toEqual(['alfa', 'Bravo', 'Charlie']);
    expect(sortRows(rows, 'n', 'desc', sortAccessors).map(r => r.n)).toEqual([10, 2, 1]);
  });

  it('keeps the original order without direction or for unknown columns', () => {
    expect(sortRows(rows, 'name', '', sortAccessors)).toEqual(rows);
    expect(sortRows(rows, 'missing', 'asc', sortAccessors)).toEqual(rows);
  });

  it('compares composite keys element by element', () => {
    const keyed = [{ k: ['b', 1] }, { k: ['a b', 2] }, { k: ['a', 10] }, { k: ['a', 2] }, { k: ['a'] }];
    const sorted = sortRows(keyed, 'k', 'asc', { k: r => r.k });
    expect(sorted.map(r => r.k)).toEqual([['a'], ['a', 2], ['a', 10], ['a b', 2], ['b', 1]]);
  });
});
