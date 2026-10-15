import { FeatureLike } from 'ol/Feature';
import ImageTile from 'ol/ImageTile';
import Tile from 'ol/Tile';
import { Circle, Fill, Stroke, Style, Text } from 'ol/style';
import { FeatureProperties } from '../public.api';

const LABEL_FONT = '600 13px Roboto, system-ui, sans-serif';

/** OpenLayers style built from the colours computed by the server. */
export function featureStyle(properties: FeatureProperties, isPoint: boolean): Style {
  const stroke = new Stroke({ color: properties.stroke, width: properties.strokeWidth });
  const fill = new Fill({ color: properties.fill });
  return new Style({
    stroke,
    fill,
    image: isPoint ? new Circle({ radius: 7, fill: new Fill({ color: properties.stroke }), stroke: new Stroke({ color: '#fff', width: 2 }) }) : undefined,
    text: properties.name ? label(properties.name, isPoint) : undefined,
  });
}

/** Style function for a vector layer of server GeoJSON features. */
export function styleFeature(feature: FeatureLike): Style {
  const properties: FeatureProperties = {
    name: asString(feature.get('name')),
    stroke: asString(feature.get('stroke')) ?? '#1976d2',
    strokeWidth: Number(feature.get('strokeWidth')) || 3,
    fill: asString(feature.get('fill')) ?? 'rgba(0,0,0,0)',
  };
  return featureStyle(properties, feature.getGeometry()?.getType() === 'Point');
}

/** Style of the GPS position (blue dot) and of its accuracy circle. */
export const POSITION_STYLE = new Style({
  image: new Circle({ radius: 8, fill: new Fill({ color: '#1a73e8' }), stroke: new Stroke({ color: '#fff', width: 3 }) }),
  fill: new Fill({ color: 'rgba(26,115,232,0.15)' }),
  stroke: new Stroke({ color: 'rgba(26,115,232,0.5)', width: 1 }),
});

/**
 * OSM requires a Referer, while the app sends none (Referrer-Policy: no-referrer):
 * tiles opt in to sending the origin only, never the path that contains the share token.
 */
export function loadTileWithReferrer(tile: Tile, src: string): void {
  const image = (tile as ImageTile).getImage();
  if (image instanceof HTMLImageElement) {
    image.referrerPolicy = 'strict-origin';
    image.src = src;
  }
}

function label(text: string, isPoint: boolean): Text {
  return new Text({
    text,
    font: LABEL_FONT,
    overflow: true,
    offsetY: isPoint ? -18 : 0,
    fill: new Fill({ color: '#1f1f1f' }),
    stroke: new Stroke({ color: '#fff', width: 3 }),
  });
}

function asString(value: unknown): string | null {
  return typeof value === 'string' && value.length > 0 ? value : null;
}
