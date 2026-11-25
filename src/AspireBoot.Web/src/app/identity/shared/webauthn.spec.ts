import * as webauthn from './webauthn';

interface FakeCredential {
  toJSON: () => { id: string; type: string };
}

const credentialsMock = { create: vi.fn(), get: vi.fn() };
const PUBLIC_KEY_OPTIONS = JSON.stringify({
  challenge: 'AQID',
  user: { id: 'BAUG', name: 'a@example.com', displayName: 'A' },
  excludeCredentials: [{ id: 'BwgJ', type: 'public-key' }],
});
const REQUEST_OPTIONS = JSON.stringify({ challenge: 'AQID', allowCredentials: [] });

function stubPublicKeyCredential(statics: Record<string, unknown> = {}): void {
  class FakePublicKeyCredential {}
  Object.assign(FakePublicKeyCredential, {
    isConditionalMediationAvailable: vi.fn().mockResolvedValue(true),
    ...statics,
  });
  vi.stubGlobal('PublicKeyCredential', FakePublicKeyCredential);
}

function fakeCredential(): FakeCredential {
  return { toJSON: () => ({ id: 'abc', type: 'public-key' }) };
}

describe('webauthn', () => {
  beforeEach(() => {
    credentialsMock.create.mockReset();
    credentialsMock.get.mockReset();
    Object.defineProperty(navigator, 'credentials', { value: credentialsMock, configurable: true });
    stubPublicKeyCredential();
  });

  afterEach(() => {
    vi.unstubAllGlobals();
    delete (navigator as unknown as Record<string, unknown>)['credentials'];
  });

  describe('feature detection', () => {
    it('reports unsupported when PublicKeyCredential is missing', async () => {
      vi.stubGlobal('PublicKeyCredential', undefined);

      expect(webauthn.isSupported()).toBe(false);
      expect(await webauthn.create(PUBLIC_KEY_OPTIONS)).toEqual({ status: 'unsupported' });
      expect(credentialsMock.create).not.toHaveBeenCalled();
    });

    it('reports conditional UI availability from the browser', async () => {
      expect(webauthn.isSupported()).toBe(true);
      expect(await webauthn.isConditionalAvailable()).toBe(true);
    });

    it('reports conditional UI as unavailable when the browser cannot tell', async () => {
      stubPublicKeyCredential({
        isConditionalMediationAvailable: vi.fn().mockRejectedValue(new Error('boom')),
      });

      expect(await webauthn.isConditionalAvailable()).toBe(false);
    });
  });

  describe('create', () => {
    it('returns the credential as JSON on success', async () => {
      credentialsMock.create.mockResolvedValue(fakeCredential());

      const outcome = await webauthn.create(PUBLIC_KEY_OPTIONS);

      expect(outcome).toEqual({
        status: 'success',
        credential: '{"id":"abc","type":"public-key"}',
      });
    });

    it('uses the native parser when the browser provides one', async () => {
      const parsed = { challenge: new ArrayBuffer(1) };
      const parse = vi.fn().mockReturnValue(parsed);
      stubPublicKeyCredential({ parseCreationOptionsFromJSON: parse });
      credentialsMock.create.mockResolvedValue(fakeCredential());

      await webauthn.create(PUBLIC_KEY_OPTIONS);

      expect(parse).toHaveBeenCalledWith(JSON.parse(PUBLIC_KEY_OPTIONS));
      expect(credentialsMock.create).toHaveBeenCalledWith({ publicKey: parsed });
    });

    it('decodes base64url fields itself when the native parser is missing', async () => {
      credentialsMock.create.mockResolvedValue(fakeCredential());

      await webauthn.create(PUBLIC_KEY_OPTIONS);

      const publicKey = credentialsMock.create.mock.calls[0][0].publicKey;
      expect(Array.from(new Uint8Array(publicKey.challenge))).toEqual([1, 2, 3]);
      expect(Array.from(new Uint8Array(publicKey.user.id))).toEqual([4, 5, 6]);
      expect(Array.from(new Uint8Array(publicKey.excludeCredentials[0].id))).toEqual([7, 8, 9]);
    });

    it('maps a user cancellation to cancelled', async () => {
      for (const name of ['NotAllowedError', 'AbortError']) {
        credentialsMock.create.mockRejectedValueOnce(new DOMException('cancel', name));

        expect(await webauthn.create(PUBLIC_KEY_OPTIONS)).toEqual({ status: 'cancelled' });
      }
    });

    it('maps other browser errors to failed', async () => {
      credentialsMock.create.mockRejectedValue(new DOMException('bad', 'InvalidStateError'));

      expect(await webauthn.create(PUBLIC_KEY_OPTIONS)).toEqual({ status: 'failed' });
    });

    it('maps an empty result to failed', async () => {
      credentialsMock.create.mockResolvedValue(null);

      expect(await webauthn.create(PUBLIC_KEY_OPTIONS)).toEqual({ status: 'failed' });
    });
  });

  describe('get', () => {
    it('asks for conditional mediation when requested', async () => {
      credentialsMock.get.mockResolvedValue(fakeCredential());

      await webauthn.get(REQUEST_OPTIONS, { conditional: true });

      expect(credentialsMock.get.mock.calls[0][0].mediation).toBe('conditional');
    });

    it('passes the abort signal through', async () => {
      credentialsMock.get.mockResolvedValue(fakeCredential());
      const controller = new AbortController();

      await webauthn.get(REQUEST_OPTIONS, { signal: controller.signal });

      expect(credentialsMock.get.mock.calls[0][0].signal).toBe(controller.signal);
    });

    it('returns the assertion as JSON on success', async () => {
      credentialsMock.get.mockResolvedValue(fakeCredential());

      expect(await webauthn.get(REQUEST_OPTIONS)).toEqual({
        status: 'success',
        credential: '{"id":"abc","type":"public-key"}',
      });
    });

    it('maps cancellation and failures', async () => {
      credentialsMock.get.mockRejectedValueOnce(new DOMException('x', 'NotAllowedError'));
      expect(await webauthn.get(REQUEST_OPTIONS)).toEqual({ status: 'cancelled' });

      credentialsMock.get.mockRejectedValueOnce(new TypeError('network'));
      expect(await webauthn.get(REQUEST_OPTIONS)).toEqual({ status: 'failed' });
    });

    it('is unsupported without PublicKeyCredential', async () => {
      vi.stubGlobal('PublicKeyCredential', undefined);

      expect(await webauthn.get(REQUEST_OPTIONS)).toEqual({ status: 'unsupported' });
    });
  });
});
