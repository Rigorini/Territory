import { afterNextRender, Component, DestroyRef, ElementRef, inject, input, signal, viewChild } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatTooltipModule } from '@angular/material/tooltip';
import { TranslatePipe } from '@ngx-translate/core';
import { GeoFeatureCollection } from '../public.api';
import { LocateState, MAP_CONTROLLER_FACTORY, MapController } from './map-controller';

const LOCATE_ICONS: Record<LocateState, string> = {
  idle: 'my_location',
  searching: 'location_searching',
  active: 'my_location',
  denied: 'location_disabled',
  unsupported: 'location_disabled',
};

/** Interactive territory map: pan/zoom, fit to territory and GPS position. */
@Component({
  selector: 'app-territory-map',
  imports: [MatButtonModule, MatIconModule, MatTooltipModule, TranslatePipe],
  templateUrl: './territory-map.html',
  styleUrl: './territory-map.scss',
})
export class TerritoryMap {
  readonly map = input.required<GeoFeatureCollection>();

  private readonly host = viewChild.required<ElementRef<HTMLElement>>('host');
  private readonly createController = inject(MAP_CONTROLLER_FACTORY);
  private controller?: MapController;

  protected readonly locateState = signal<LocateState>('idle');
  protected readonly icons = LOCATE_ICONS;

  constructor() {
    afterNextRender(() => {
      this.controller = this.createController(this.host().nativeElement, this.map(), state => this.locateState.set(state));
    });
    inject(DestroyRef).onDestroy(() => this.controller?.dispose());
  }

  protected resetView(): void {
    this.controller?.resetView();
  }

  protected locate(): void {
    this.controller?.locate();
  }
}
