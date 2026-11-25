import { provideHttpClient, withXsrfConfiguration } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { IdentityApi } from './identity-api';

const XSRF_COOKIE = 'XSRF-TOKEN';

describe('IdentityApi', () => {
  let api: IdentityApi;
  let httpTesting: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(
          withXsrfConfiguration({ cookieName: XSRF_COOKIE, headerName: 'X-XSRF-TOKEN' }),
        ),
        provideHttpClientTesting(),
      ],
    });
    api = TestBed.inject(IdentityApi);
    httpTesting = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    httpTesting.verify();
    document.cookie = `${XSRF_COOKIE}=; expires=Thu, 01 Jan 1970 00:00:00 GMT; path=/`;
  });

  it('posts sign-up data as JSON', async () => {
    const done = api.signUp({ displayName: 'Ana', email: 'ana@example.com', culture: 'pt-BR' });

    const request = httpTesting.expectOne('/api/identity/auth/signup');
    expect(request.request.method).toBe('POST');
    expect(request.request.body).toEqual({
      displayName: 'Ana',
      email: 'ana@example.com',
      culture: 'pt-BR',
    });
    request.flush(null, { status: 202, statusText: 'Accepted' });
    await done;
  });

  it('returns WebAuthn options as the raw JSON text', async () => {
    const done = api.beginRegistration({ userId: 'u1', token: 't1', purpose: 'confirm' });

    const request = httpTesting.expectOne('/api/identity/auth/registration/options');
    expect(request.request.body).toEqual({ userId: 'u1', token: 't1', purpose: 'confirm' });
    expect(request.request.responseType).toBe('text');
    request.flush('{"challenge":"AQID"}');
    expect(await done).toBe('{"challenge":"AQID"}');
  });

  it('sends the credential JSON as a string with keepSignedIn', async () => {
    const done = api.signIn('{"id":"abc"}', true);

    const request = httpTesting.expectOne('/api/identity/auth/signin');
    expect(request.request.body).toEqual({ credential: '{"id":"abc"}', keepSignedIn: true });
    request.flush(null, { status: 204, statusText: 'No Content' });
    await done;
  });

  it('loads the current user', async () => {
    const done = api.me();

    const request = httpTesting.expectOne('/api/identity/account/me');
    expect(request.request.method).toBe('GET');
    request.flush({ id: 'u1', displayName: 'Ana', email: 'ana@example.com', culture: 'en-US' });
    expect(await done).toEqual({
      id: 'u1',
      displayName: 'Ana',
      email: 'ana@example.com',
      culture: 'en-US',
    });
  });

  it('encodes the passkey id in rename and remove URLs', async () => {
    const rename = api.renamePasskey('ab+/=', 'Laptop');
    const renameRequest = httpTesting.expectOne('/api/identity/account/passkeys/ab%2B%2F%3D');
    expect(renameRequest.request.method).toBe('PATCH');
    expect(renameRequest.request.body).toEqual({ name: 'Laptop' });
    renameRequest.flush(null, { status: 204, statusText: 'No Content' });
    await rename;

    const remove = api.removePasskey('abc');
    const removeRequest = httpTesting.expectOne('/api/identity/account/passkeys/abc');
    expect(removeRequest.request.method).toBe('DELETE');
    removeRequest.flush(null, { status: 204, statusText: 'No Content' });
    await remove;
  });

  it('sends the XSRF header on every unsafe request', async () => {
    document.cookie = `${XSRF_COOKIE}=token-123; path=/`;
    const calls = [
      api.signOut(),
      api.updatePreferences('es-ES'),
      api.addPasskey('{"id":"x"}', 'Phone'),
      api.removePasskey('abc'),
      api.requestRecovery('ana@example.com'),
    ];

    const requests = httpTesting.match((request) => request.url.startsWith('/api/identity/'));
    expect(requests).toHaveLength(calls.length);
    for (const request of requests) {
      expect(request.request.headers.get('X-XSRF-TOKEN'), request.request.url).toBe('token-123');
      request.flush(null, { status: 204, statusText: 'No Content' });
    }
    await Promise.all(calls);
  });

  it('does not send the XSRF header on reads', async () => {
    document.cookie = `${XSRF_COOKIE}=token-123; path=/`;
    const done = api.listPasskeys();

    const request = httpTesting.expectOne('/api/identity/account/passkeys');
    expect(request.request.headers.has('X-XSRF-TOKEN')).toBe(false);
    request.flush([]);
    await done;
  });

  it('updates the display name with PUT profile', async () => {
    const done = api.updateProfile('Ana Maria');

    const request = httpTesting.expectOne('/api/identity/account/profile');
    expect(request.request.method).toBe('PUT');
    expect(request.request.body).toEqual({ displayName: 'Ana Maria' });
    request.flush(null, { status: 204, statusText: 'No Content' });
    await done;
  });

  it('uploads the avatar as multipart form data under the file field', async () => {
    const file = new File([new Uint8Array([1, 2, 3])], 'me.png', { type: 'image/png' });
    const done = api.uploadAvatar(file);

    const request = httpTesting.expectOne('/api/identity/account/avatar');
    expect(request.request.method).toBe('PUT');
    expect(request.request.body).toBeInstanceOf(FormData);
    expect((request.request.body as FormData).get('file')).toBe(file);
    request.flush({ avatarUrl: '/api/identity/account/avatar/2' });
    expect(await done).toEqual({ avatarUrl: '/api/identity/account/avatar/2' });
  });

  it('removes the avatar with DELETE', async () => {
    const done = api.removeAvatar();

    const request = httpTesting.expectOne('/api/identity/account/avatar');
    expect(request.request.method).toBe('DELETE');
    request.flush(null, { status: 204, statusText: 'No Content' });
    await done;
  });

  it('sends the XSRF header on profile and avatar writes', async () => {
    document.cookie = `${XSRF_COOKIE}=token-123; path=/`;
    const file = new File([new Uint8Array([1])], 'me.webp', { type: 'image/webp' });
    const calls = [api.updateProfile('Ana'), api.uploadAvatar(file), api.removeAvatar()];

    const requests = httpTesting.match((request) =>
      request.url.startsWith('/api/identity/account/'),
    );
    expect(requests).toHaveLength(calls.length);
    for (const request of requests) {
      expect(request.request.headers.get('X-XSRF-TOKEN'), request.request.url).toBe('token-123');
      request.flush(request.request.body instanceof FormData ? { avatarUrl: null } : null);
    }
    await Promise.all(calls);
  });
});
