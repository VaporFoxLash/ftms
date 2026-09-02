import { HttpErrorResponse, HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { Router } from '@angular/router';
import { catchError, from, switchMap, throwError } from 'rxjs';
import { isCredentialEndpoint } from '../auth/auth.interceptor';
import { AuthService } from '../auth/auth.service';
import { ToastService } from '../notifications/toast.service';
import { summariseProblem } from './problem-details';

/**
 * The single ProblemDetails handler.
 *
 * design: doc 05 section 1 - every failure response has the same shape, so both clients write
 * one error handler. This is it.
 *
 * Two statuses are deliberately NOT toasted, because the component that made the call has to
 * handle them itself and a toast on top would be noise:
 *
 *  - 400, whose field errors belong on the form controls that caused them.
 *  - 412 and 428, which mean "reload and reapply" and need a specific recovery flow rather
 *    than a message that scrolls away (doc 05 section 6).
 *
 * The error is always rethrown. An interceptor that swallows failures leaves callers unable to
 * tell success from silence.
 */
export const errorInterceptor: HttpInterceptorFn = (request, next) => {
  const toasts = inject(ToastService);
  const auth = inject(AuthService);
  const router = inject(Router);

  return next(request).pipe(
    catchError((error: unknown) => {
      if (!(error instanceof HttpErrorResponse)) {
        return throwError(() => error);
      }

      // A 401 on an ordinary API call usually just means the fifteen minute access token
      // expired mid-session. Try the refresh cookie once and replay the request, so a user
      // typing into a form at minute sixteen is not thrown back to the login screen with their
      // work discarded.
      //
      // Only once, and never for the credential endpoints themselves: a 401 from login means
      // the password was wrong, and a 401 from refresh means the session is genuinely over.
      // Retrying either would loop.
      if (error.status === 401 && !isCredentialEndpoint(request.url)) {
        return from(auth.refresh()).pipe(
          switchMap((renewed) => {
            if (renewed) {
              return next(
                request.clone({
                  setHeaders: { Authorization: `Bearer ${auth.accessToken()}` },
                }),
              );
            }

            // Told, not shown silently. The user was working a moment ago and is about to lose
            // the screen; a bare redirect looks like the app crashed.
            toasts.error('Your session has expired', 'Please sign in again.');

            void router.navigate(['/auth/login'], {
              queryParams: { returnUrl: router.url },
            });

            return throwError(() => error);
          }),
        );
      }

      const problem = summariseProblem(error.status, error.error);

      // 401 from login or refresh is NOT toasted. Both are expected outcomes with a caller that
      // already handles them: the login form shows a failure inline, and the guard's startup
      // refresh 401s for every first time visitor - who would otherwise be greeted by an error
      // toast reading "No session cookie was presented" before they had done anything at all.
      const handledByTheCaller =
        [400, 412, 428].includes(error.status) ||
        (error.status === 401 && isCredentialEndpoint(request.url));

      if (!handledByTheCaller) {
        toasts.error(
          problem.message,
          problem.traceId
            ? `${problem.detail ?? ''} (trace ${problem.traceId})`.trim()
            : problem.detail,
        );
      }

      return throwError(() => error);
    }),
  );
};
