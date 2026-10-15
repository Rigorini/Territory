import { Directive, inject, signal } from '@angular/core';
import { AbstractControl, FormGroup } from '@angular/forms';
import { MatDialogRef } from '@angular/material/dialog';
import { ApiError, errorKey, FieldErrors, toApiError } from '../core/api-error';

const SERVER_ERROR = 'server';

/** Conflict code shown on a form field: [control name, field error code]. */
export type ConflictField = readonly [string, string];

/**
 * Base of create/edit dialogs: submits the form, closes with the saved entity and
 * shows server validation codes on the matching fields (or as a dialog error).
 */
@Directive()
export abstract class FormDialog<TResult> {
  private readonly dialogRef = inject<MatDialogRef<unknown, TResult>>(MatDialogRef);
  protected readonly busy = signal(false);
  protected readonly errorKey = signal<string | null>(null);
  protected abstract readonly form: FormGroup;
  protected readonly conflictFields: Readonly<Record<string, ConflictField>> = {};

  protected async save(): Promise<void> {
    if (this.busy() || !this.canSubmit()) {
      this.form.markAllAsTouched();
      return;
    }
    if (!(await this.confirmSave())) {
      return;
    }
    this.busy.set(true);
    this.errorKey.set(null);
    try {
      this.dialogRef.close(await this.send());
    } catch (error) {
      this.applyServerError(toApiError(error));
    } finally {
      this.busy.set(false);
    }
  }

  protected serverErrors(control: string): string[] {
    const codes: unknown = this.form.get(control)?.errors?.[SERVER_ERROR];
    return Array.isArray(codes) ? (codes as string[]) : [];
  }

  protected canSubmit(): boolean {
    return this.form.valid;
  }

  /** Optional confirmation step before sending. */
  protected confirmSave(): Promise<boolean> {
    return Promise.resolve(true);
  }

  protected abstract send(): Promise<TResult>;

  private applyServerError(error: ApiError): void {
    const conflict = this.conflictFields[error.code];
    if (conflict) {
      setServerErrors(this.form.get(conflict[0]), [conflict[1]]);
      return;
    }
    if (!this.applyFieldErrors(error.fields)) {
      this.errorKey.set(errorKey(error.code));
    }
  }

  private applyFieldErrors(fields: FieldErrors): boolean {
    let applied = false;
    for (const [field, codes] of Object.entries(fields)) {
      const control = this.form.get(field);
      if (control) {
        setServerErrors(control, codes);
        applied = true;
      }
    }
    return applied;
  }
}

function setServerErrors(control: AbstractControl | null, codes: string[]): void {
  control?.setErrors({ [SERVER_ERROR]: codes });
  control?.markAsTouched();
}
