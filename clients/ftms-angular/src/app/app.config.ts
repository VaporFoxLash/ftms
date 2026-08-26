import { ApplicationConfig, provideBrowserGlobalErrorListeners } from '@angular/core';
import { provideHttpClient, withFetch, withInterceptors } from '@angular/common/http';
import { provideRouter, withComponentInputBinding } from '@angular/router';
import { routes } from './app.routes';
import { provideApiBaseUrl } from './core/api/api-base-url';
import { authInterceptor } from './core/auth/auth.interceptor';
import { errorInterceptor } from './core/http/error.interceptor';

export const appConfig: ApplicationConfig = {
  providers: [
    provideBrowserGlobalErrorListeners(),
    provideRouter(routes, withComponentInputBinding()),

    // Interceptor order is execution order: auth attaches the bearer token on the way OUT,
    // then error inspects the response on the way BACK. Reversing them would mean the error
    // handler never saw a request the auth interceptor had modified.
    provideHttpClient(withFetch(), withInterceptors([authInterceptor, errorInterceptor])),

    // Same origin, relative URLs. The dev server proxies /api to the backend.
    provideApiBaseUrl(''),
  ],
};
