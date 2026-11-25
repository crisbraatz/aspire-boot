import { Component, DestroyRef, inject, signal } from '@angular/core';
import { FormField, email, form, maxLength, required, submit } from '@angular/forms/signals';
import { RouterLink } from '@angular/router';
import { I18n } from '../../../core/i18n/i18n';
import { TranslatePipe } from '../../../core/i18n/translate.pipe';
import { IdentityApi } from '../../shared/identity-api';
import { Notice, noticeFromError, noticeText } from '../../shared/notice';

/** Mirrors the API limit: the same address may ask for another email after 60 seconds. */
const RESEND_COOLDOWN_SECONDS = 60;
const NAME_MAX_LENGTH = 100;

const ERROR_IDS: Readonly<Record<string, string>> = {
  required: 'auth.validation.required',
  email: 'auth.validation.email',
  maxLength: 'auth.validation.maxLength',
};

interface SignUpModel {
  displayName: string;
  email: string;
}

/** Creates an account. The response is always the same, so the page never reveals whether an email is registered. */
@Component({
  selector: 'ab-sign-up-page',
  imports: [FormField, RouterLink, TranslatePipe],
  templateUrl: './sign-up.page.html',
})
export class SignUpPage {
  private readonly api = inject(IdentityApi);
  private readonly i18n = inject(I18n);
  private readonly destroyRef = inject(DestroyRef);
  private cooldownTimer: ReturnType<typeof setInterval> | undefined;

  private readonly model = signal<SignUpModel>({ displayName: '', email: '' });
  protected readonly signUpForm = form(this.model, (path) => {
    required(path.displayName);
    maxLength(path.displayName, NAME_MAX_LENGTH);
    required(path.email);
    email(path.email);
  });

  protected readonly busy = signal(false);
  protected readonly sent = signal(false);
  protected readonly resent = signal(false);
  protected readonly cooldown = signal(0);
  protected readonly notice = signal<Notice | null>(null);

  constructor() {
    this.destroyRef.onDestroy(() => clearInterval(this.cooldownTimer));
  }

  protected async onSubmit(event: Event): Promise<void> {
    event.preventDefault();
    this.notice.set(null);
    await submit(this.signUpForm, async () => {
      this.busy.set(true);
      try {
        const { displayName, email: address } = this.model();
        await this.api.signUp({ displayName, email: address, culture: this.i18n.culture() });
        this.resent.set(false);
        this.sent.set(true);
        this.startCooldown();
      } catch (error) {
        this.notice.set(noticeFromError(error));
      } finally {
        this.busy.set(false);
      }
      return undefined;
    });
  }

  protected async resend(): Promise<void> {
    this.notice.set(null);
    try {
      await this.api.resendConfirmation(this.model().email);
      this.resent.set(true);
      this.startCooldown();
    } catch (error) {
      this.notice.set(noticeFromError(error));
    }
  }

  /** The first validation message for a touched field, or null. Messages are looked up on every render, so they follow the language. */
  protected errorFor(state: {
    touched(): boolean;
    errors(): readonly { readonly kind: string }[];
  }): string | null {
    const error = state.errors()[0];
    if (!state.touched() || !error) return null;
    const params = error.kind === 'maxLength' ? { max: NAME_MAX_LENGTH } : undefined;
    return this.i18n.t(ERROR_IDS[error.kind] ?? 'errors.unexpected', params);
  }

  protected text(notice: Notice): string {
    return noticeText(this.i18n, notice);
  }

  private startCooldown(): void {
    clearInterval(this.cooldownTimer);
    this.cooldown.set(RESEND_COOLDOWN_SECONDS);
    this.cooldownTimer = setInterval(() => {
      this.cooldown.update((seconds) => seconds - 1);
      if (this.cooldown() <= 0) clearInterval(this.cooldownTimer);
    }, 1000);
  }
}
