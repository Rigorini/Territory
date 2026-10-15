import { Component, inject } from '@angular/core';
import { NonNullableFormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { MAT_DIALOG_DATA, MatDialogModule } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatButtonModule } from '@angular/material/button';
import { TranslatePipe } from '@ngx-translate/core';
import { PublisherDto, PublishersApi } from './publishers.api';
import { Feedback } from '../shared/feedback';
import { FormDialog } from '../shared/form-dialog';

export interface PublisherDialogData {
  publisher?: PublisherDto;
}

/** How the dialog ended: reactivated is true when a deleted publisher with the same name was restored. */
export interface PublisherSaved {
  reactivated: boolean;
}

export const PUBLISHER_NAME_MAX_LENGTH = 60;

@Component({
  selector: 'app-publisher-dialog',
  imports: [ReactiveFormsModule, MatDialogModule, MatFormFieldModule, MatInputModule, MatButtonModule, TranslatePipe],
  templateUrl: './publisher-dialog.html',
  styleUrl: '../shared/form-dialog.scss',
})
export class PublisherDialog extends FormDialog<PublisherSaved> {
  private readonly api = inject(PublishersApi);
  private readonly feedback = inject(Feedback);
  protected readonly data = inject<PublisherDialogData>(MAT_DIALOG_DATA);

  protected readonly maxLength = PUBLISHER_NAME_MAX_LENGTH;
  private readonly nameValidators = [Validators.required, Validators.maxLength(this.maxLength)];
  protected readonly form = inject(NonNullableFormBuilder).group({
    firstName: [this.data.publisher?.firstName ?? '', this.nameValidators],
    lastName: [this.data.publisher?.lastName ?? '', this.nameValidators],
  });

  protected override confirmSave(): Promise<boolean> {
    return this.feedback.confirm({
      titleKey: 'publishers.confirmSaveTitle',
      messageKey: 'publishers.confirmSaveMessage',
      params: this.request(),
      confirmKey: 'common.save',
    });
  }

  protected async send(): Promise<PublisherSaved> {
    const publisher = this.data.publisher;
    if (publisher) {
      await this.api.update(publisher.id, this.request());
      return { reactivated: false };
    }
    const { reactivated } = await this.api.create(this.request());
    return { reactivated };
  }

  private request() {
    const { firstName, lastName } = this.form.getRawValue();
    return { firstName: firstName.trim(), lastName: lastName.trim() };
  }
}
