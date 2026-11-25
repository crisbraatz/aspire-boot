import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { PartialMatchRouteSnapshot, Route, Router, UrlTree } from '@angular/router';
import { problemInterceptor } from '../../core/http/problem.interceptor';
import { adminGuard } from './admin.guard';
import { Session } from './session';

const ANA = {
  id: 'u1',
  displayName: 'Ana',
  email: 'ana@example.com',
  culture: 'en-US',
  avatarUrl: null,
  roles: ['User'],
};
const ADMIN = { ...ANA, id: 'u2', roles: ['Admin'] };

describe('adminGuard', () => {
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
      adminGuard({} as Route, [], {} as PartialMatchRouteSnapshot),
    );
  }

  it('allows the route for an admin', async () => {
    const guard = runGuard();
    httpTesting.expectOne('/api/identity/account/me').flush(ADMIN);

    expect(await guard).toBe(true);
  });

  it('redirects a signed-in non-admin to the home page', async () => {
    const guard = runGuard();
    httpTesting.expectOne('/api/identity/account/me').flush(ANA);

    const result = await guard;
    expect(result).toBeInstanceOf(UrlTree);
    expect(router.serializeUrl(result as UrlTree)).toBe('/');
  });

  it('redirects anonymous visitors to the home page', async () => {
    const guard = runGuard();
    httpTesting
      .expectOne('/api/identity/account/me')
      .flush(null, { status: 401, statusText: 'Unauthorized' });

    expect(router.serializeUrl((await guard) as UrlTree)).toBe('/');
  });

  it('does not reload the user once the session is known', async () => {
    const session = TestBed.inject(Session);
    const first = session.refresh();
    httpTesting.expectOne('/api/identity/account/me').flush(ADMIN);
    await first;

    expect(await runGuard()).toBe(true);
  });
});
