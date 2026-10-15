import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { MunicipalitiesApi, TerritoriesApi, TerritoryDto } from './territories.api';
import { PublishersApi } from '../publishers/publishers.api';
import { AdminsApi } from '../admins/admins.api';

const territory: TerritoryDto = {
  id: 4, municipalityId: 1, municipalityName: 'Giano', number: 3, name: 'Centro', mapUrl: 'u', assignmentCount: 0,
};

function setup() {
  TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
  return { http: TestBed.inject(HttpTestingController) };
}

describe('CrudApi resources', () => {
  afterEach(() => TestBed.inject(HttpTestingController).verify());

  it('lists, creates, updates and deletes municipalities', async () => {
    const { http } = setup();
    const api = TestBed.inject(MunicipalitiesApi);

    const list = api.list();
    http.expectOne({ method: 'GET', url: '/api/municipalities' }).flush([]);
    expect(await list).toEqual([]);

    const created = api.create({ name: 'Giano' });
    const post = http.expectOne({ method: 'POST', url: '/api/municipalities' });
    expect(post.request.body).toEqual({ name: 'Giano' });
    post.flush({ id: 1, name: 'Giano', territoryCount: 0 });
    expect((await created).id).toBe(1);

    const updated = api.update(1, { name: 'Bastardo' });
    http.expectOne({ method: 'PUT', url: '/api/municipalities/1' }).flush({ id: 1, name: 'Bastardo', territoryCount: 0 });
    await updated;

    const deleted = api.delete(1);
    http.expectOne({ method: 'DELETE', url: '/api/municipalities/1' }).flush(null);
    await deleted;
  });

  it('confirms the history deletion only for territories with assignments', async () => {
    const { http } = setup();
    const api = TestBed.inject(TerritoriesApi);

    const plain = api.deleteTerritory(territory);
    http.expectOne(r => r.method === 'DELETE' && r.urlWithParams === '/api/territories/4').flush(null);
    await plain;

    const withHistory = api.deleteTerritory({ ...territory, assignmentCount: 2 });
    http.expectOne(r => r.urlWithParams === '/api/territories/4?confirmAssignments=true').flush(null);
    await withHistory;
  });

  it('returns the reactivation flag when creating a publisher', async () => {
    const { http } = setup();
    const api = TestBed.inject(PublishersApi);

    const created = api.create({ firstName: 'Anna', lastName: 'Rossi' });
    http.expectOne({ method: 'POST', url: '/api/publishers' })
      .flush({ publisher: { id: 9, firstName: 'Anna', lastName: 'Rossi' }, reactivated: true });

    expect((await created).reactivated).toBe(true);
  });

  it('serves admins under /api/admins', async () => {
    const { http } = setup();

    const list = TestBed.inject(AdminsApi).list();
    http.expectOne({ method: 'GET', url: '/api/admins' }).flush([]);

    expect(await list).toEqual([]);
  });
});
