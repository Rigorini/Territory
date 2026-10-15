import { Component, computed, inject } from '@angular/core';
import { takeUntilDestroyed, toSignal } from '@angular/core/rxjs-interop';
import { AbstractControl, NonNullableFormBuilder, ReactiveFormsModule, ValidationErrors, Validators } from '@angular/forms';
import { MAT_DIALOG_DATA, MatDialogModule } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatSelectModule } from '@angular/material/select';
import { AuthService } from '../core/auth.service';
import { ADMIN_ROLES, AdminRole, AdminRoleValue } from '../core/roles';
import { TranslatePipe } from '@ngx-translate/core';
import { AdminDto, AdminsApi } from './admins.api';
import { PASSWORD_MAX_LENGTH, passwordHints } from '../core/password-rules';
import { FormDialog } from '../shared/form-dialog';

export interface AdminDialogData {
  admin?: AdminDto;
}

export const USERNAME_PATTERN = /^[A-Za-z0-9._-]{3,50}$/;
export const DISPLAY_NAME_MAX_LENGTH = 100;

/** Create/edit dialog; closes with the saved admin, or undefined when cancelled. */
@Component({
  selector: 'app-admin-dialog',
  imports: [
    ReactiveFormsModule, MatDialogModule, MatFormFieldModule, MatInputModule,
    MatButtonModule, MatIconModule, MatSelectModule, TranslatePipe,
  ],
  templateUrl: './admin-dialog.html',
  styleUrl: '../shared/form-dialog.scss',
})
export class AdminDialog extends FormDialog<AdminDto> {
  private readonly api = inject(AdminsApi);
  protected readonly data = inject<AdminDialogData>(MAT_DIALOG_DATA);

  protected readonly isEdit = this.data.admin !== undefined;
  protected readonly roles = ADMIN_ROLES;
  /** The API rejects changing your own role, so the selector is read-only for the logged-in user. */
  protected readonly isSelf = this.data.admin?.id !== undefined && this.data.admin.id === inject(AuthService).user()?.id;
  protected readonly maxPasswordLength = PASSWORD_MAX_LENGTH;
  protected readonly maxDisplayNameLength = DISPLAY_NAME_MAX_LENGTH;
  protected readonly form = inject(NonNullableFormBuilder).group({
    displayName: [
      this.data.admin?.displayName ?? '',
      [Validators.required, Validators.maxLength(DISPLAY_NAME_MAX_LENGTH), Validators.pattern(/\S/)],
    ],
    username: [this.data.admin?.username ?? '', [Validators.required, Validators.pattern(USERNAME_PATTERN)]],
    password: ['', this.isEdit ? [] : [Validators.required]],
    confirmPassword: ['', (control: AbstractControl) => this.matchesPassword(control)],
    role: [this.data.admin?.role ?? AdminRole.Administrator as AdminRoleValue],
  });
  protected override readonly conflictFields = { 'admins.usernameTaken': ['username', 'usernameTaken'] as const };

  private readonly password = toSignal(this.form.controls.password.valueChanges, { initialValue: '' });
  protected readonly hints = computed(() => passwordHints(this.password()));
  protected readonly showHints = computed(() => !this.isEdit || this.password() !== '');

  constructor() {
    super();
    if (this.isSelf) {
      this.form.controls.role.disable();
    }
    this.form.controls.password.valueChanges
      .pipe(takeUntilDestroyed())
      .subscribe(() => {
        this.form.controls.confirmPassword.updateValueAndValidity({ emitEvent: false });
      });
  }

  private matchesPassword(control: AbstractControl): ValidationErrors | null {
    const password = this.form?.controls.password.value ?? '';
    return control.value === password ? null : { mismatch: true };
  }

  protected override canSubmit(): boolean {
    return this.isPasswordAcceptable() && this.form.valid;
  }

  protected send(): Promise<AdminDto> {
    const { displayName, username, password, role } = this.form.getRawValue();
    const request = {
      username: username.trim(), displayName: displayName.trim(), password: password === '' ? null : password, role,
    };
    if (!this.data.admin) {
      return this.api.create(request);
    }
    return this.api.update(this.data.admin.id, request);
  }

  private isPasswordAcceptable(): boolean {
    const password = this.password();
    return (this.isEdit && password === '') || this.hints().every(h => h.satisfied);
  }
}
