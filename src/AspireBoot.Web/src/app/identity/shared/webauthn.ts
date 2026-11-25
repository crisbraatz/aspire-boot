/** Result of a WebAuthn ceremony. The credential is the JSON string the API expects. */
export type WebAuthnOutcome =
  { status: 'success'; credential: string } | { status: 'cancelled' | 'failed' | 'unsupported' };

/** Static helpers that recent browsers expose on PublicKeyCredential (not yet in every TypeScript DOM lib). */
interface PublicKeyCredentialStatics {
  parseCreationOptionsFromJSON?: (json: unknown) => PublicKeyCredentialCreationOptions;
  parseRequestOptionsFromJSON?: (json: unknown) => PublicKeyCredentialRequestOptions;
  isConditionalMediationAvailable?: () => Promise<boolean>;
}

interface WireCredentialDescriptor {
  id: string;
  type: 'public-key';
  transports?: AuthenticatorTransport[];
}

interface WireCreationOptions {
  challenge: string;
  user: { id: string; name: string; displayName: string };
  excludeCredentials?: WireCredentialDescriptor[];
}

interface WireRequestOptions {
  challenge: string;
  allowCredentials?: WireCredentialDescriptor[];
}

const statics = (): PublicKeyCredentialStatics =>
  globalThis.PublicKeyCredential as unknown as PublicKeyCredentialStatics;

/** True when the browser has WebAuthn and the Credential Management API. */
export function isSupported(): boolean {
  return typeof PublicKeyCredential !== 'undefined' && !!navigator?.credentials;
}

/** True when conditional UI (passkey autofill) can be used on this browser. */
export async function isConditionalAvailable(): Promise<boolean> {
  if (!isSupported()) return false;
  const check = statics().isConditionalMediationAvailable;
  if (typeof check !== 'function') return false;
  try {
    return await check.call(PublicKeyCredential);
  } catch {
    return false;
  }
}

/** Runs a registration ceremony with the creation options JSON sent by the API. */
export async function create(optionsJson: string): Promise<WebAuthnOutcome> {
  if (!isSupported()) return { status: 'unsupported' };
  try {
    const credential = await navigator.credentials.create({
      publicKey: parseCreationOptions(optionsJson),
    });
    return toOutcome(credential);
  } catch (error) {
    return { status: outcomeForError(error) };
  }
}

/** Runs an authentication ceremony. `conditional` enables passkey autofill; the signal cancels it. */
export async function get(
  optionsJson: string,
  options: { conditional?: boolean; signal?: AbortSignal } = {},
): Promise<WebAuthnOutcome> {
  if (!isSupported()) return { status: 'unsupported' };
  try {
    const credential = await navigator.credentials.get({
      publicKey: parseRequestOptions(optionsJson),
      signal: options.signal,
      ...(options.conditional ? { mediation: 'conditional' as const } : {}),
    });
    return toOutcome(credential);
  } catch (error) {
    return { status: outcomeForError(error) };
  }
}

function toOutcome(credential: Credential | null): WebAuthnOutcome {
  const publicKey = credential as PublicKeyCredential | null;
  if (!publicKey || typeof publicKey.toJSON !== 'function') return { status: 'failed' };
  return { status: 'success', credential: JSON.stringify(publicKey.toJSON()) };
}

/** A user dismissing the prompt or aborting is a cancellation; anything else is a failure. */
function outcomeForError(error: unknown): 'cancelled' | 'failed' {
  if (
    error instanceof DOMException &&
    (error.name === 'NotAllowedError' || error.name === 'AbortError')
  ) {
    return 'cancelled';
  }
  return 'failed';
}

function parseCreationOptions(json: string): PublicKeyCredentialCreationOptions {
  const parse = statics().parseCreationOptionsFromJSON;
  if (typeof parse === 'function') return parse.call(PublicKeyCredential, JSON.parse(json));
  const wire = JSON.parse(json) as WireCreationOptions;
  return {
    ...wire,
    challenge: fromBase64Url(wire.challenge),
    user: { ...wire.user, id: fromBase64Url(wire.user.id) },
    excludeCredentials: wire.excludeCredentials?.map(decodeDescriptor),
  } as PublicKeyCredentialCreationOptions;
}

function parseRequestOptions(json: string): PublicKeyCredentialRequestOptions {
  const parse = statics().parseRequestOptionsFromJSON;
  if (typeof parse === 'function') return parse.call(PublicKeyCredential, JSON.parse(json));
  const wire = JSON.parse(json) as WireRequestOptions;
  return {
    ...wire,
    challenge: fromBase64Url(wire.challenge),
    allowCredentials: wire.allowCredentials?.map(decodeDescriptor),
  } as PublicKeyCredentialRequestOptions;
}

function decodeDescriptor(descriptor: WireCredentialDescriptor): PublicKeyCredentialDescriptor {
  return { ...descriptor, id: fromBase64Url(descriptor.id) };
}

/** Decodes an unpadded base64url string (RFC 4648 section 5) into bytes. */
function fromBase64Url(value: string): ArrayBuffer {
  const base64 = value.replace(/-/g, '+').replace(/_/g, '/');
  const padded = base64.padEnd(Math.ceil(base64.length / 4) * 4, '=');
  const binary = atob(padded);
  const bytes = Uint8Array.from(binary, (char) => char.charCodeAt(0));
  return bytes.buffer;
}
