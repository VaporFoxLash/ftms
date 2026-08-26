import { HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { AuthService } from './auth.service';

/**
 * Attaches the bearer token to API calls.
 *
 * design: doc 06 section 3. Two deliberate exclusions:
 *
 *  - Anything that is not /api is left alone, so static assets never carry a credential.
 *  - The token endpoint itself is skipped, because sending a stale token to the thing that
 *    issues tokens is how you get a confusing 401 during sign in.
 */
export const authInterceptor: HttpInterceptorFn = (request, next) => {
  const auth = inject(AuthService);
  const token = auth.accessToken();

  const needsToken =
    token !== null && request.url.startsWith('/api') && !request.url.startsWith('/api/dev/token');

  if (!needsToken) {
    return next(request);
  }

  return next(
    request.clone({
      setHeaders: { Authorization: `Bearer ${token}` },
    }),
  );
};
