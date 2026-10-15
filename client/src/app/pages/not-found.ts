import { Component } from '@angular/core';
import { TranslatePipe } from '@ngx-translate/core';

@Component({
  selector: 'app-not-found',
  imports: [TranslatePipe],
  template: `<main class="center"><h1>{{ 'errors.notFound' | translate }}</h1></main>`,
  styles: `.center { display: grid; place-items: center; min-height: 100dvh; padding: 24px; text-align: center; }`,
})
export class NotFound {}
