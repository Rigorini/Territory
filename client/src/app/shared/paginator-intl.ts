import { inject, Injectable } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { MatPaginatorIntl } from '@angular/material/paginator';
import { TranslateService } from '@ngx-translate/core';
import { merge } from 'rxjs';

/** Paginator labels taken from the app translations, kept in sync with the active language. */
@Injectable()
export class PaginatorIntl extends MatPaginatorIntl {
  private readonly translate = inject(TranslateService);

  constructor() {
    super();
    merge(this.translate.onLangChange, this.translate.onTranslationChange)
      .pipe(takeUntilDestroyed())
      .subscribe(() => this.translateLabels());
    this.translateLabels();
  }

  private translateLabels(): void {
    this.itemsPerPageLabel = this.translate.instant('common.itemsPerPage');
    this.firstPageLabel = this.translate.instant('common.firstPage');
    this.previousPageLabel = this.translate.instant('common.previousPage');
    this.nextPageLabel = this.translate.instant('common.nextPage');
    this.lastPageLabel = this.translate.instant('common.lastPage');
    this.getRangeLabel = (page, pageSize, length) => {
      const pages = pageSize > 0 ? Math.ceil(length / pageSize) : 0;
      return this.translate.instant('common.pageRange', { page: page + 1, total: pages });
    };
    this.changes.next();
  }
}
