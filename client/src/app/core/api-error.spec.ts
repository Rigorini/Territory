import { HttpErrorResponse } from '@angular/common/http';
import { errorKey, toApiError } from './api-error';

describe('toApiError', () => {
  it('reads code and field codes from ProblemDetails', () => {
    const error = new HttpErrorResponse({
      status: 400,
      error: { code: 'validation', errors: { username: ['required', 42], bad: 'x' } },
    });

    expect(toApiError(error)).toEqual({ code: 'validation', fields: { username: ['required'] } });
  });

  it('maps network failures', () => {
    expect(toApiError(new HttpErrorResponse({ status: 0 })).code).toBe('network');
  });

  it('maps 429 without a code to tooManyRequests', () => {
    expect(toApiError(new HttpErrorResponse({ status: 429, error: null })).code).toBe('tooManyRequests');
  });

  it('falls back to generic for unknown bodies and non-http errors', () => {
    expect(toApiError(new HttpErrorResponse({ status: 500, error: 'boom' })).code).toBe('generic');
    expect(toApiError(new Error('x'))).toEqual({ code: 'generic', fields: {} });
  });

  it('builds the translation key', () => {
    expect(errorKey('admins.notFound')).toBe('errors.codes.admins.notFound');
  });
});
