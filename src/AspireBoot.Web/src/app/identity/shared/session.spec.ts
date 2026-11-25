import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { I18n } from '../../core/i18n/i18n';
import { problemInterceptor } from '../../core/http/problem.interceptor';
import { Session } from './session';

const STORAGE_KEY = 'aspireboot.culture';
const ANA = { id: 'u1', displayName: 'Ana', email: 'ana@example.com', culture: 'en-US' };

describe('Session', () => {
  let session: Session;
  let httpTesting: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(withInterceptors([problemInterceptor])),
        provideHttpClientTesting(),
      ],
    });
    session = TestBed.inject(Session);
    httpTesting = TestBed.inject(HttpTestingController);
  });

  beforeEach(() => localStorage.clear());

  afterEach(() => {
    httpTesting.verify();
    localStorage.clear();
  });

  it('starts anonymous and not yet loaded', () => {
    expect(session.currentUser()).toBeNull();
    expect(session.isSignedIn()).toBe(false);
    expect(session.isLoaded()).toBe(false);
  });

  it('loads the current user from GET me', async () => {
    const done = session.refresh();

    httpTesting.expectOne('/api/identity/account/me').flush(ANA);
    await done;

    expect(session.currentUser()).toEqual(ANA);
    expect(session.isSignedIn()).toBe(true);
    expect(session.isLoaded()).toBe(true);
  });

  it('becomes anonymous when the API answers 401', async () => {
    const done = session.refresh();

    httpTesting
      .expectOne('/api/identity/account/me')
      .flush(null, { status: 401, statusText: 'Unauthorized' });
    await done;

    expect(session.currentUser()).toBeNull();
    expect(session.isLoaded()).toBe(true);
  });

  it('keeps the previous state and rethrows on other failures', async () => {
    const first = session.refresh();
    httpTesting.expectOne('/api/identity/account/me').flush(ANA);
    await first;

    const second = session.refresh();
    httpTesting
      .expectOne('/api/identity/account/me')
      .flush(null, { status: 500, statusText: 'Server Error' });

    await expect(second).rejects.toMatchObject({ status: 500 });
    expect(session.currentUser()).toEqual(ANA);
  });

  it('signs in by posting the credential and then loading the user', async () => {
    const done = session.signIn('{"id":"abc"}', false);

    const post = httpTesting.expectOne('/api/identity/auth/signin');
    expect(post.request.body).toEqual({ credential: '{"id":"abc"}', keepSignedIn: false });
    post.flush(null, { status: 204, statusText: 'No Content' });

    (await vi.waitFor(() => httpTesting.expectOne('/api/identity/account/me'))).flush(ANA);
    await done;

    expect(session.currentUser()).toEqual(ANA);
  });

  it('signs out, clears the user and posts to signout', async () => {
    const load = session.refresh();
    httpTesting.expectOne('/api/identity/account/me').flush(ANA);
    await load;

    const done = session.signOut();
    httpTesting
      .expectOne('/api/identity/auth/signout')
      .flush(null, { status: 204, statusText: 'No Content' });
    await done;

    expect(session.currentUser()).toBeNull();
    expect(session.isSignedIn()).toBe(false);
  });

  it('applies the server culture when it differs from the local one', async () => {
    const i18n = TestBed.inject(I18n);
    const done = session.refresh();
    httpTesting.expectOne('/api/identity/account/me').flush({ ...ANA, culture: 'pt-BR' });
    await flushCatalog(httpTesting, 'en-US');
    await flushCatalog(httpTesting, 'pt-BR');
    await done;

    expect(i18n.culture()).toBe('pt-BR');
    expect(localStorage.getItem(STORAGE_KEY)).toBe('pt-BR');
  });

  it('lets the server culture win over a locally chosen one', async () => {
    const i18n = TestBed.inject(I18n);
    i18n.culture.set('es-ES');
    localStorage.setItem(STORAGE_KEY, 'es-ES');

    const done = session.refresh();
    httpTesting.expectOne('/api/identity/account/me').flush(ANA);
    await flushCatalog(httpTesting, 'en-US');
    await done;

    expect(i18n.culture()).toBe('en-US');
    expect(localStorage.getItem(STORAGE_KEY)).toBe('en-US');
  });

  it('keeps the local culture when the catalog for the server culture fails to load', async () => {
    const i18n = TestBed.inject(I18n);
    const done = session.refresh();
    httpTesting.expectOne('/api/identity/account/me').flush({ ...ANA, culture: 'pt-BR' });
    await flushCatalog(httpTesting, 'en-US');
    (await vi.waitFor(() => httpTesting.expectOne('/i18n/pt-BR.json'))).flush(null, {
      status: 500,
      statusText: 'Server Error',
    });
    await done;

    expect(session.currentUser()?.culture).toBe('pt-BR');
    expect(i18n.culture()).toBe('en-US');
  });

  it('saves a language change on the server while signed in', async () => {
    const i18n = TestBed.inject(I18n);
    const load = session.refresh();
    httpTesting.expectOne('/api/identity/account/me').flush(ANA);
    await load;

    const done = session.changeCulture('pt-BR');
    await flushCatalog(httpTesting, 'en-US');
    await flushCatalog(httpTesting, 'pt-BR');
    await done;

    const put = await vi.waitFor(() => httpTesting.expectOne('/api/identity/account/preferences'));
    expect(put.request.method).toBe('PUT');
    expect(put.request.body).toEqual({ culture: 'pt-BR' });
    put.flush(null, { status: 204, statusText: 'No Content' });
    await vi.waitFor(() => expect(session.currentUser()?.culture).toBe('pt-BR'));

    expect(i18n.culture()).toBe('pt-BR');
    expect(localStorage.getItem(STORAGE_KEY)).toBe('pt-BR');
  });

  it('keeps the new language locally when saving it on the server fails', async () => {
    const load = session.refresh();
    httpTesting.expectOne('/api/identity/account/me').flush(ANA);
    await load;

    const done = session.changeCulture('es-ES');
    await flushCatalog(httpTesting, 'en-US');
    await flushCatalog(httpTesting, 'es-ES');
    await done;

    (await vi.waitFor(() => httpTesting.expectOne('/api/identity/account/preferences'))).flush(
      null,
      { status: 500, statusText: 'Server Error' },
    );
    await vi.waitFor(() => expect(TestBed.inject(I18n).culture()).toBe('es-ES'));

    expect(session.currentUser()?.culture).toBe('en-US');
  });

  it('does not call the server when anonymous', async () => {
    const done = session.changeCulture('pt-BR');
    await flushCatalog(httpTesting, 'en-US');
    await flushCatalog(httpTesting, 'pt-BR');
    await done;

    expect(TestBed.inject(I18n).culture()).toBe('pt-BR');
  });

  it('patches the current user in place without calling the server', async () => {
    const load = session.refresh();
    httpTesting.expectOne('/api/identity/account/me').flush(ANA);
    await load;

    session.patchUser({ displayName: 'Ana Maria', avatarUrl: '/api/identity/account/avatar/1' });

    expect(session.currentUser()).toEqual({
      ...ANA,
      displayName: 'Ana Maria',
      avatarUrl: '/api/identity/account/avatar/1',
    });
  });

  it('ignores patches while anonymous', () => {
    session.patchUser({ displayName: 'Ghost' });

    expect(session.currentUser()).toBeNull();
  });
});

async function flushCatalog(testing: HttpTestingController, culture: string): Promise<void> {
  (await vi.waitFor(() => testing.expectOne(`/i18n/${culture}.json`))).flush({});
}
