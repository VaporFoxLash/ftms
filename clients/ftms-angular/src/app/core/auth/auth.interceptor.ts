import { HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { AuthService } from './auth.service';

/** The endpoints that carry the refresh cookie rather than a bearer token. */
const AUTH_ENDPOINTS = '/api/auth/';

/** Sign in and session renewal. Never sent a bearer token; always sent the cookie. */
export const isCredentialEndpoint = (url: string): boolean =>
  url.startsWith('/api/auth/login') || url.startsWith('/api/auth/refresh');

/**
 * Attaches the bearer token to API calls, and the refresh cookie to the auth ones.
 *
 * design: doc 06 section 3. Three deliberate rules:
 *
 *  - Anything that is not /api is left alone, so static assets never carry a credential.
 *  - Login and refresh are skipped, because sending a stale or absent token to the thing that
 *    issues tokens produces a confusing 401 in the middle of signing in.
 *  - Every /api/auth call sets withCredentials, so the browser attaches and accepts the
 *    httpOnly refresh cookie. It is redundant while the SPA is served same origin - which it is
 *    today, behind the dev proxy - and load bearing the moment the API moves to its own origin.
 *    Setting it now means that move is a configuration change rather than a debugging session.
 */
export const authInterceptor: HttpInterceptorFn = (request, next) => {
  const auth = inject(AuthService);

  if (request.url.startsWith(AUTH_ENDPOINTS)) {
    const withCookie = request.clone({ withCredentials: true });

    if (isCredentialEndpoint(request.url)) {
      return next(withCookie);
    }

    // Logout and /me are authenticated AND need the cookie, so they fall through to the bearer
    // logic below with withCredentials already applied.
    const token = auth.accessToken();

    return next(
      token === null
        ? withCookie
        : withCookie.clone({ setHeaders: { Authorization: `Bearer ${token}` } }),
    );
  }

  const token = auth.accessToken();

  if (token === null || !request.url.startsWith('/api')) {
    return next(request);
  }

  return next(
    request.clone({
      setHeaders: { Authorization: `Bearer ${token}` },
    }),
  );
};
