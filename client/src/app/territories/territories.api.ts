import { Injectable } from '@angular/core';
import { CrudApi } from '../shared/crud-api';

export interface MunicipalityDto {
  id: number;
  name: string;
  territoryCount: number;
}

export interface SaveMunicipalityRequest {
  name: string;
}

export interface TerritoryDto {
  id: number;
  municipalityId: number;
  municipalityName: string;
  number: number;
  name: string;
  /** MyMaps viewer link rebuilt by the server from the stored map id. */
  mapUrl: string;
  assignmentCount: number;
}

export interface SaveTerritoryRequest {
  municipalityId: number | null;
  number: number | null;
  name: string;
  mapUrl: string;
}

@Injectable({ providedIn: 'root' })
export class MunicipalitiesApi extends CrudApi<MunicipalityDto, SaveMunicipalityRequest> {
  constructor() {
    super('/api/municipalities');
  }
}

@Injectable({ providedIn: 'root' })
export class TerritoriesApi extends CrudApi<TerritoryDto, SaveTerritoryRequest> {
  constructor() {
    super('/api/territories');
  }

  /** Deleting a territory with assignments requires confirmAssignments: its history is removed too. */
  deleteTerritory(territory: TerritoryDto): Promise<void> {
    return territory.assignmentCount > 0 ? this.delete(territory.id, { confirmAssignments: 'true' }) : this.delete(territory.id);
  }
}
