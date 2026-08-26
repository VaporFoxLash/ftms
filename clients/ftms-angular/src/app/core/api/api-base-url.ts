import { ApiConfiguration } from './generated/api-configuration';
import { EnvironmentProviders, Provider } from '@angular/core';

/**
 * The generated client resolves every URL against ApiConfiguration.rootUrl, which
 * ng-openapi-gen defaults to whatever `servers` said in the OpenAPI document. That value points
 * at whichever machine generated the snapshot, so it is wrong everywhere else.
 *
 * An empty root makes every request same-origin and relative, which is what we want in all
 * three environments: the dev server proxies /api to the backend (proxy.conf.json), and in
 * production the SPA is served as static files from the same host as the API
 * (design: doc 04 section 6 - everything on one Windows host for the first version).
 */
export function provideApiBaseUrl(rootUrl = ''): (Provider | EnvironmentProviders)[] {
  return [
    {
      provide: ApiConfiguration,
      useValue: Object.assign(new ApiConfiguration(), { rootUrl }),
    },
  ];
}
