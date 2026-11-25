import {
  ApplicationConfig,
  inject,
  provideAppInitializer,
  provideBrowserGlobalErrorListeners,
} from '@angular/core';
import {
  provideHttpClient,
  withFetch,
  withInterceptors,
  withXsrfConfiguration,
} from '@angular/common/http';
import { provideRouter, withComponentInputBinding, withInMemoryScrolling } from '@angular/router';
import { TitleStrategy } from '@angular/router';
import { routes } from './app.routes';
import { I18n } from './core/i18n/i18n';
import { I18nTitleStrategy } from './core/i18n/i18n-title-strategy';
import { problemInterceptor } from './core/http/problem.interceptor';

export const appConfig: ApplicationConfig = {
  providers: [
    provideBrowserGlobalErrorListeners(),
    // Load the saved or browser-matched catalog before the first render; a failed load shows message ids.
    provideAppInitializer(() => {
      const i18n = inject(I18n);
      return i18n.use(i18n.resolveInitialCulture()).catch(() => undefined);
    }),
    { provide: TitleStrategy, useClass: I18nTitleStrategy },
    provideRouter(
      routes,
      withComponentInputBinding(),
      withInMemoryScrolling({ scrollPositionRestoration: 'enabled' }),
    ),
    // The SPA and the API share one origin behind Caddy; the API issues the XSRF cookie.
    provideHttpClient(
      withFetch(),
      withXsrfConfiguration({ cookieName: 'XSRF-TOKEN', headerName: 'X-XSRF-TOKEN' }),
      // Maps Problem Details and 429 Retry-After to localized ApiProblem errors.
      withInterceptors([problemInterceptor]),
    ),
  ],
};
