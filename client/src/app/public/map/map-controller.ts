import { InjectionToken } from '@angular/core';
import { Feature, Geolocation, Map as OlMap, View } from 'ol';
import { defaults as defaultControls } from 'ol/control/defaults';
import { isEmpty } from 'ol/extent';
import GeoJSON from 'ol/format/GeoJSON';
import { Point } from 'ol/geom';
import TileLayer from 'ol/layer/Tile';
import VectorLayer from 'ol/layer/Vector';
import { fromLonLat } from 'ol/proj';
import OSM from 'ol/source/OSM';
import VectorSource from 'ol/source/Vector';
import { GeoFeatureCollection } from '../public.api';
import { loadTileWithReferrer, POSITION_STYLE, styleFeature } from './map-style';

export type LocateState = 'idle' | 'searching' | 'active' | 'denied' | 'unsupported';

/** Map operations used by the component; OpenLayers stays behind this interface. */
export interface MapController {
  resetView(): void;
  locate(): void;
  dispose(): void;
}

export type MapControllerFactory = (
  target: HTMLElement,
  map: GeoFeatureCollection,
  onLocate: (state: LocateState) => void,
) => MapController;

export const MAP_CONTROLLER_FACTORY = new InjectionToken<MapControllerFactory>('MapControllerFactory', {
  providedIn: 'root',
  factory: () => (target, map, onLocate) => new OlMapController(target, map, onLocate),
});

/** Giano dell'Umbria: initial centre before fitting the territory. */
const DEFAULT_CENTER = fromLonLat([12.578, 42.833]);
const FIT_PADDING = [48, 48, 48, 48];
const MAX_FIT_ZOOM = 18;
const LOCATE_ZOOM = 17;
const ANIMATION_MS = 400;

export class OlMapController implements MapController {
  private readonly territory: VectorSource;
  private readonly position = new Feature();
  private readonly accuracy = new Feature();
  private readonly view = new View({ center: DEFAULT_CENTER, zoom: 13, maxZoom: 19 });
  private readonly map: OlMap;
  private readonly geolocation: Geolocation;
  private centerOnNextFix = false;

  constructor(
    target: HTMLElement,
    collection: GeoFeatureCollection,
    private readonly onLocate: (state: LocateState) => void,
  ) {
    const features = new GeoJSON({ featureProjection: 'EPSG:3857' }).readFeatures(collection);
    this.territory = new VectorSource({ features });
    this.map = new OlMap({
      target,
      controls: defaultControls({ rotate: false }),
      view: this.view,
      layers: [
        new TileLayer({ source: new OSM({ tileLoadFunction: loadTileWithReferrer }) }),
        new VectorLayer({ source: this.territory, style: styleFeature }),
        new VectorLayer({ source: new VectorSource({ features: [this.accuracy, this.position] }), style: POSITION_STYLE }),
      ],
    });
    this.geolocation = new Geolocation({
      projection: this.view.getProjection(),
      trackingOptions: { enableHighAccuracy: true },
    });
    this.geolocation.on('change:position', () => this.showPosition());
    this.geolocation.on('change:accuracyGeometry', () => this.accuracy.setGeometry(this.geolocation.getAccuracyGeometry() ?? undefined));
    this.geolocation.on('error', () => {
      this.geolocation.setTracking(false);
      this.onLocate('denied');
    });
    this.resetView(0);
  }

  /** Fits the whole territory in the viewport. */
  resetView(duration = ANIMATION_MS): void {
    const extent = this.territory.getExtent();
    if (extent && !isEmpty(extent)) {
      this.view.fit(extent, { padding: FIT_PADDING, maxZoom: MAX_FIT_ZOOM, duration });
    }
  }

  /** Starts GPS tracking and centres the map on the next fix. */
  locate(): void {
    if (!('geolocation' in navigator)) {
      this.onLocate('unsupported');
      return;
    }
    this.centerOnNextFix = true;
    if (this.geolocation.getTracking() && this.geolocation.getPosition()) {
      this.showPosition();
      return;
    }
    this.geolocation.setTracking(true);
    this.onLocate('searching');
  }

  dispose(): void {
    this.geolocation.setTracking(false);
    this.geolocation.dispose();
    this.map.setTarget(undefined);
    this.map.dispose();
  }

  private showPosition(): void {
    const coordinates = this.geolocation.getPosition();
    this.position.setGeometry(coordinates ? new Point(coordinates) : undefined);
    if (!coordinates) {
      return;
    }
    this.onLocate('active');
    if (this.centerOnNextFix) {
      this.centerOnNextFix = false;
      this.view.animate({ center: coordinates, zoom: Math.max(this.view.getZoom() ?? 0, LOCATE_ZOOM), duration: ANIMATION_MS });
    }
  }
}
