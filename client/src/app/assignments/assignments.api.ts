import { HttpClient } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { firstValueFrom } from 'rxjs';

export interface OpenAssignmentDto {
  id: number;
  publisherId: number;
  publisherName: string;
  assignedAt: string;
}

export interface TerritoryStatusDto {
  territoryId: number;
  municipalityId: number;
  municipalityName: string;
  number: number;
  name: string;
  assignment: OpenAssignmentDto | null;
  lastReturnedAt: string | null;
  /** Times the territory was assigned in the last 365 days. */
  assignmentsLastYear: number;
}

export interface AssignRequest {
  territoryId: number;
  publisherId: number;
  assignedAt: string;
}

/** The clear token is returned only by assign and renew: it is never stored by the server. */
export interface ShareLinkDto {
  assignmentId: number;
  token: string;
}

export type ReturnedBy = 'publisher' | 'admin';

export interface AssignmentHistoryDto {
  id: number;
  publisherName: string;
  assignedAt: string;
  returnedAt: string | null;
  returnedBy: ReturnedBy | null;
  /** Admin who registered the return (null for publishers and older entries). */
  returnedByName: string | null;
}

@Injectable({ providedIn: 'root' })
export class AssignmentsApi {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = '/api/assignments';

  list(): Promise<TerritoryStatusDto[]> {
    return firstValueFrom(this.http.get<TerritoryStatusDto[]>(this.baseUrl));
  }

  assign(request: AssignRequest): Promise<ShareLinkDto> {
    return firstValueFrom(this.http.post<ShareLinkDto>(this.baseUrl, request));
  }

  /** Issues a new share link: the previous one stops working. */
  renewLink(assignmentId: number): Promise<ShareLinkDto> {
    return firstValueFrom(this.http.post<ShareLinkDto>(`${this.baseUrl}/${assignmentId}/link`, null));
  }

  registerReturn(assignmentId: number, returnedAt: string): Promise<void> {
    return firstValueFrom(this.http.post<void>(`${this.baseUrl}/${assignmentId}/return`, { returnedAt }));
  }

  /** Corrects the dates of an assignment; returnedAt must be null for an open one. */
  updateDates(assignmentId: number, dates: { assignedAt: string; returnedAt: string | null }): Promise<void> {
    return firstValueFrom(this.http.put<void>(`${this.baseUrl}/${assignmentId}`, dates));
  }

  /** Removes an assignment from the history. */
  delete(assignmentId: number): Promise<void> {
    return firstValueFrom(this.http.delete<void>(`${this.baseUrl}/${assignmentId}`));
  }

  history(territoryId: number): Promise<AssignmentHistoryDto[]> {
    return firstValueFrom(this.http.get<AssignmentHistoryDto[]>(`/api/territories/${territoryId}/assignments`));
  }
}
