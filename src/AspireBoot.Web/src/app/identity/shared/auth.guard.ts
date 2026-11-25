import { inject } from '@angular/core';
import { CanMatchFn, Router } from '@angular/router';
import { Session } from './session';

/** UX guard for signed-in routes. The API still enforces authorization on every call. */
export const authGuard: CanMatchFn = async () => {
  const session = inject(Session);
  const router = inject(Router);
  if (!session.isLoaded()) {
    try {
      await session.refresh();
    } catch {
      return router.createUrlTree(['/signin']);
    }
  }
  return session.isSignedIn() || router.createUrlTree(['/signin']);
};
