import { DOCUMENT } from '@angular/common';
import { Component, inject, signal } from '@angular/core';
import { Router, RouterLink } from '@angular/router';
import { ApiProblem } from '../../../core/http/problem.interceptor';
import { I18n } from '../../../core/i18n/i18n';
import { TranslatePipe } from '../../../core/i18n/translate.pipe';
import { IdentityApi } from '../../shared/identity-api';
import { Notice, noticeFromError, noticeText } from '../../shared/notice';
import { Session } from '../../shared/session';
import * as webauthn from '../../shared/webauthn';

type RecoverState = 'ready' | 'working' | 'done' | 'invalid' | 'unsupported';

interface RecoveryLink {
  readonly userId: string;
  readonly token: string;
}

/** Opened from the recovery email: creates a new passkey (which ends the other sessions) and signs the user in. */
@Component({
  selector: 'ab-recover-complete-page',
  imports: [RouterLink, TranslatePipe],
  templateUrl: './recover-complete.page.html',
})
export class RecoverCompletePage {
  private readonly api = inject(IdentityApi);
  private readonly i18n = inject(I18n);
  private readonly session = inject(Session);
  private readonly router = inject(Router);
  private readonly document = inject(DOCUMENT);
  private readonly link: RecoveryLink | null;

  protected readonly state = signal<RecoverState>('ready');
  protected readonly notice = signal<Notice | null>(null);

  constructor() {
    this.link = this.readLinkAndClearUrl();
    if (!webauthn.isSupported()) this.state.set('unsupported');
    else if (!this.link) this.state.set('invalid');
  }

  protected async createPasskey(): Promise<void> {
    const link = this.link;
    if (!link || this.state() === 'working') return;
    this.state.set('working');
    this.notice.set(null);
    try {
      const options = await this.api.beginRegistration({ ...link, purpose: 'recover' });
      const outcome = await webauthn.create(options);
      switch (outcome.status) {
        case 'success':
          break;
        case 'unsupported':
          this.state.set('unsupported');
          return;
        case 'cancelled':
          this.state.set('ready');
          this.notice.set({ id: 'auth.confirm.cancelled' });
          return;
        case 'failed':
          this.state.set('ready');
          this.notice.set({ id: 'errors.passkeyFailed' });
          return;
      }
      await this.api.completeRegistration({
        ...link,
        purpose: 'recover',
        credential: outcome.credential,
        name: this.i18n.t('auth.confirm.defaultPasskeyName'),
      });
      await this.session.refresh();
      this.state.set('done');
      await this.router.navigateByUrl('/account');
    } catch (error) {
      if (error instanceof ApiProblem && error.code === 'identity.invalidLink') {
        this.state.set('invalid');
        return;
      }
      this.state.set('ready');
      this.notice.set(noticeFromError(error));
    }
  }

  protected text(notice: Notice): string {
    return noticeText(this.i18n, notice);
  }

  /** Reads the one-time token from the query, then removes it from the address bar and history entry. */
  private readLinkAndClearUrl(): RecoveryLink | null {
    const view = this.document.defaultView;
    if (!view) return null;
    const url = new URL(view.location.href);
    const userId = url.searchParams.get('u');
    const token = url.searchParams.get('t');
    if (url.search) {
      url.search = '';
      view.history.replaceState(view.history.state, '', url);
    }
    return userId && token ? { userId, token } : null;
  }
}
