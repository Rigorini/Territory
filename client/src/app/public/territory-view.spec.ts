import { TestBed } from '@angular/core/testing';
import { HttpErrorResponse } from '@angular/common/http';
import { provideTranslateService } from '@ngx-translate/core';
import { Feedback } from '../shared/feedback';
import { LocateState, MAP_CONTROLLER_FACTORY, MapControllerFactory } from './map/map-controller';
import { TerritoryMap } from './map/territory-map';
import { GeoFeatureCollection, PublicApi, PublicTerritoryDto } from './public.api';
import { TerritoryView } from './territory-view';

const territory: PublicTerritoryDto = {
  municipalityName: 'Giano',
  number: 7,
  name: 'Centro',
  publisherName: 'Rossi Mario',
  assignedAt: '2025-01-10T10:00:00Z',
};
const map: GeoFeatureCollection = { type: 'FeatureCollection', features: [] };
const notFound = new HttpErrorResponse({ status: 404, error: { code: 'public.invalidLink' } });
const serverError = new HttpErrorResponse({ status: 500 });

function fakeController() {
  let onLocate: (state: LocateState) => void = () => undefined;
  const controller = { resetView: vi.fn(), locate: vi.fn(), dispose: vi.fn() };
  const factory = vi.fn<MapControllerFactory>((_target, _map, callback) => {
    onLocate = callback;
    return controller;
  });
  return { controller, factory, emit: (state: LocateState) => onLocate(state) };
}

/** Renders, lets pending API promises settle, then renders their outcome. */
async function render(fixture: { detectChanges(): void; whenStable(): Promise<unknown> }) {
  fixture.detectChanges();
  await fixture.whenStable();
  await new Promise(resolve => setTimeout(resolve));
  fixture.detectChanges();
}

describe('TerritoryMap', () => {
  async function setup() {
    const fake = fakeController();
    TestBed.configureTestingModule({
      imports: [TerritoryMap],
      providers: [provideTranslateService(), { provide: MAP_CONTROLLER_FACTORY, useValue: fake.factory }],
    });
    const fixture = TestBed.createComponent(TerritoryMap);
    fixture.componentRef.setInput('map', map);
    await render(fixture);
    return { ...fake, fixture, element: fixture.nativeElement as HTMLElement };
  }

  it('creates the map after rendering and disposes it on destroy', async () => {
    const { factory, controller, fixture, element } = await setup();

    expect(factory).toHaveBeenCalledWith(element.querySelector('.map'), map, expect.any(Function));
    fixture.destroy();
    expect(controller.dispose).toHaveBeenCalled();
  });

  it('delegates reset view and locate', async () => {
    const { controller, element } = await setup();
    const [reset, locate] = Array.from(element.querySelectorAll('button'));

    reset.click();
    locate.click();

    expect(controller.resetView).toHaveBeenCalled();
    expect(controller.locate).toHaveBeenCalled();
  });

  it('shows the GPS state', async () => {
    const { emit, fixture, element } = await setup();

    emit('active');
    fixture.detectChanges();
    expect(element.querySelector('button.active')).not.toBeNull();
    expect(element.querySelector('.notice')).toBeNull();

    emit('denied');
    fixture.detectChanges();
    expect(element.querySelector('.notice')!.textContent).toContain('public.locate.denied');
  });
});

