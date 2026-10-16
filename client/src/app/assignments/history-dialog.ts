import { Component, computed, inject, OnInit, signal } from '@angular/core';
import { MAT_DIALOG_DATA, MatDialogConfig, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { MatTooltipModule } from '@angular/material/tooltip';
import { Router } from '@angular/router';
import { TranslatePipe } from '@ngx-translate/core';
import { AssignmentsApi, TerritoryStatusDto } from './assignments.api';
import { HistoryList } from './history-list';
import { Feedback } from '../shared/feedback';
import { formatOptionalDate } from '../shared/format';

/** Assignments shown in the dialog; the rest are on the history page. */
const PREVIEW = 5;

export interface HistoryDialogData {
  territory: TerritoryStatusDto;
}

/** Wide enough for the history table to keep every cell on one line; the default cap is 560px. */
const HISTORY_DIALOG_WIDTH = '760px';

/** The table cannot widen the dialog on its own: its cells would wrap instead. */
export const HISTORY_DIALOG_CONFIG: MatDialogConfig<HistoryDialogData> = {
  width: HISTORY_DIALOG_WIDTH,
  maxWidth: `min(${HISTORY_DIALOG_WIDTH}, calc(100vw - 32px))`,
};

/** Latest assignments of a territory, with the date corrections and a link to the full history. */
@Component({
  selector: 'app-history-dialog',
  imports: [MatDialogModule, MatButtonModule, MatIconModule, MatProgressBarModule, MatTooltipModule, TranslatePipe],
  templateUrl: './history-dialog.html',
  styleUrl: './assignment-dialogs.scss',
})
export class HistoryDialog implements OnInit {
  private readonly dialogRef = inject(MatDialogRef);
  private readonly router = inject(Router);
  protected readonly data = inject<HistoryDialogData>(MAT_DIALOG_DATA);
  private readonly feedback = inject(Feedback);
  protected readonly history = new HistoryList(
    () => this.data.territory.territoryId, () => this.data.territory, inject(AssignmentsApi), this.feedback);
  protected readonly recent = computed(() => this.history.items().slice(0, PREVIEW));
  protected readonly loaded = signal(false);

  async ngOnInit(): Promise<void> {
    await this.history.reload();
    this.loaded.set(true);
  }

  /** The full history is a page: it lists everything, paginated. */
  protected seeAll(): void {
    void this.router.navigate(['/admin/assignments', this.data.territory.territoryId, 'history']);
    this.dialogRef.close();
  }

  protected formatDate(value: string | null): string {
    return formatOptionalDate(value);
  }
}
