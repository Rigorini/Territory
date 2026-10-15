import { AbstractControl, ValidationErrors } from '@angular/forms';

// Mirrors the server allowlist (MyMapsLink.cs): the server remains the authority.
export const MYMAPS_URL_MAX_LENGTH = 2048;
const ALLOWED_HOSTS = new Set(['www.google.com', 'google.com', 'www.google.it', 'google.it']);
const MAP_ID_PATTERN = /^[A-Za-z0-9_-]{10,100}$/;

export function extractMapId(url: string): string | null {
  const value = url.trim();
  if (value === '' || value.length > MYMAPS_URL_MAX_LENGTH || !URL.canParse(value)) {
    return null;
  }
  const parsed = new URL(value);
  if (!isMyMapsUrl(parsed)) {
    return null;
  }
  const mapId = parsed.searchParams.get('mid');
  return mapId !== null && MAP_ID_PATTERN.test(mapId) ? mapId : null;
}

function isMyMapsUrl(url: URL): boolean {
  const noCredentials = url.username === '' && url.password === '';
  return url.protocol === 'https:' && url.port === '' && noCredentials
    && ALLOWED_HOSTS.has(url.hostname) && url.pathname.startsWith('/maps/d/');
}

/** Empty values are left to Validators.required. */
export function myMapsUrlValidator(control: AbstractControl<string>): ValidationErrors | null {
  const value = control.value ?? '';
  return value.trim() === '' || extractMapId(value) !== null ? null : { invalidFormat: true };
}
