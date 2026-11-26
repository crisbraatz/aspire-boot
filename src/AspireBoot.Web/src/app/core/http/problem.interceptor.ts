import { HttpErrorResponse, HttpInterceptorFn } from '@angular/common/http';
import { Injector, inject } from '@angular/core';
import { Router } from '@angular/router';
import { catchError, throwError } from 'rxjs';
import { Session } from '../../identity/shared/session';
import { I18n } from '../i18n/i18n';

/** An API failure with a localized message. `code` is the Problem Details `code` extension, when present. */
export class ApiProblem extends Error {
  constructor(
    readonly status: number,
    readonly code: string | undefined,
    message: string,
    readonly retryAfterSeconds?: number,
  ) {
    super(message);
    this.name = 'ApiProblem';
  }
}

/** Problem Details codes from the API mapped to message ids. Unknown codes use errors.unexpected. */
const MESSAGE_IDS: Readonly<Record<string, string>> = {
  'identity.lastPasskey': 'errors.lastPasskey',
  'identity.invalidLink': 'errors.invalidLink',
  'identity.passkeyFailed': 'errors.passkeyFailed',
  'identity.signInFailed': 'errors.signInFailed',
  'identity.signInRequired': 'errors.signInRequired',
  'antiforgery.invalid': 'errors.antiforgery',
  'identity.sessionNotFound': 'errors.sessionNotFound',
  'identity.currentSession': 'errors.currentSession',
};

/** Requests that answer 401 to an anonymous visitor by design, so they never redirect. */
const ANONYMOUS_OK_PREFIXES: readonly string[] = ['/api/identity/auth/'];
const CURRENT_USER_PATH = '/api/identity/account/me';

function answersAnonymously(url: string): boolean {
  const path = url.split(/[?#]/, 1)[0];
  return path === CURRENT_USER_PATH || ANONYMOUS_OK_PREFIXES.some((p) => path.includes(p));
}

/** Turns every HTTP error into an ApiProblem with a localized message, including 429 with the retry time. */
export const problemInterceptor: HttpInterceptorFn = (request, next) => {
  const i18n = inject(I18n);
  const router = inject(Router);
  const injector = inject(Injector);
  return next(request).pipe(
    catchError((error: unknown) => {
      if (!(error instanceof HttpErrorResponse)) return throwError(() => error);
      // The session ended (signed out elsewhere, revoked or expired): send the visitor to sign in.
      if (error.status === 401 && !answersAnonymously(request.url)) {
        // Lazy: Session depends on HttpClient, which depends on this interceptor.
        injector.get(Session).markSignedOut();
        void router.navigateByUrl('/signin');
      }
      return throwError(() => toApiProblem(error, i18n));
    }),
  );
};

function toApiProblem(error: HttpErrorResponse, i18n: I18n): ApiProblem {
  if (error.status === 0) {
    return new ApiProblem(0, undefined, i18n.t('errors.network'));
  }
  const code = problemCode(error.error);
  if (error.status === 429) {
    const seconds = retryAfterSeconds(error.headers.get('Retry-After'));
    return seconds === undefined
      ? new ApiProblem(429, code ?? 'rateLimited', i18n.t('errors.rateLimitedGeneric'))
      : new ApiProblem(
          429,
          code ?? 'rateLimited',
          i18n.t('errors.rateLimited', { seconds }),
          seconds,
        );
  }
  const messageId = (code && MESSAGE_IDS[code]) || 'errors.unexpected';
  return new ApiProblem(error.status, code, i18n.t(messageId));
}

/** Problem Details arrive as JSON, or as text when the request asked for text (the WebAuthn option calls). */
function problemCode(body: unknown): string | undefined {
  const parsed = typeof body === 'string' ? parseJson(body) : body;
  if (typeof parsed !== 'object' || parsed === null || !('code' in parsed)) return undefined;
  const code = parsed.code;
  return typeof code === 'string' ? code : undefined;
}

function parseJson(text: string): unknown {
  try {
    return JSON.parse(text);
  } catch {
    return undefined;
  }
}

/** Accepts both forms of Retry-After: delay in seconds, or an HTTP date. */
function retryAfterSeconds(value: string | null): number | undefined {
  if (!value) return undefined;
  if (/^\d+$/.test(value.trim())) return Number(value.trim());
  const date = Date.parse(value);
  if (Number.isNaN(date)) return undefined;
  return Math.max(0, Math.ceil((date - Date.now()) / 1000));
}
