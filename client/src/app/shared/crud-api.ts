import { HttpClient, HttpParams } from '@angular/common/http';
import { inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';

/** REST resource with list/create/update/delete under a base URL. */
export abstract class CrudApi<TDto, TRequest, TCreated = TDto> {
  protected readonly http = inject(HttpClient);

  protected constructor(protected readonly baseUrl: string) {}

  list(): Promise<TDto[]> {
    return firstValueFrom(this.http.get<TDto[]>(this.baseUrl));
  }

  create(request: TRequest): Promise<TCreated> {
    return firstValueFrom(this.http.post<TCreated>(this.baseUrl, request));
  }

  update(id: number, request: TRequest): Promise<TDto> {
    return firstValueFrom(this.http.put<TDto>(`${this.baseUrl}/${id}`, request));
  }

  delete(id: number, params?: Record<string, string>): Promise<void> {
    return firstValueFrom(this.http.delete<void>(`${this.baseUrl}/${id}`, { params: new HttpParams({ fromObject: params }) }));
  }
}
