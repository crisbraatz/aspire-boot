import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import type { Culture } from '../../core/i18n/supported-cultures';

/** Mirrors the Identity API contracts under /api/identity (see docs/adr/0003 and the feature plan). */
export type RegistrationPurpose = 'confirm' | 'recover';

export interface CurrentUser {
  id: string;
  displayName: string;
  email: string;
  culture: Culture;
  /** Versioned avatar URL, or null when the user has no avatar. */
  avatarUrl: string | null;
}

export interface AvatarUploadResult {
  avatarUrl: string;
}

export interface PasskeyInfo {
  /** base64url credential id. */
  id: string;
  name: string;
  createdAt: string;
  lastUsedAt: string | null;
}

export interface SignUpRequest {
  displayName: string;
  email: string;
  culture: Culture;
}

export interface RegistrationRequest {
  userId: string;
  token: string;
  purpose: RegistrationPurpose;
}

export interface CompleteRegistrationRequest extends RegistrationRequest {
  /** The credential JSON produced by webauthn.create(). */
  credential: string;
  name: string;
}

const BASE = '/api/identity';

/**
 * Typed client for the Identity endpoints. Options endpoints return the WebAuthn options JSON as text,
 * which is passed unchanged to webauthn.ts. Mutations carry X-XSRF-TOKEN through HttpClient's XSRF interceptor.
 */
@Injectable({ providedIn: 'root' })
export class IdentityApi {
  private readonly http = inject(HttpClient);

  signUp(request: SignUpRequest): Promise<void> {
    return this.send<void>('POST', `${BASE}/auth/signup`, request);
  }

  resendConfirmation(email: string): Promise<void> {
    return this.send<void>('POST', `${BASE}/auth/confirmation/resend`, { email });
  }

  beginRegistration(request: RegistrationRequest): Promise<string> {
    return this.text('POST', `${BASE}/auth/registration/options`, request);
  }

  completeRegistration(request: CompleteRegistrationRequest): Promise<void> {
    return this.send<void>('POST', `${BASE}/auth/registration`, request);
  }

  beginSignIn(): Promise<string> {
    return this.text('POST', `${BASE}/auth/signin/options`, {});
  }

  signIn(credential: string, keepSignedIn: boolean): Promise<void> {
    return this.send<void>('POST', `${BASE}/auth/signin`, { credential, keepSignedIn });
  }

  signOut(): Promise<void> {
    return this.send<void>('POST', `${BASE}/auth/signout`, {});
  }

  requestRecovery(email: string): Promise<void> {
    return this.send<void>('POST', `${BASE}/auth/recovery`, { email });
  }

  me(): Promise<CurrentUser> {
    return this.send<CurrentUser>('GET', `${BASE}/account/me`);
  }

  updateProfile(displayName: string): Promise<void> {
    return this.send<void>('PUT', `${BASE}/account/profile`, { displayName });
  }

  /** Uploads the avatar as multipart form data; the server answers with the new avatar URL. */
  uploadAvatar(file: File): Promise<AvatarUploadResult> {
    const body = new FormData();
    body.append('file', file);
    return this.send<AvatarUploadResult>('PUT', `${BASE}/account/avatar`, body);
  }

  removeAvatar(): Promise<void> {
    return this.send<void>('DELETE', `${BASE}/account/avatar`);
  }

  updatePreferences(culture: Culture): Promise<void> {
    return this.send<void>('PUT', `${BASE}/account/preferences`, { culture });
  }

  listPasskeys(): Promise<PasskeyInfo[]> {
    return this.send<PasskeyInfo[]>('GET', `${BASE}/account/passkeys`);
  }

  beginAddPasskey(): Promise<string> {
    return this.text('POST', `${BASE}/account/passkeys/options`, {});
  }

  addPasskey(credential: string, name: string): Promise<void> {
    return this.send<void>('POST', `${BASE}/account/passkeys`, { credential, name });
  }

  renamePasskey(id: string, name: string): Promise<void> {
    return this.send<void>('PATCH', this.passkeyUrl(id), { name });
  }

  removePasskey(id: string): Promise<void> {
    return this.send<void>('DELETE', this.passkeyUrl(id));
  }

  private passkeyUrl(id: string): string {
    return `${BASE}/account/passkeys/${encodeURIComponent(id)}`;
  }

  private send<T>(method: string, url: string, body?: unknown): Promise<T> {
    return firstValueFrom(this.http.request<T>(method, url, { body: body ?? null }));
  }

  private text(method: string, url: string, body: unknown): Promise<string> {
    return firstValueFrom(this.http.request(method, url, { body, responseType: 'text' }));
  }
}
