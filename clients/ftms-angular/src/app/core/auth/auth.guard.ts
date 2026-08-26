import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { AuthService } from './auth.service';

/**
 * Keeps unauthenticated users off the transaction screens.
 *
 * This is a UX guard and nothing more. design: doc 04 decision 3 - both clients are thin by
 * contract and the API owns all behaviour, so the real enforcement is the ASP.NET Core policy
 * on every endpoint (doc 06 section 3). A guard that a user can disable in devtools is not a
 * security control; it just avoids rendering a screen that would only fill with 401s.
 *
 * TODO design: doc 06 - once refresh tokens land, this should attempt a silent refresh against
 * the httpOnly cookie before redirecting, so a page reload does not look like a logout.
 */
export const authGuard: CanActivateFn = (_route, state) => {
  const auth = inject(AuthService);
  const router = inject(Router);

  if (auth.isAuthenticated()) {
    return true;
  }

  return router.createUrlTree(['/auth/login'], {
    queryParams: { returnUrl: state.url },
  });
};
