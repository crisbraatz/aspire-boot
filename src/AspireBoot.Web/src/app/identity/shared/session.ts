import { Injectable, computed, inject, signal } from '@angular/core';
import { ApiProblem } from '../../core/http/problem.interceptor';
import { I18n } from '../../core/i18n/i18n';
import { Culture, isSupportedCulture } from '../../core/i18n/supported-cultures';
import { CurrentUser, IdentityApi } from './identity-api';

/** Holds the signed-in user for the SPA. The API is the source of truth; this is only UI state. */
@Injectable({ providedIn: 'root' })
export class Session {
  private readonly api = inject(IdentityApi);
  private readonly i18n = inject(I18n);
  private readonly user = signal<CurrentUser | null>(null);
  private readonly loaded = signal(false);

  /** The signed-in user, or null when anonymous. */
  readonly currentUser = this.user.asReadonly();
  readonly isSignedIn = computed(() => this.user() !== null);
  /** True once GET me has answered at least once (signed in or anonymous). */
  readonly isLoaded = this.loaded.asReadonly();
  /** True when the signed-in user has the Admin role. Only a UX hint; the API enforces it. */
  readonly isAdmin = computed(() => this.user()?.roles.includes('Admin') ?? false);

  /**
   * Loads the current user. A 401 means anonymous; other failures are rethrown and keep the previous state.
   * The server culture wins: when it differs from the local one, it is applied and persisted.
   */
  async refresh(): Promise<CurrentUser | null> {
    try {
      const user = await this.api.me();
      this.user.set(user);
      await this.applyCulture(user.culture);
    } catch (error) {
      if (!(error instanceof ApiProblem && error.status === 401)) throw error;
      this.user.set(null);
    }
    this.loaded.set(true);
    return this.user();
  }

  /**
   * Switches the UI language. A signed-in user also saves it on the account, in the background:
   * a failed save keeps the new language on this device and leaves the account value unchanged.
   */
  async changeCulture(culture: Culture): Promise<void> {
    await this.i18n.use(culture).catch(() => undefined);
    if (!this.isSignedIn()) return;
    this.api
      .updatePreferences(culture)
      .then(() => this.user.update((user) => user && { ...user, culture }))
      .catch(() => undefined);
  }

  /**
   * Merges fields into the signed-in user so the header updates at once, after a profile or avatar change.
   * It does not call the server and does nothing while anonymous.
   */
  patchUser(partial: Partial<CurrentUser>): void {
    this.user.update((user) => user && { ...user, ...partial });
  }

  /** Posts the passkey assertion and then reloads the user, so the session state matches the cookie. */
  async signIn(credential: string, keepSignedIn: boolean): Promise<CurrentUser | null> {
    await this.api.signIn(credential, keepSignedIn);
    return this.refresh();
  }

  /** Drops the signed-in state without calling the server, after the server ended the session (a 401). */
  markSignedOut(): void {
    this.user.set(null);
    this.loaded.set(true);
  }

  async signOut(): Promise<void> {
    await this.api.signOut();
    this.user.set(null);
    this.loaded.set(true);
  }

  private async applyCulture(culture: Culture): Promise<void> {
    if (!isSupportedCulture(culture) || culture === this.i18n.culture()) return;
    await this.i18n.use(culture).catch(() => undefined);
  }
}
