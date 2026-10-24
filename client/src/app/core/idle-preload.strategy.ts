import { Injectable } from '@angular/core';
import { PreloadingStrategy, Route } from '@angular/router';
import { EMPTY, Observable, catchError, from, switchMap } from 'rxjs';

/** Delay after the browser becomes idle, so lazy chunks never compete with the first paint. */
const PRELOAD_DELAY_MS = 1200;

interface NetworkInformationLike {
  saveData?: boolean;
  effectiveType?: string;
}

/**
 * Preloads lazy routes in the background, but only after the first navigation is done and
 * the browser is idle. On data-saver connections and very slow networks nothing is preloaded.
 * Routes can opt out with `data: { preload: false }` (used for the heavy map view).
 */
@Injectable({ providedIn: 'root' })
export class IdlePreloadStrategy implements PreloadingStrategy {
  preload(route: Route, load: () => Observable<unknown>): Observable<unknown> {
    if (route.data?.['preload'] === false || !canPreload()) {
      return EMPTY;
    }
    return from(whenIdle()).pipe(switchMap(() => load().pipe(catchError(() => EMPTY))));
  }
}

function canPreload(): boolean {
  const connection = (navigator as Navigator & { connection?: NetworkInformationLike }).connection;
  if (connection?.saveData) {
    return false;
  }
  return !connection?.effectiveType || !['slow-2g', '2g'].includes(connection.effectiveType);
}

function whenIdle(): Promise<void> {
  return new Promise(resolve => {
    const start = () => setTimeout(resolve, PRELOAD_DELAY_MS);
    if (typeof requestIdleCallback === 'function') {
      requestIdleCallback(start, { timeout: 4000 });
    } else {
      setTimeout(start, 2000);
    }
  });
}
