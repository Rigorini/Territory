import { Feature } from 'ol';
import ImageTile from 'ol/ImageTile';
import Tile from 'ol/Tile';
import { LineString, Point, Polygon } from 'ol/geom';
import CircleStyle from 'ol/style/Circle';
import { GeoFeatureCollection } from '../public.api';
import { LocateState, MAP_CONTROLLER_FACTORY, OlMapController } from './map-controller';
import { featureStyle, loadTileWithReferrer, styleFeature } from './map-style';
import { TestBed } from '@angular/core/testing';

const collection: GeoFeatureCollection = {
  type: 'FeatureCollection',
  features: [
    {
      type: 'Feature',
      geometry: { type: 'Polygon', coordinates: [[[12.5, 42.8], [12.6, 42.8], [12.6, 42.9], [12.5, 42.8]]] },
      properties: { name: 'Zona A', stroke: 'rgba(0,143,47,1)', strokeWidth: 4, fill: 'rgba(0,143,47,0.3)' },
    },
  ],
};

describe('map style', () => {
  it('uses server colours, a label and a dot for points', () => {
    const style = featureStyle({ name: 'Casa', stroke: 'rgba(1,2,3,1)', strokeWidth: 5, fill: 'rgba(0,0,0,0)' }, true);

    expect(style.getStroke()!.getColor()).toBe('rgba(1,2,3,1)');
    expect(style.getStroke()!.getWidth()).toBe(5);
    expect(style.getFill()!.getColor()).toBe('rgba(0,0,0,0)');
    expect(style.getText()!.getText()).toBe('Casa');
    expect(style.getText()!.getOffsetY()).toBe(-18);
    expect(style.getImage()).toBeInstanceOf(CircleStyle);
  });

  it('has no label nor dot for unnamed areas', () => {
    const style = featureStyle({ name: null, stroke: 'red', strokeWidth: 3, fill: 'blue' }, false);

    expect(style.getText()).toBeNull();
    expect(style.getImage()).toBeNull();
  });

  it('reads feature properties with safe defaults', () => {
    const area = new Feature({ geometry: new Polygon([[[0, 0], [1, 0], [1, 1], [0, 0]]]), name: 'A', stroke: 'red', strokeWidth: 2, fill: 'blue' });
    const bare = new Feature({ geometry: new LineString([[0, 0], [1, 1]]), name: '', strokeWidth: 'x' });
    const point = new Feature({ geometry: new Point([0, 0]) });

    expect(styleFeature(area).getStroke()!.getWidth()).toBe(2);
    expect(styleFeature(area).getText()!.getText()).toBe('A');
    const fallback = styleFeature(bare);
    expect(fallback.getStroke()!.getColor()).toBe('#1976d2');
    expect(fallback.getStroke()!.getWidth()).toBe(3);
    expect(fallback.getFill()!.getColor()).toBe('rgba(0,0,0,0)');
    expect(fallback.getText()).toBeNull();
    expect(styleFeature(point).getImage()).toBeInstanceOf(CircleStyle);
  });

  it('loads OSM tiles sending only the origin as referrer', () => {
    const image = document.createElement('img');
    const tile = { getImage: () => image } as unknown as ImageTile;

    loadTileWithReferrer(tile, 'https://tile.openstreetmap.org/1/2/3.png');

    expect(image.referrerPolicy).toBe('strict-origin');
    expect(image.src).toBe('https://tile.openstreetmap.org/1/2/3.png');
  });

  it('ignores tiles without an image element', () => {
    const canvas = document.createElement('canvas');
    const tile = { getImage: () => canvas } as unknown as Tile;

    expect(() => loadTileWithReferrer(tile, 'https://x')).not.toThrow();
  });
});

describe('OlMapController', () => {
  beforeAll(() => {
    // jsdom has no ResizeObserver, which OpenLayers uses to follow the target size.
    globalThis.ResizeObserver ??= class {
      observe(): void {}
      unobserve(): void {}
      disconnect(): void {}
    };
  });

  let target: HTMLElement;
  let states: LocateState[];
  let watch: { success?: PositionCallback; error?: PositionErrorCallback };

  beforeEach(() => {
    target = document.createElement('div');
    document.body.appendChild(target);
    states = [];
    watch = {};
  });

  afterEach(() => {
    target.remove();
    Reflect.deleteProperty(navigator, 'geolocation');
  });

  function stubGeolocation(): void {
    Object.defineProperty(navigator, 'geolocation', {
      configurable: true,
      value: {
        watchPosition: vi.fn((success: PositionCallback, error: PositionErrorCallback) => {
          watch = { success, error };
          return 1;
        }),
        clearWatch: vi.fn(),
      },
    });
  }

  function fix(lon: number, lat: number): void {
    watch.success!({
      coords: { longitude: lon, latitude: lat, accuracy: 15, altitude: null, altitudeAccuracy: null, heading: null, speed: null },
      timestamp: Date.now(),
    } as GeolocationPosition);
  }

  function create(): OlMapController {
    return new OlMapController(target, collection, state => states.push(state));
  }

  it('renders into the target and can be disposed', () => {
    const controller = create();

    expect(target.querySelector('.ol-viewport')).not.toBeNull();
    expect(() => controller.resetView()).not.toThrow();
    controller.dispose();
    expect(target.querySelector('.ol-viewport')).toBeNull();
  });

  it('accepts an empty map', () => {
    const controller = new OlMapController(target, { type: 'FeatureCollection', features: [] }, () => undefined);

    expect(() => controller.resetView()).not.toThrow();
    controller.dispose();
  });

  it('reports unsupported geolocation', () => {
    expect('geolocation' in navigator).toBe(false);
    const controller = create();

    controller.locate();

    expect(states).toEqual(['unsupported']);
    controller.dispose();
  });

  it('tracks the position and centres on the first fix', () => {
    stubGeolocation();
    const controller = create();

    controller.locate();
    fix(12.55, 42.85);
    controller.locate();

    expect(states).toEqual(['searching', 'active', 'active']);
    controller.dispose();
  });

  it('reports a denied permission', () => {
    stubGeolocation();
    const controller = create();

    controller.locate();
    watch.error!({ code: 1, message: 'denied', PERMISSION_DENIED: 1, POSITION_UNAVAILABLE: 2, TIMEOUT: 3 } as GeolocationPositionError);

    expect(states).toEqual(['searching', 'denied']);
    controller.dispose();
  });

  it('is the default factory', () => {
    const controller = TestBed.inject(MAP_CONTROLLER_FACTORY)(target, collection, () => undefined);

    expect(controller).toBeInstanceOf(OlMapController);
    controller.dispose();
  });
});
