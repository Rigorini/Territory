import {
  ApplicationConfig,
  inject,
  isDevMode,
  provideAppInitializer,
  provideBrowserGlobalErrorListeners,
} from '@angular/core';
import { provideHttpClient, withFetch, withInterceptors, withXsrfConfiguration } from '@angular/common/http';
import { provideRouter, withComponentInputBinding, withPreloading } from '@angular/router';
import { provideServiceWorker } from '@angular/service-worker';
import { provideTranslateService } from '@ngx-translate/core';
import { provideTranslateHttpLoader } from '@ngx-translate/http-loader';
import { routes } from './app.routes';
import { authInterceptor } from './core/auth.interceptor';
import { IdlePreloadStrategy } from './core/idle-preload.strategy';
import { FALLBACK_LANGUAGE, LanguageService } from './core/language.service';
import { SplashScreenService } from './core/splash-screen.service';
import { ThemeService } from './core/theme.service';
import { provideDayDateAdapter } from './shared/day-date-adapter';

export const appConfig: ApplicationConfig = {
  providers: [
    provideBrowserGlobalErrorListeners(),
    provideRouter(routes, withComponentInputBinding(), withPreloading(IdlePreloadStrategy)),
    provideHttpClient(
      withFetch(),
      // Must match AuthSetup.XsrfCookieName / XsrfHeaderName on the server.
      withXsrfConfiguration({ cookieName: '__Host-XSRF-TOKEN', headerName: 'X-XSRF-TOKEN' }),
      withInterceptors([authInterceptor]),
    ),
    provideTranslateService({
      loader: provideTranslateHttpLoader({ prefix: '/i18n/', suffix: '.json' }),
      fallbackLang: FALLBACK_LANGUAGE,
    }),
    provideDayDateAdapter(),
    provideServiceWorker('ngsw-worker.js', {
      enabled: !isDevMode(),
      // Never compete with the first render: register once the app is stable, at the latest 5 s.
      registrationStrategy: 'registerWhenStable:5000',
    }),
    provideAppInitializer(() => {
      inject(ThemeService);
      inject(LanguageService).init();
      inject(SplashScreenService).watch();
    }),
  ],
};
