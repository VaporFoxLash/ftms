import { HttpErrorResponse, HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { Router } from '@angular/router';
import { catchError, throwError } from 'rxjs';
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

      const problem = summariseProblem(error.status, error.error);

      if (error.status === 401) {
        auth.signOut();
        void router.navigate(['/auth/login']);
      }

      const handledByTheCaller = [400, 412, 428].includes(error.status);

      if (!handledByTheCaller) {
        toasts.error(
          problem.message,
          problem.traceId ? `${problem.detail ?? ''} (trace ${problem.traceId})`.trim() : problem.detail,
        );
      }

      return throwError(() => error);
    }),
  );
};
