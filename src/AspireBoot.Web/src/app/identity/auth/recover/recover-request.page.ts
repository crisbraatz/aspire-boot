import { Component, inject, signal } from '@angular/core';
import { FormField, email, form, maxLength, required, submit } from '@angular/forms/signals';
import { I18n } from '../../../core/i18n/i18n';
import { TranslatePipe } from '../../../core/i18n/translate.pipe';
import { IdentityApi } from '../../shared/identity-api';
import { Notice, noticeFromError, noticeText } from '../../shared/notice';

const EMAIL_MAX_LENGTH = 256;

const ERROR_IDS: Readonly<Record<string, string>> = {
  required: 'auth.validation.required',
  email: 'auth.validation.email',
  maxLength: 'auth.validation.maxLength',
};

interface RecoverModel {
  email: string;
}

/** Asks for a recovery link. The answer is the same for every address, so it never reveals whether an account exists. */
@Component({
  selector: 'ab-recover-request-page',
  imports: [FormField, TranslatePipe],
  templateUrl: './recover-request.page.html',
})
export class RecoverRequestPage {
  private readonly api = inject(IdentityApi);
  private readonly i18n = inject(I18n);

  private readonly model = signal<RecoverModel>({ email: '' });
  protected readonly recoverForm = form(this.model, (path) => {
    required(path.email);
    maxLength(path.email, EMAIL_MAX_LENGTH);
    email(path.email);
  });

  protected readonly busy = signal(false);
  protected readonly sent = signal(false);
  protected readonly notice = signal<Notice | null>(null);

  protected async onSubmit(event: Event): Promise<void> {
    event.preventDefault();
    this.notice.set(null);
    await submit(this.recoverForm, async () => {
      this.busy.set(true);
      try {
        await this.api.requestRecovery(this.model().email);
        this.sent.set(true);
      } catch (error) {
        this.notice.set(noticeFromError(error));
      } finally {
        this.busy.set(false);
      }
      return undefined;
    });
  }

  /** The first validation message for a touched field, or null. Looked up on every render so it follows the language. */
  protected errorFor(state: {
    touched(): boolean;
    errors(): readonly { readonly kind: string }[];
  }): string | null {
    const error = state.errors()[0];
    if (!state.touched() || !error) return null;
    const params = error.kind === 'maxLength' ? { max: EMAIL_MAX_LENGTH } : undefined;
    return this.i18n.t(ERROR_IDS[error.kind] ?? 'errors.unexpected', params);
  }

  protected text(notice: Notice): string {
    return noticeText(this.i18n, notice);
  }
}
