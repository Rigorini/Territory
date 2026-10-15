import { Injectable } from '@angular/core';
import { CrudApi } from '../shared/crud-api';

export interface PublisherDto {
  id: number;
  firstName: string;
  lastName: string;
}

export interface SavePublisherRequest {
  firstName: string;
  lastName: string;
}

/** Creating a publisher with the name of a deleted one reactivates the old record. */
export interface CreatePublisherResult {
  publisher: PublisherDto;
  reactivated: boolean;
}

@Injectable({ providedIn: 'root' })
export class PublishersApi extends CrudApi<PublisherDto, SavePublisherRequest, CreatePublisherResult> {
  constructor() {
    super('/api/publishers');
  }
}
