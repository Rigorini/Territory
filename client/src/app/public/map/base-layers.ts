import TileLayer from 'ol/layer/Tile';
import OSM from 'ol/source/OSM';
import XYZ from 'ol/source/XYZ';
import { loadTileWithReferrer } from './map-style';

/** Basemap of the public map: the street map, or satellite imagery. */
export type BaseLayerKind = 'street' | 'satellite';

/** Basemaps can be switched: the satellite one is optional and its service may be unreachable. */
export const BASE_LAYER_KINDS: readonly BaseLayerKind[] = ['street', 'satellite'];

/** Satellite tiles that never load, over this many errors, mean the service is unavailable. */
const SATELLITE_ERROR_LIMIT = 3;

/** Stacking of the map layers: the basemap, the satellite labels, the territory, the GPS dot. */
export const BASEMAP_Z = 0;
export const LABELS_Z = 1;
export const TERRITORY_Z = 2;
export const POSITION_Z = 3;

const ESRI_SERVICES = 'https://server.arcgisonline.com/ArcGIS/rest/services';
const ESRI_ATTRIBUTION =
  'Tiles © <a href="https://www.esri.com/" target="_blank" rel="noopener noreferrer">Esri</a> — ' +
  'Source: Esri, Maxar, Earthstar Geographics, and the GIS User Community';

/** Layers of a basemap; satellite imagery comes with its place labels on top. */
export function createBaseLayers(kind: BaseLayerKind): TileLayer<OSM | XYZ>[] {
  if (kind === 'street') {
    return [new TileLayer({ source: new OSM({ tileLoadFunction: loadTileWithReferrer }), zIndex: BASEMAP_Z })];
  }
  return [
    new TileLayer({ source: esriSource('World_Imagery'), zIndex: BASEMAP_Z }),
    new TileLayer({ source: esriSource('Reference/World_Boundaries_and_Places'), zIndex: LABELS_Z }),
  ];
}

/**
 * Satellite tiles failing while none ever loaded: the service is blocked or unreachable,
 * rather than a single gap in the imagery. One loaded tile clears the doubt.
 */
export function satelliteUnavailable(loaded: number, errors: number): boolean {
  return loaded === 0 && errors >= SATELLITE_ERROR_LIMIT;
}

function esriSource(service: string): XYZ {
  return new XYZ({
    url: `${ESRI_SERVICES}/${service}/MapServer/tile/{z}/{y}/{x}`,
    // The imagery is licensed from several providers: the attribution is not optional.
    attributions: [ESRI_ATTRIBUTION],
    attributionsCollapsible: false,
    tileLoadFunction: loadTileWithReferrer,
    maxZoom: 19,
  });
}
