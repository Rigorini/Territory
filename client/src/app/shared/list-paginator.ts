import { Component, input } from '@angular/core';
import { MatPaginatorIntl, MatPaginatorModule } from '@angular/material/paginator';
import { Paging } from './paging';
import { PaginatorIntl } from './paginator-intl';

/** Bottom paginator of a table; hidden while the list has no rows. */
@Component({
  selector: 'app-list-paginator',
  imports: [MatPaginatorModule],
  providers: [{ provide: MatPaginatorIntl, useClass: PaginatorIntl }],
  // The bar sits below the table and outside its card: it takes the page background instead of the
  // table rows' surface colour.
  styles: `
    mat-paginator {
      --mat-paginator-container-background-color: transparent;
    }
  `,
  template: `
    @if (paging().length() > 0) {
      <mat-paginator [length]="paging().length()" [pageIndex]="paging().index()" [pageSize]="paging().size()"
                     [pageSizeOptions]="paging().pageSizeOptions" [showFirstLastButtons]="true"
                     (page)="paging().onPage($event)" />
    }
  `,
})
export class ListPaginator {
  readonly paging = input.required<Paging<unknown>>();
}
