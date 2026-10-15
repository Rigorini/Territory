import { Component, computed, inject, signal } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { AbstractControl, NonNullableFormBuilder, ReactiveFormsModule, ValidationErrors, Validators } from '@angular/forms';
import { Router } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { TranslatePipe } from '@ngx-translate/core';
import { DISPLAY_NAME_MAX_LENGTH, USERNAME_PATTERN } from '../admins/admin-dialog';
import { ApiError, errorKey, toApiError } from '../core/api-error';
import { AuthService } from '../core/auth.service';
import { DEFAULT_AUTHENTICATED_URL } from '../core/auth.guard';
import { PASSWORD_MAX_LENGTH, passwordHints } from '../core/password-rules';
import { SetupApi } from './setup.api';

/** Creates the first administrator and signs in with it. */
@Component({
  selector: 'app-setup',
  imports: [
    ReactiveFormsModule, MatCardModule, MatFormFieldModule, MatInputModule,
    MatButtonModule, MatIconModule, MatProgressBarModule, TranslatePipe,
  ],
  templateUrl: './setup-page.html',
  styleUrl: './setup-page.scss',
})
export class SetupPage {
  private readonly api = inject(SetupApi);
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);

  protected readonly maxPasswordLength = PASSWORD_MAX_LENGTH;
  protected readonly maxDisplayNameLength = DISPLAY_NAME_MAX_LENGTH;
  protected readonly busy = signal(false);
  protected readonly errorKey = signal<string | null>(null);
  protected readonly form = inject(NonNullableFormBuilder).group({
    displayName: ['', [Validators.required, Validators.maxLength(DISPLAY_NAME_MAX_LENGTH), Validators.pattern(/\S/)]],
    username: ['', [Validators.required, Validators.pattern(USERNAME_PATTERN)]],
    password: ['', Validators.required],
    confirmPassword: ['', (control: AbstractControl) => this.matchesPassword(control)],
  });

  private readonly password = toSignal(this.form.controls.password.valueChanges, { initialValue: '' });
  protected readonly hints = computed(() => passwordHints(this.password()));

  constructor() {
    this.form.controls.password.valueChanges.subscribe(() =>
      this.form.controls.confirmPassword.updateValueAndValidity({ emitEvent: false }));
  }

  protected async submit(): Promise<void> {
    const passwordOk = this.hints().every(h => h.satisfied);
    if (this.form.invalid || !passwordOk || this.busy()) {
      this.form.markAllAsTouched();
      return;
    }
    this.busy.set(true);
    this.errorKey.set(null);
    try {
      const { displayName, username, password } = this.form.getRawValue();
      await this.api.createFirstAdmin({ displayName: displayName.trim(), username: username.trim(), password });
      await this.auth.login(username.trim(), password);
      await this.router.navigateByUrl(DEFAULT_AUTHENTICATED_URL);
    } catch (error) {
      await this.report(toApiError(error));
    } finally {
      this.busy.set(false);
    }
  }

  private async report(error: ApiError): Promise<void> {
    if (error.code === 'setup.alreadyDone') {
      await this.router.navigateByUrl('/login');
      return;
    }
    const fieldCode = Object.values(error.fields)[0]?.[0];
    this.errorKey.set(fieldCode ? `errors.fields.${fieldCode}` : errorKey(error.code));
  }

  private matchesPassword(control: AbstractControl): ValidationErrors | null {
    return control.value === (this.form?.controls.password.value ?? '') ? null : { mismatch: true };
  }
}
