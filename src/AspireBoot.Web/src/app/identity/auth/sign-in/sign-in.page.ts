import { Component, DestroyRef, inject, signal } from '@angular/core';
import { Router, RouterLink } from '@angular/router';
import { I18n } from '../../../core/i18n/i18n';
import { TranslatePipe } from '../../../core/i18n/translate.pipe';
import { IdentityApi } from '../../shared/identity-api';
import { Notice, noticeFromError, noticeText } from '../../shared/notice';
import { Session } from '../../shared/session';
import * as webauthn from '../../shared/webauthn';

/** Passkey sign-in: an explicit button, plus conditional UI (passkey autofill) when the browser offers it. */
@Component({
  selector: 'ab-sign-in-page',
  imports: [RouterLink, TranslatePipe],
  templateUrl: './sign-in.page.html',
})
export class SignInPage {
  private readonly api = inject(IdentityApi);
  private readonly i18n = inject(I18n);
  private readonly session = inject(Session);
  private readonly router = inject(Router);
  private readonly destroyRef = inject(DestroyRef);
  private conditional: AbortController | undefined;

  protected readonly supported = webauthn.isSupported();
  protected readonly keepSignedIn = signal(false);
  protected readonly busy = signal(false);
  protected readonly notice = signal<Notice | null>(null);

  constructor() {
    this.destroyRef.onDestroy(() => this.conditional?.abort());
    if (this.supported) void this.startConditionalUi();
  }

  protected async signInWithPasskey(event?: Event): Promise<void> {
    event?.preventDefault();
    if (this.busy()) return;
    this.conditional?.abort();
    this.busy.set(true);
    this.notice.set(null);
    try {
      const options = await this.api.beginSignIn();
      const outcome = await webauthn.get(options);
      switch (outcome.status) {
        case 'success':
          await this.complete(outcome.credential);
          break;
        case 'cancelled':
          this.notice.set({ id: 'auth.signIn.cancelled' });
          break;
        case 'unsupported':
          this.notice.set({ id: 'auth.unsupported' });
          break;
        case 'failed':
          this.notice.set({ id: 'errors.signInFailed' });
          break;
      }
    } catch (error) {
      this.notice.set(noticeFromError(error));
    } finally {
      this.busy.set(false);
    }
  }

  protected onKeepSignedInChange(event: Event): void {
    this.keepSignedIn.set((event.target as HTMLInputElement).checked);
  }

  protected text(notice: Notice): string {
    return noticeText(this.i18n, notice);
  }

  /** Autofill sign-in. Failures stay silent: the explicit button is the fallback and reports errors. */
  private async startConditionalUi(): Promise<void> {
    if (!(await webauthn.isConditionalAvailable())) return;
    const controller = new AbortController();
    this.conditional = controller;
    try {
      const options = await this.api.beginSignIn();
      const outcome = await webauthn.get(options, {
        conditional: true,
        signal: controller.signal,
      });
      if (outcome.status === 'success') await this.complete(outcome.credential);
    } catch {
      // Conditional UI is best effort; the explicit button stays available.
    }
  }

  private async complete(credential: string): Promise<void> {
    try {
      await this.session.signIn(credential, this.keepSignedIn());
      await this.router.navigateByUrl('/account');
    } catch (error) {
      this.notice.set(noticeFromError(error));
    }
  }
}
