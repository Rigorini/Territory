import { HttpErrorResponse } from '@angular/common/http';
import { Component, computed, inject, input, OnInit, signal } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { TranslatePipe } from '@ngx-translate/core';
import { LanguageService } from '../core/language.service';
import { Feedback } from '../shared/feedback';
import { formatDate } from '../shared/format';
import { heldDuration, pluralKey } from './held-duration';
import { TerritoryMap } from './map/territory-map';
import { GeoFeatureCollection, PublicApi, PublicTerritoryDto, ReturnReceiptDto } from './public.api';

type PageState = 'loading' | 'ready' | 'invalid' | 'error' | 'returned';
type MapState = 'loading' | 'ready' | 'unavailable';

/** Public page opened from the shared link: territory map, GPS and return. */
@Component({
  selector: 'app-territory-view',
  imports: [
    MatButtonModule,
    MatCardModule,
    MatIconModule,
    MatProgressBarModule,
    MatProgressSpinnerModule,
    TranslatePipe,
    TerritoryMap,
  ],
  templateUrl: './territory-view.html',
  styleUrl: './territory-view.scss',
})
export class TerritoryView implements OnInit {
  readonly token = input.required<string>();

  private readonly api = inject(PublicApi);
  private readonly feedback = inject(Feedback);
  private readonly language = inject(LanguageService);

  protected readonly state = signal<PageState>('loading');
  protected readonly mapState = signal<MapState>('loading');
  protected readonly territory = signal<PublicTerritoryDto | null>(null);
  protected readonly map = signal<GeoFeatureCollection | null>(null);
  protected readonly receipt = signal<ReturnReceiptDto | null>(null);
  protected readonly returning = signal(false);

  protected readonly held = computed(() => {
    const receipt = this.receipt();
    return receipt ? heldDuration(new Date(receipt.assignedAt), new Date(receipt.returnedAt)) : null;
  });

  async ngOnInit(): Promise<void> {
    await this.load();
  }

  protected async load(): Promise<void> {
    this.state.set('loading');
    try {
      this.territory.set(await this.api.territory(this.token()));
      this.state.set('ready');
    } catch (error) {
      this.fail(error);
      return;
    }
    await this.loadMap();
  }

  protected async loadMap(): Promise<void> {
    this.mapState.set('loading');
    try {
      this.map.set(await this.api.map(this.token()));
      this.mapState.set('ready');
    } catch {
      this.mapState.set('unavailable');
    }
  }

  protected async returnTerritory(): Promise<void> {
    const territory = this.territory();
    if (!territory || this.returning()) {
      return;
    }
    const confirmed = await this.feedback.confirm({
      titleKey: 'public.returnTitle',
      messageKey: 'public.returnMessage',
      params: { number: territory.number, name: territory.name },
      confirmKey: 'public.returnConfirm',
    });
    if (!confirmed) {
      return;
    }
    this.returning.set(true);
    try {
      this.receipt.set(await this.api.returnTerritory(this.token()));
      this.state.set('returned');
    } catch (error) {
      this.handleReturnError(error);
    } finally {
      this.returning.set(false);
    }
  }

  protected date(value: string): string {
    return formatDate(value);
  }

  protected unitKey(base: string, count: number): string {
    return pluralKey(base, count, this.language.active());
  }

  private fail(error: unknown): void {
    this.state.set(isInvalidLink(error) ? 'invalid' : 'error');
  }

  private handleReturnError(error: unknown): void {
    if (isInvalidLink(error)) {
      this.state.set('invalid');
      return;
    }
    this.feedback.notifyError(error);
  }
}

/** Unknown, replaced and already returned links all answer 404. */
function isInvalidLink(error: unknown): boolean {
  return error instanceof HttpErrorResponse && error.status === 404;
}
