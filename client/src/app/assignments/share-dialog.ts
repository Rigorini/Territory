import { Component, DOCUMENT, inject } from '@angular/core';
import { Clipboard } from '@angular/cdk/clipboard';
import { MAT_DIALOG_DATA, MatDialogModule } from '@angular/material/dialog';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { TerritoryStatusDto } from './assignments.api';
import { shareUrl, telegramUrl, whatsAppUrl } from './share-links';
import { Feedback } from '../shared/feedback';

export interface ShareDialogData {
  territory: TerritoryStatusDto;
  token: string;
}

/** Sends the secret link of an assignment: system share sheet, WhatsApp, Telegram or clipboard. */
@Component({
  selector: 'app-share-dialog',
  imports: [MatDialogModule, MatButtonModule, MatIconModule, TranslatePipe],
  templateUrl: './share-dialog.html',
  styleUrl: './assignment-dialogs.scss',
})
export class ShareDialog {
  private readonly clipboard = inject(Clipboard);
  private readonly feedback = inject(Feedback);
  private readonly navigator = inject(DOCUMENT).defaultView?.navigator;
  protected readonly data = inject<ShareDialogData>(MAT_DIALOG_DATA);

  protected readonly url = shareUrl(inject(DOCUMENT).location.origin, this.data.token);
  protected readonly text: string = inject(TranslateService).instant('assignments.shareText', {
    number: this.data.territory.number,
    name: this.data.territory.name,
    municipality: this.data.territory.municipalityName,
  });
  protected readonly whatsApp = whatsAppUrl(this.text, this.url);
  protected readonly telegram = telegramUrl(this.text, this.url);
  protected readonly canShare = typeof this.navigator?.share === 'function';

  protected async share(): Promise<void> {
    try {
      await this.navigator!.share({ text: this.text, url: this.url });
    } catch (error) {
      // Closing the share sheet rejects with AbortError: not a failure.
      if (!(error instanceof DOMException && error.name === 'AbortError')) {
        this.feedback.notify('assignments.shareFailed');
      }
    }
  }

  protected copy(): void {
    this.feedback.notify(this.clipboard.copy(this.url) ? 'assignments.copied' : 'assignments.copyFailed');
  }
}
