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
import XYZ from 'ol/source/XYZ';
import { GeoFeatureCollection } from '../public.api';
import {
  BASE_LAYER_KINDS, BaseLayerKind, createBaseLayers, POSITION_Z, satelliteUnavailable, TERRITORY_Z,
} from './base-layers';
import { POSITION_STYLE, styleFeature } from './map-style';

export type LocateState = 'idle' | 'searching' | 'active' | 'denied' | 'unsupported';

/** What the map reports to the component. */
export type MapNotice =
  | { kind: 'locate'; state: LocateState }
  | { kind: 'satelliteUnavailable' };

/** Map operations used by the component; OpenLayers stays behind this interface. */
export interface MapController {
  setBaseLayer(kind: BaseLayerKind): void;
  resetView(): void;
  locate(): void;
  dispose(): void;
}

export type MapControllerFactory = (
  target: HTMLElement,
  map: GeoFeatureCollection,
  onNotice: (notice: MapNotice) => void,
) => MapController;

export const MAP_CONTROLLER_FACTORY = new InjectionToken<MapControllerFactory>('MapControllerFactory', {
  providedIn: 'root',
  factory: () => (target, map, onNotice) => new OlMapController(target, map, onNotice),
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
  private readonly basemaps: Record<BaseLayerKind, TileLayer<OSM | XYZ>[]>;
  private baseLayer: BaseLayerKind = 'street';
  private satelliteLoaded = 0;
  private satelliteErrors = 0;
  private centerOnNextFix = false;

  constructor(
    target: HTMLElement,
    collection: GeoFeatureCollection,
    private readonly onNotice: (notice: MapNotice) => void,
  ) {
    const features = new GeoJSON({ featureProjection: 'EPSG:3857' }).readFeatures(collection);
    this.territory = new VectorSource({ features });
    const street = createBaseLayers('street');
    this.basemaps = { street, satellite: [] };
    this.map = new OlMap({
      target,
      controls: defaultControls({ rotate: false }),
      view: this.view,
      layers: [
        ...street,
        new VectorLayer({ source: this.territory, style: styleFeature, zIndex: TERRITORY_Z }),
        new VectorLayer({
          source: new VectorSource({ features: [this.accuracy, this.position] }),
          style: POSITION_STYLE,
          zIndex: POSITION_Z,
        }),
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
      this.onNotice({ kind: 'locate', state: 'denied' });
    });
    this.resetView(0);
  }

  /** Shows one basemap and hides the others, keeping the view and the overlays untouched. */
  setBaseLayer(kind: BaseLayerKind): void {
    const wanted = this.layersOf(kind);
    this.baseLayer = kind;
    for (const other of BASE_LAYER_KINDS) {
      if (other !== kind) {
        this.basemaps[other].forEach(layer => layer.setVisible(false));
      }
    }
    wanted.forEach(layer => layer.setVisible(true));
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
      this.onNotice({ kind: 'locate', state: 'unsupported' });
      return;
    }
    this.centerOnNextFix = true;
    if (this.geolocation.getTracking() && this.geolocation.getPosition()) {
      this.showPosition();
      return;
    }
    this.geolocation.setTracking(true);
    this.onNotice({ kind: 'locate', state: 'searching' });
  }

  dispose(): void {
    this.geolocation.setTracking(false);
    this.geolocation.dispose();
    this.map.setTarget(undefined);
    this.map.dispose();
  }

  /** Satellite layers are built on first use, so that they are never requested otherwise. */
  private layersOf(kind: BaseLayerKind): TileLayer<OSM | XYZ>[] {
    if (kind === 'satellite' && this.basemaps.satellite.length === 0) {
      const layers = createBaseLayers('satellite');
      this.basemaps.satellite = layers;
      layers.forEach(layer => this.map.addLayer(layer));
      this.watchSatelliteTiles(layers);
      this.satelliteLoaded = 0;
      this.satelliteErrors = 0;
    }
    return this.basemaps[kind];
  }

  /**
   * An unavailable imagery service must not leave the map blank: after a few failures and
   * no tile loaded at all, the street map comes back and the component is told why.
   */
  private watchSatelliteTiles(layers: TileLayer<OSM | XYZ>[]): void {
    const imagery = layers[0].getSource();
    imagery?.on('tileloadend', () => this.satelliteLoaded++);
    imagery?.on('tileloaderror', () => {
      this.satelliteErrors++;
      if (this.baseLayer === 'satellite' && satelliteUnavailable(this.satelliteLoaded, this.satelliteErrors)) {
        this.setBaseLayer('street');
        this.onNotice({ kind: 'satelliteUnavailable' });
      }
    });
  }

  private showPosition(): void {
    const coordinates = this.geolocation.getPosition();
    this.position.setGeometry(coordinates ? new Point(coordinates) : undefined);
    if (!coordinates) {
      return;
    }
    this.onNotice({ kind: 'locate', state: 'active' });
    if (this.centerOnNextFix) {
      this.centerOnNextFix = false;
      this.view.animate({ center: coordinates, zoom: Math.max(this.view.getZoom() ?? 0, LOCATE_ZOOM), duration: ANIMATION_MS });
    }
  }
}
