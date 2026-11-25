import { inject } from '@angular/core';
import { CanMatchFn, Router } from '@angular/router';
import { Session } from './session';

/** UX guard for admin routes. Non-admins, including anonymous visitors, are sent to the home page. */
export const adminGuard: CanMatchFn = async () => {
  const session = inject(Session);
  const router = inject(Router);
  if (!session.isLoaded()) {
    try {
      await session.refresh();
    } catch {
      return router.createUrlTree(['/']);
    }
  }
  return session.isAdmin() || router.createUrlTree(['/']);
};
