import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { PartialMatchRouteSnapshot, Route, Router, UrlTree } from '@angular/router';
import { problemInterceptor } from '../../core/http/problem.interceptor';
import { authGuard } from './auth.guard';
import { Session } from './session';

const ANA = { id: 'u1', displayName: 'Ana', email: 'ana@example.com', culture: 'en-US' };

describe('authGuard', () => {
  let httpTesting: HttpTestingController;
  let router: Router;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(withInterceptors([problemInterceptor])),
        provideHttpClientTesting(),
      ],
    });
    httpTesting = TestBed.inject(HttpTestingController);
    router = TestBed.inject(Router);
  });

  afterEach(() => httpTesting.verify());

  function runGuard() {
    return TestBed.runInInjectionContext(() =>
      authGuard({} as Route, [], {} as PartialMatchRouteSnapshot),
    );
  }

  it('allows the route when the user is signed in', async () => {
    const guard = runGuard();
    httpTesting.expectOne('/api/identity/account/me').flush(ANA);

    expect(await guard).toBe(true);
  });

  it('redirects anonymous users to sign-in', async () => {
    const guard = runGuard();
    httpTesting
      .expectOne('/api/identity/account/me')
      .flush(null, { status: 401, statusText: 'Unauthorized' });

    const result = await guard;
    expect(result).toBeInstanceOf(UrlTree);
    expect(router.serializeUrl(result as UrlTree)).toBe('/signin');
  });

  it('redirects to sign-in when the session cannot be loaded', async () => {
    const guard = runGuard();
    httpTesting
      .expectOne('/api/identity/account/me')
      .flush(null, { status: 500, statusText: 'Server Error' });

    expect(router.serializeUrl((await guard) as UrlTree)).toBe('/signin');
  });

  it('does not reload the user once the session is known', async () => {
    const session = TestBed.inject(Session);
    const first = session.refresh();
    httpTesting.expectOne('/api/identity/account/me').flush(ANA);
    await first;

    expect(await runGuard()).toBe(true);
  });
});
