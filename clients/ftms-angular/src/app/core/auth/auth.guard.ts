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
 * It attempts a silent refresh before redirecting, so a page reload - which loses the in memory
 * access token by design - does not look like a logout. The httpOnly cookie survives the reload
 * even though the token does not, which is exactly the property that makes this work.
 */
export const authGuard: CanActivateFn = async (_route, state) => {
  const auth = inject(AuthService);
  const router = inject(Router);

  if (auth.isAuthenticated()) {
    return true;
  }

  if (await auth.restore()) {
    return true;
  }

  // returnUrl is read by the login component, which navigates back here on success. It was
  // being set and ignored before, so a deep link survived the redirect only to be dropped on
  // the way back.
  return router.createUrlTree(['/auth/login'], {
    queryParams: { returnUrl: state.url },
  });
};
