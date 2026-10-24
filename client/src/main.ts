import { bootstrapApplication } from '@angular/platform-browser';
import { appConfig } from './app/app.config';
import { App } from './app/app';
import { failSplash } from './app/core/splash';

bootstrapApplication(App, appConfig).catch((err) => {
  console.error(err);
  // The router could not start: turn the splash into an error state instead of leaving
  // the user in front of an endless spinner.
  failSplash();
});
