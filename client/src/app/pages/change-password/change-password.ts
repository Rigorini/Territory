import { Component, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed, toSignal } from '@angular/core/rxjs-interop';
import { AbstractControl, NonNullableFormBuilder, ReactiveFormsModule, ValidationErrors, Validators } from '@angular/forms';
import { Router } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { TranslatePipe } from '@ngx-translate/core';
import { ApiError, errorKey, toApiError } from '../../core/api-error';
import { AuthService } from '../../core/auth.service';
import { DEFAULT_AUTHENTICATED_URL } from '../../core/auth.guard';
import { PASSWORD_MAX_LENGTH, passwordHints } from '../../core/password-rules';

/** Page shown after the first login: the user must replace the password set by the administrator. */
@Component({
  selector: 'app-change-password-page',
  imports: [
    ReactiveFormsModule, MatCardModule, MatFormFieldModule, MatInputModule,
    MatButtonModule, MatIconModule, MatProgressBarModule, TranslatePipe,
  ],
  templateUrl: './change-password.html',
  styleUrl: './change-password.scss',
})
export class ChangePasswordPage {
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);

  protected readonly maxPasswordLength = PASSWORD_MAX_LENGTH;
  protected readonly busy = signal(false);
  protected readonly errorKey = signal<string | null>(null);
  protected readonly serverFields = signal<Record<string, string[]>>({});
  protected readonly form = inject(NonNullableFormBuilder).group({
    currentPassword: ['', Validators.required],
    password: ['', Validators.required],
    confirmPassword: ['', (control: AbstractControl) => this.matchesPassword(control)],
  });

  private readonly password = toSignal(this.form.controls.password.valueChanges, { initialValue: '' });
  protected readonly hints = computed(() => passwordHints(this.password()));

  constructor() {
    this.form.controls.password.valueChanges
      .pipe(takeUntilDestroyed())
      .subscribe(() => this.form.controls.confirmPassword.updateValueAndValidity({ emitEvent: false }));
  }

  protected async submit(): Promise<void> {
    if (this.busy()) {
      return;
    }
    if (!this.hints().every(h => h.satisfied) || this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }
    this.busy.set(true);
    this.errorKey.set(null);
    this.serverFields.set({});
    try {
      const { currentPassword, password } = this.form.getRawValue();
      await this.auth.changePassword(currentPassword, password);
      const profile = this.auth.user();
      if (profile) {
        this.auth.updateProfile({ ...profile, mustChangePassword: false });
      }
      await this.router.navigateByUrl(DEFAULT_AUTHENTICATED_URL);
    } catch (error) {
      this.showError(toApiError(error));
    } finally {
      this.busy.set(false);
    }
  }

  protected async logout(): Promise<void> {
    try {
      await this.auth.logout();
    } finally {
      await this.router.navigate(['/login']);
    }
  }

  private showError(error: ApiError): void {
    if (Object.keys(error.fields).length > 0) {
      this.serverFields.set(error.fields);
    } else {
      this.errorKey.set(errorKey(error.code));
    }
  }

  private matchesPassword(control: AbstractControl): ValidationErrors | null {
    return control.value === (this.form?.controls.password.value ?? '') ? null : { mismatch: true };
  }
}
