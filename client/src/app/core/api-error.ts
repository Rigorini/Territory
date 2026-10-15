import { HttpErrorResponse } from '@angular/common/http';

export type FieldErrors = Record<string, string[]>;

/** Error returned by the API as ProblemDetails with a stable, translatable code. */
export interface ApiError {
  code: string;
  fields: FieldErrors;
}

export function toApiError(error: unknown): ApiError {
  if (!(error instanceof HttpErrorResponse)) {
    return { code: 'generic', fields: {} };
  }
  if (error.status === 0) {
    return { code: 'network', fields: {} };
  }
  const body: unknown = error.error;
  return { code: readCode(body, error.status), fields: readFields(body) };
}

/** Translation key of an error code, e.g. "admins.notFound" → "errors.codes.admins.notFound". */
export function errorKey(code: string): string {
  return `errors.codes.${code}`;
}

function readCode(body: unknown, status: number): string {
  if (isRecord(body) && typeof body['code'] === 'string') {
    return body['code'];
  }
  return status === 429 ? 'tooManyRequests' : 'generic';
}

function readFields(body: unknown): FieldErrors {
  if (!isRecord(body) || !isRecord(body['errors'])) {
    return {};
  }
  const fields: FieldErrors = {};
  for (const [field, codes] of Object.entries(body['errors'])) {
    if (Array.isArray(codes)) {
      fields[field] = codes.filter((c): c is string => typeof c === 'string');
    }
  }
  return fields;
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value);
}
