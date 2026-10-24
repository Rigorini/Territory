import { inject, Injectable } from '@angular/core';
import { NavigationEnd, NavigationError, Router } from '@angular/router';
import { filter, firstValueFrom, take, timer } from 'rxjs';
import { LanguageService } from './language.service';
import { failSplash, hideSplash, whenPublicViewReady } from './splash';

/** Maximum time the splash waits for the first translation file after the route is ready. */
const TRANSLATIONS_TIMEOUT_MS = 4000;

/** Maximum time the public link page may take to put its map on screen. */
const PUBLIC_VIEW_TIMEOUT_MS = 12000;

/**
 * Keeps the static splash visible until the first route (including the auth guard request)
 * and its translations are ready, then fades it out. Hiding before the router is ready would
 * show an empty page for the whole authentication round trip. The public link page also
 * loads its territory and map after the navigation: there the splash stays until the map
 * has been painted, so the publisher never sees a bare spinner.
 */
@Injectable({ providedIn: 'root' })
export class SplashScreenService {
  private readonly router = inject(Router);
  private readonly language = inject(LanguageService);
  private handled = false;

  /** Subscribed from an app initializer so the first navigation event cannot be missed. */
  watch(): void {
    this.router.events
      .pipe(
        filter(event => event instanceof NavigationEnd || event instanceof NavigationError),
        take(1),
      )
      .subscribe(event => {
        if (event instanceof NavigationError) {
          this.fail();
        } else {
          void this.finish(event.urlAfterRedirects);
        }
      });

    // With blocking initial navigation the event above may already be in the past.
    if (this.router.navigated) {
      void this.finish(this.router.url);
    }
  }

  private async finish(url: string): Promise<void> {
    if (this.handled) {
      return;
    }
    this.handled = true;
    await Promise.race([
      this.language.whenReady(),
      firstValueFrom(timer(TRANSLATIONS_TIMEOUT_MS)),
    ]);
    if (isPublicView(url)) {
      await whenPublicViewReady(PUBLIC_VIEW_TIMEOUT_MS);
    }
    hideSplash();
  }

  private fail(): void {
    if (this.handled) {
      return;
    }
    this.handled = true;
    failSplash();
  }
}

/** Publisher links are the only routes whose content arrives after the first navigation. */
function isPublicView(url: string): boolean {
  return url.startsWith('/t/');
}
