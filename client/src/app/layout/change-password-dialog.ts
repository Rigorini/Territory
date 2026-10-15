import { Component, computed, inject } from '@angular/core';
import { takeUntilDestroyed, toSignal } from '@angular/core/rxjs-interop';
import { AbstractControl, NonNullableFormBuilder, ReactiveFormsModule, ValidationErrors, Validators } from '@angular/forms';
import { MatDialogModule } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { TranslatePipe } from '@ngx-translate/core';
import { AuthService } from '../core/auth.service';
import { PASSWORD_MAX_LENGTH, passwordHints } from '../core/password-rules';
import { FormDialog } from '../shared/form-dialog';

/** Lets the logged-in user change their own password; closes with true when saved. */
@Component({
  selector: 'app-change-password-dialog',
  imports: [
    ReactiveFormsModule, MatDialogModule, MatFormFieldModule, MatInputModule, MatButtonModule, MatIconModule, TranslatePipe,
  ],
  templateUrl: './change-password-dialog.html',
  styleUrl: '../shared/form-dialog.scss',
})
export class ChangePasswordDialog extends FormDialog<boolean> {
  private readonly auth = inject(AuthService);
  protected readonly maxPasswordLength = PASSWORD_MAX_LENGTH;
  protected readonly form = inject(NonNullableFormBuilder).group({
    currentPassword: ['', Validators.required],
    password: ['', Validators.required],
    confirmPassword: ['', (control: AbstractControl) => this.matchesPassword(control)],
  });

  private readonly password = toSignal(this.form.controls.password.valueChanges, { initialValue: '' });
  protected readonly hints = computed(() => passwordHints(this.password()));

  constructor() {
    super();
    this.form.controls.password.valueChanges
      .pipe(takeUntilDestroyed())
      .subscribe(() => this.form.controls.confirmPassword.updateValueAndValidity({ emitEvent: false }));
  }

  private matchesPassword(control: AbstractControl): ValidationErrors | null {
    return control.value === (this.form?.controls.password.value ?? '') ? null : { mismatch: true };
  }

  protected override canSubmit(): boolean {
    return this.hints().every(h => h.satisfied) && this.form.valid;
  }

  protected async send(): Promise<boolean> {
    const { currentPassword, password } = this.form.getRawValue();
    await this.auth.changePassword(currentPassword, password);
    return true;
  }
}