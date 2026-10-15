import { Component, inject, OnInit } from '@angular/core';
import { MatTabsModule } from '@angular/material/tabs';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { TranslatePipe } from '@ngx-translate/core';
import { MunicipalitiesApi, TerritoriesApi } from './territories.api';
import { TerritoriesTable } from './territories-table';
import { MunicipalitiesTable } from './municipalities-table';
import { Feedback } from '../shared/feedback';
import { ListResource } from '../shared/list-resource';

/** Territories and municipalities tabs; both lists reload together because counts depend on each other. */
@Component({
  selector: 'app-territories-page',
  imports: [MatTabsModule, MatProgressBarModule, TranslatePipe, TerritoriesTable, MunicipalitiesTable],
  template: `
    <div class="page-head"><h2>{{ 'nav.territories' | translate }}</h2></div>
    @if (territories.loading() || municipalities.loading()) {
      <mat-progress-bar mode="indeterminate" />
    }
    <mat-tab-group mat-stretch-tabs="false" animationDuration="0ms">
      <mat-tab [label]="'territories.tab' | translate">
        <app-territories-table [territories]="territories.items()" [municipalities]="municipalities.items()"
                               (changed)="reload()" />
      </mat-tab>
      <mat-tab [label]="'municipalities.tab' | translate">
        <app-municipalities-table [municipalities]="municipalities.items()" (changed)="reload()" />
      </mat-tab>
    </mat-tab-group>
  `,
})
export class TerritoriesPage implements OnInit {
  private readonly feedback = inject(Feedback);
  private readonly territoriesApi = inject(TerritoriesApi);
  private readonly municipalitiesApi = inject(MunicipalitiesApi);

  protected readonly territories = new ListResource(() => this.territoriesApi.list(), this.feedback);
  protected readonly municipalities = new ListResource(() => this.municipalitiesApi.list(), this.feedback);

  ngOnInit(): void {
    void this.reload();
  }

  protected async reload(): Promise<void> {
    await Promise.all([this.territories.reload(), this.municipalities.reload()]);
  }
}
