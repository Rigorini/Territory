import { ComponentType } from '@angular/cdk/portal';
import { inject, Injectable } from '@angular/core';
import { MatDialog } from '@angular/material/dialog';
import { MatSnackBar } from '@angular/material/snack-bar';
import { TranslateService } from '@ngx-translate/core';
import { firstValueFrom } from 'rxjs';
import { errorKey, toApiError } from '../core/api-error';
import { ConfirmDialog, ConfirmDialogData } from './confirm-dialog';

/** User feedback shared by admin pages: snackbar messages, API errors and confirmations. */
@Injectable({ providedIn: 'root' })
export class Feedback {
  private readonly snackBar = inject(MatSnackBar);
  private readonly translate = inject(TranslateService);
  private readonly dialog = inject(MatDialog);

  notify(key: string, params?: Record<string, unknown>): void {
    this.snackBar.open(this.translate.instant(key, params), undefined, { duration: 4000 });
  }

  notifyError(error: unknown): void {
    this.notify(errorKey(toApiError(error).code));
  }

  async confirm(data: ConfirmDialogData): Promise<boolean> {
    return (await this.open<ConfirmDialog, ConfirmDialogData, boolean>(ConfirmDialog, data)) === true;
  }

  /** Opens a dialog and resolves with its result (undefined when dismissed). */
  open<TComponent, TData, TResult>(component: ComponentType<TComponent>, data: TData): Promise<TResult | undefined> {
    const ref = this.dialog.open<TComponent, TData, TResult>(component, { data });
    return firstValueFrom(ref.afterClosed());
  }

  /** Runs an API action, notifying success or the translated error; resolves true on success. */
  async attempt(action: () => Promise<unknown>, successKey: string): Promise<boolean> {
    try {
      await action();
      this.notify(successKey);
      return true;
    } catch (error) {
      this.notifyError(error);
      return false;
    }
  }
}
