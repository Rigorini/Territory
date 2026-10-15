import { HttpClient, HttpHeaders } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { firstValueFrom } from 'rxjs';

/** Must match PublicEndpoints.TokenHeader: the token never appears in API URLs (and so in access logs). */
export const SHARE_TOKEN_HEADER = 'X-Share-Token';

export interface PublicTerritoryDto {
  municipalityName: string;
  number: number;
  name: string;
  publisherName: string;
  assignedAt: string;
}

export interface ReturnReceiptDto {
  assignedAt: string;
  returnedAt: string;
}

export interface FeatureProperties {
  name: string | null;
  stroke: string;
  strokeWidth: number;
  fill: string;
}

/** GeoJSON produced by the server from the MyMaps KML (lon/lat, WGS84). */
export interface GeoFeatureCollection {
  type: 'FeatureCollection';
  features: {
    type: 'Feature';
    geometry: { type: 'Point' | 'LineString' | 'Polygon'; coordinates: unknown };
    properties: FeatureProperties;
  }[];
}

@Injectable({ providedIn: 'root' })
export class PublicApi {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = '/api/public/territory';

  territory(token: string): Promise<PublicTerritoryDto> {
    return firstValueFrom(this.http.get<PublicTerritoryDto>(this.baseUrl, { headers: headers(token) }));
  }

  map(token: string): Promise<GeoFeatureCollection> {
    return firstValueFrom(this.http.get<GeoFeatureCollection>(`${this.baseUrl}/map`, { headers: headers(token) }));
  }

  returnTerritory(token: string): Promise<ReturnReceiptDto> {
    return firstValueFrom(this.http.post<ReturnReceiptDto>(`${this.baseUrl}/return`, null, { headers: headers(token) }));
  }
}

function headers(token: string): HttpHeaders {
  return new HttpHeaders({ [SHARE_TOKEN_HEADER]: token });
}
