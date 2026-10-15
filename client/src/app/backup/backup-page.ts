import { Component, inject, signal } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { TranslatePipe } from '@ngx-translate/core';
import { toApiError } from '../core/api-error';
import { AuthService } from '../core/auth.service';
import { ADMINISTRATOR_ONLY } from '../core/roles';
import { Feedback } from '../shared/feedback';
import { BackupApi } from './backup.api';

/** Backup of the registry: clear-text export and destructive import of the same file. */
@Component({
  selector: 'app-backup-page',
  imports: [MatButtonModule, MatIconModule, MatProgressBarModule, TranslatePipe],
  templateUrl: './backup-page.html',
  styleUrl: './backup-page.scss',
})
export class BackupPage {
  private readonly api = inject(BackupApi);
  private readonly feedback = inject(Feedback);
  protected readonly auth = inject(AuthService);
  protected readonly administratorOnly = ADMINISTRATOR_ONLY;

  protected readonly busy = signal(false);
  /** Translation key of the last import rejection, shown inline because it describes the file. */
  protected readonly importErrorKey = signal<string | null>(null);

  protected async export(): Promise<void> {
    await this.run(() => this.api.download());
  }

  protected async import(input: HTMLInputElement): Promise<void> {
    const file = input.files?.item(0);
    input.value = '';
    if (!file || this.busy()) {
      return;
    }
    this.importErrorKey.set(null);
    const confirmed = await this.feedback.confirm({
      titleKey: 'backup.confirmTitle',
      messageKey: 'backup.confirmMessage',
      confirmKey: 'backup.confirmAction',
      params: { file: file.name },
    });
    if (!confirmed) {
      return;
    }
    await this.run(async () => {
      const summary = await this.api.import(file);
      this.feedback.notify('backup.imported', { ...summary });
    });
  }

  private async run(action: () => Promise<void>): Promise<void> {
    this.busy.set(true);
    try {
      await action();
    } catch (error) {
      this.report(error);
    } finally {
      this.busy.set(false);
    }
  }

  private report(error: unknown): void {
    const fileCode = toApiError(error).fields['file']?.[0];
    if (fileCode) {
      this.importErrorKey.set(`errors.fields.${fileCode}`);
      return;
    }
    this.feedback.notifyError(error);
  }
}