describe('TerritoryView', () => {
  async function setup(options: { territory?: Promise<unknown>; map?: Promise<unknown>; confirmed?: boolean } = {}) {
    const api = {
      territory: vi.fn(() => options.territory ?? Promise.resolve(territory)),
      map: vi.fn(() => options.map ?? Promise.resolve(map)),
      returnTerritory: vi.fn().mockResolvedValue({ assignedAt: '2025-01-10T10:00:00Z', returnedAt: '2025-03-15T10:00:00Z' }),
    };
    const feedback = { confirm: vi.fn().mockResolvedValue(options.confirmed ?? true), notifyError: vi.fn() };
    TestBed.configureTestingModule({
      imports: [TerritoryView],
      providers: [
        provideTranslateService(),
        { provide: PublicApi, useValue: api },
        { provide: Feedback, useValue: feedback },
        { provide: MAP_CONTROLLER_FACTORY, useValue: fakeController().factory },
      ],
    });
    const fixture = TestBed.createComponent(TerritoryView);
    fixture.componentRef.setInput('token', 'tok');
    await render(fixture);
    const element = fixture.nativeElement as HTMLElement;
    const click = async (selector: string) => {
      element.querySelector<HTMLButtonElement>(selector)!.click();
      await render(fixture);
    };
    return { api, feedback, fixture, element, click };
  }

  it('shows the territory and its map', async () => {
    const { api, element } = await setup();

    expect(api.territory).toHaveBeenCalledWith('tok');
    expect(api.map).toHaveBeenCalledWith('tok');
    expect(element.querySelector('.municipality')!.textContent).toContain('Giano');
    expect(element.querySelector('h1')!.textContent).toContain('public.territoryTitle');
    expect(element.querySelector('app-territory-map')).not.toBeNull();
  });

  it('shows the invalid link page for unknown or returned links', async () => {
    const { api, element } = await setup({ territory: Promise.reject(notFound) });

    expect(element.textContent).toContain('public.invalidTitle');
    expect(api.map).not.toHaveBeenCalled();
  });

  it('offers a retry when loading fails', async () => {
    const { api, element, click } = await setup({ territory: Promise.reject(serverError) });
    expect(element.textContent).toContain('public.loadError');

    api.territory.mockResolvedValue(territory);
    await click('button');

    expect(element.querySelector('app-territory-map')).not.toBeNull();
  });

  it('keeps the return button when the map is unavailable', async () => {
    const { api, element, click } = await setup({ map: Promise.reject(serverError) });
    expect(element.textContent).toContain('public.mapUnavailable');
    expect(element.querySelector('button.return')).not.toBeNull();

    api.map.mockResolvedValue(map);
    await click('.map-area button');

    expect(element.querySelector('app-territory-map')).not.toBeNull();
  });

  it('returns after confirmation and thanks with the held duration', async () => {
    const { api, feedback, element, click } = await setup();

    await click('button.return');

    expect(feedback.confirm).toHaveBeenCalledWith(expect.objectContaining({ params: { number: 7, name: 'Centro' } }));
    expect(api.returnTerritory).toHaveBeenCalledWith('tok');
    expect(element.textContent).toContain('public.thanksTitle');
    expect(element.textContent).toContain('public.months.other');
    expect(element.textContent).toContain('public.and');
    expect(element.textContent).toContain('public.days.other');
  });

  it('shows only days when held for less than a month', async () => {
    const { api, element, click } = await setup();
    api.returnTerritory.mockResolvedValue({ assignedAt: '2025-01-10T10:00:00Z', returnedAt: '2025-01-11T10:00:00Z' });

    await click('button.return');

    expect(element.textContent).not.toContain('public.months');
    expect(element.textContent).toContain('public.days.one');
  });

  it('does nothing when the return is cancelled', async () => {
    const { api, element, click } = await setup({ confirmed: false });

    await click('button.return');

    expect(api.returnTerritory).not.toHaveBeenCalled();
    expect(element.querySelector('button.return')).not.toBeNull();
  });

  it('shows the invalid page when the link died meanwhile', async () => {
    const { api, element, click } = await setup();
    api.returnTerritory.mockRejectedValue(notFound);

    await click('button.return');

    expect(element.textContent).toContain('public.invalidTitle');
  });

  it('reports other return errors and stays on the page', async () => {
    const { api, feedback, element, click } = await setup();
    api.returnTerritory.mockRejectedValue(serverError);

    await click('button.return');

    expect(feedback.notifyError).toHaveBeenCalledWith(serverError);
    expect(element.querySelector('button.return')).not.toBeNull();
  });
});
