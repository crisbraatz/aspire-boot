import { HttpClient, provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { Router } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { I18n } from '../i18n/i18n';
import { Session } from '../../identity/shared/session';
import { ApiProblem, problemInterceptor } from './problem.interceptor';
import en from '../../../../public/i18n/en-US.json';

describe('problemInterceptor', () => {
  let http: HttpClient;
  let httpTesting: HttpTestingController;

  beforeEach(async () => {
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(withInterceptors([problemInterceptor])),
        provideHttpClientTesting(),
      ],
    });
    http = TestBed.inject(HttpClient);
    httpTesting = TestBed.inject(HttpTestingController);
    const loading = TestBed.inject(I18n).use('en-US');
    httpTesting.expectOne('/i18n/en-US.json').flush(en);
    await loading;
  });

  afterEach(() => httpTesting.verify());

  /** Sends a request and returns the error it rejects with. */
  async function failWith(
    respond: (request: ReturnType<HttpTestingController['expectOne']>) => void,
  ): Promise<ApiProblem> {
    const result = firstValueFrom(http.get('/api/x')).catch((error: unknown) => error);
    respond(httpTesting.expectOne('/api/x'));
    const error = await result;
    expect(error).toBeInstanceOf(ApiProblem);
    return error as ApiProblem;
  }

  it('maps a Problem Details code to a localized message and keeps the code', async () => {
    const error = await failWith((request) =>
      request.flush(
        { title: 'Conflict', code: 'identity.lastPasskey' },
        { status: 409, statusText: 'Conflict' },
      ),
    );

    expect(error.status).toBe(409);
    expect(error.code).toBe('identity.lastPasskey');
    expect(error.message).toBe(en['errors.lastPasskey']);
  });

  it('reads the code from a Problem Details body delivered as text', async () => {
    const error = await failWith((request) =>
      request.flush(JSON.stringify({ code: 'identity.invalidLink' }), {
        status: 400,
        statusText: 'Bad Request',
      }),
    );

    expect(error.code).toBe('identity.invalidLink');
    expect(error.message).toBe(en['errors.invalidLink']);
  });

  it('maps every known identity code to its message', async () => {
    const cases: Record<string, string> = {
      'identity.invalidLink': 'errors.invalidLink',
      'identity.passkeyFailed': 'errors.passkeyFailed',
      'identity.signInFailed': 'errors.signInFailed',
      'identity.signInRequired': 'errors.signInRequired',
      'antiforgery.invalid': 'errors.antiforgery',
    };
    for (const [code, key] of Object.entries(cases)) {
      const error = await failWith((request) =>
        request.flush({ code }, { status: 400, statusText: 'Bad Request' }),
      );
      expect(error.message, code).toBe(en[key as keyof typeof en]);
    }
  });

  it('falls back to the generic message for unknown codes and bodies', async () => {
    const error = await failWith((request) =>
      request.flush('<html>oops</html>', { status: 500, statusText: 'Server Error' }),
    );

    expect(error.code).toBeUndefined();
    expect(error.message).toBe(en['errors.unexpected']);
  });

  it('maps 429 to a message with the Retry-After seconds', async () => {
    const error = await failWith((request) =>
      request.flush(
        { code: 'rateLimited' },
        { status: 429, statusText: 'Too Many Requests', headers: { 'Retry-After': '42' } },
      ),
    );

    expect(error.status).toBe(429);
    expect(error.retryAfterSeconds).toBe(42);
    expect(error.message).toBe(en['errors.rateLimited'].replace('{seconds}', '42'));
  });

  it('maps 429 without Retry-After to the generic rate limit message', async () => {
    const error = await failWith((request) =>
      request.flush(null, { status: 429, statusText: 'Too Many Requests' }),
    );

    expect(error.retryAfterSeconds).toBeUndefined();
    expect(error.message).toBe(en['errors.rateLimitedGeneric']);
  });

  it('reads Retry-After given as an HTTP date', async () => {
    const inThirtySeconds = new Date(Date.now() + 30_000).toUTCString();
    const error = await failWith((request) =>
      request.flush(null, {
        status: 429,
        statusText: 'Too Many Requests',
        headers: { 'Retry-After': inThirtySeconds },
      }),
    );

    expect(error.retryAfterSeconds).toBeGreaterThan(0);
    expect(error.retryAfterSeconds).toBeLessThanOrEqual(30);
  });

  it('maps a network failure (status 0) to the connection message', async () => {
    const error = await failWith((request) =>
      request.error(new ProgressEvent('error'), { status: 0, statusText: '' }),
    );

    expect(error.status).toBe(0);
    expect(error.message).toBe(en['errors.network']);
  });
  it('redirects to sign-in on 401 from a signed-in API call', async () => {
    const router = TestBed.inject(Router);
    const navigate = vi.spyOn(router, 'navigateByUrl').mockResolvedValue(true);

    await failWith((request) => request.flush(null, { status: 401, statusText: 'Unauthorized' }));

    expect(navigate).toHaveBeenCalledWith('/signin');
  });

  it('clears the session state when it redirects after a 401', async () => {
    vi.spyOn(TestBed.inject(Router), 'navigateByUrl').mockResolvedValue(true);
    const markSignedOut = vi.spyOn(TestBed.inject(Session), 'markSignedOut');

    await failWith((request) => request.flush(null, { status: 401, statusText: 'Unauthorized' }));

    expect(markSignedOut).toHaveBeenCalledTimes(1);
  });

  it('redirects on 401 from a sub-resource of the current-user path', async () => {
    const router = TestBed.inject(Router);
    const navigate = vi.spyOn(router, 'navigateByUrl').mockResolvedValue(true);
    const result = firstValueFrom(http.get('/api/identity/account/me/avatar')).catch(
      (e: unknown) => e,
    );
    httpTesting
      .expectOne('/api/identity/account/me/avatar')
      .flush(null, { status: 401, statusText: 'Unauthorized' });
    await result;

    expect(navigate).toHaveBeenCalledWith('/signin');
  });

  it('does not redirect on 401 from the current-user probe with a query string', async () => {
    const router = TestBed.inject(Router);
    const navigate = vi.spyOn(router, 'navigateByUrl').mockResolvedValue(true);
    const result = firstValueFrom(http.get('/api/identity/account/me?x=1')).catch(
      (e: unknown) => e,
    );
    httpTesting
      .expectOne('/api/identity/account/me?x=1')
      .flush(null, { status: 401, statusText: 'Unauthorized' });
    await result;

    expect(navigate).not.toHaveBeenCalled();
  });

  it('does not redirect on 401 from the current-user probe', async () => {
    const router = TestBed.inject(Router);
    const navigate = vi.spyOn(router, 'navigateByUrl').mockResolvedValue(true);
    const result = firstValueFrom(http.get('/api/identity/account/me')).catch((e: unknown) => e);
    httpTesting
      .expectOne('/api/identity/account/me')
      .flush(null, { status: 401, statusText: 'Unauthorized' });
    await result;

    expect(navigate).not.toHaveBeenCalled();
  });
});
