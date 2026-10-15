import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { SetupApi } from './setup.api';

/** Reads the setup state; if the server cannot be reached the normal flow continues and reports the error itself. */
async function isSetupRequired(api: SetupApi): Promise<boolean> {
  try {
    return await api.isRequired();
  } catch {
    return false;
  }
}

/** Sends visitors to the initial setup while no administrator exists. */
export const loginGuard: CanActivateFn = async () => {
  const router = inject(Router);
  const api = inject(SetupApi);
  return (await isSetupRequired(api)) ? router.createUrlTree(['/setup']) : true;
};

/** The setup page exists only until the first administrator has been created. */
export const setupGuard: CanActivateFn = async () => {
  const router = inject(Router);
  const api = inject(SetupApi);
  return (await isSetupRequired(api)) ? true : router.createUrlTree(['/login']);
};
