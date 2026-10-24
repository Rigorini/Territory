import { afterNextRender, Component, DestroyRef, ElementRef, inject, input, output, signal, viewChild } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatTooltipModule } from '@angular/material/tooltip';
import { TranslatePipe } from '@ngx-translate/core';
import { GeoFeatureCollection } from '../public.api';
import { BaseLayerKind } from './base-layers';
import { LocateState, MAP_CONTROLLER_FACTORY, MapController, MapNotice } from './map-controller';

const LOCATE_ICONS: Record<LocateState, string> = {
  idle: 'my_location',
  searching: 'location_searching',
  active: 'my_location',
  denied: 'location_disabled',
  unsupported: 'location_disabled',
};

/** Icon of what the button switches to, not of the current basemap. */
const BASEMAP_ICONS: Record<BaseLayerKind, string> = {
  street: 'satellite_alt',
  satellite: 'map',
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
  /** Emitted once the first frame (basemap tiles included) has been painted. */
  readonly rendered = output<void>();

  private readonly host = viewChild.required<ElementRef<HTMLElement>>('host');
  private readonly createController = inject(MAP_CONTROLLER_FACTORY);
  private controller?: MapController;

  protected readonly locateState = signal<LocateState>('idle');
  protected readonly baseLayer = signal<BaseLayerKind>('street');
  protected readonly satelliteUnavailable = signal(false);
  protected readonly icons = LOCATE_ICONS;
  protected readonly basemapIcons = BASEMAP_ICONS;

  constructor() {
    afterNextRender(() => {
      this.controller = this.createController(this.host().nativeElement, this.map(), notice => this.onNotice(notice));
      this.controller.setBaseLayer(this.baseLayer());
      void this.controller.whenRendered().then(() => this.rendered.emit());
    });
    inject(DestroyRef).onDestroy(() => this.controller?.dispose());
  }

  protected resetView(): void {
    this.controller?.resetView();
  }

  protected locate(): void {
    this.controller?.locate();
  }

  protected toggleBaseLayer(): void {
    const next: BaseLayerKind = this.baseLayer() === 'street' ? 'satellite' : 'street';
    this.baseLayer.set(next);
    this.satelliteUnavailable.set(false);
    this.controller?.setBaseLayer(next);
  }

  private onNotice(notice: MapNotice): void {
    switch (notice.kind) {
      case 'locate':
        this.locateState.set(notice.state);
        break;
      case 'satelliteUnavailable':
        this.baseLayer.set('street');
        this.satelliteUnavailable.set(true);
        break;
    }
  }
}
