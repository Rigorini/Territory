import { FormControl } from '@angular/forms';
import { extractMapId, myMapsUrlValidator } from './mymaps-link';

const MID = '1AbC_def-GHIjkl';

describe('extractMapId', () => {
  it.each([
    `https://www.google.com/maps/d/edit?mid=${MID}&usp=sharing`,
    `https://google.it/maps/d/viewer?ll=1,2&mid=${MID}`,
    `  https://www.google.com:443/maps/d/u/0/viewer?mid=${MID}  `,
  ])('accepts %s', url => {
    expect(extractMapId(url)).toBe(MID);
  });

  it.each([
    '',
    'not a url',
    `http://www.google.com/maps/d/edit?mid=${MID}`,
    `https://www.google.com:8443/maps/d/edit?mid=${MID}`,
    `https://user:pw@www.google.com/maps/d/edit?mid=${MID}`,
    `https://evil.example/maps/d/edit?mid=${MID}`,
    `https://www.google.com/maps/place?mid=${MID}`,
    'https://www.google.com/maps/d/edit?mid=short',
    'https://www.google.com/maps/d/edit?usp=sharing',
    `https://www.google.com/maps/d/edit?mid=${MID}&x=${'a'.repeat(2048)}`,
  ])('rejects %s', url => {
    expect(extractMapId(url)).toBeNull();
  });
});

describe('myMapsUrlValidator', () => {
  it('leaves empty values to required and flags invalid links', () => {
    expect(myMapsUrlValidator(new FormControl('', { nonNullable: true }))).toBeNull();
    expect(myMapsUrlValidator(new FormControl(`https://www.google.com/maps/d/edit?mid=${MID}`, { nonNullable: true }))).toBeNull();
    expect(myMapsUrlValidator(new FormControl('https://example.com', { nonNullable: true }))).toEqual({ invalidFormat: true });
  });
});
