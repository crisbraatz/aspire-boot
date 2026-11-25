import { Component, computed, inject, signal } from '@angular/core';
import { FormField, form, maxLength, required, submit } from '@angular/forms/signals';
import { I18n } from '../../../core/i18n/i18n';
import { TranslatePipe } from '../../../core/i18n/translate.pipe';
import { IdentityApi, PasskeyInfo } from '../../shared/identity-api';
import { Notice, noticeFromError, noticeText } from '../../shared/notice';
import * as webauthn from '../../shared/webauthn';

const NAME_MAX_LENGTH = 100;

const ERROR_IDS: Readonly<Record<string, string>> = {
  required: 'auth.validation.required',
  maxLength: 'auth.validation.maxLength',
};

interface RenameModel {
  name: string;
}

/** Lists the signed-in user's passkeys and manages them. The API refuses to remove the last one. */
@Component({
  selector: 'ab-passkeys-page',
  imports: [FormField, TranslatePipe],
  templateUrl: './passkeys.page.html',
})
export class PasskeysPage {
  private readonly api = inject(IdentityApi);
  private readonly i18n = inject(I18n);

  protected readonly passkeys = signal<PasskeyInfo[]>([]);
  protected readonly loading = signal(true);
  protected readonly busy = signal(false);
  protected readonly notice = signal<Notice | null>(null);
  protected readonly status = signal<Notice | null>(null);
  protected readonly editingId = signal<string | null>(null);
  protected readonly confirmingId = signal<string | null>(null);
  /** With one passkey left, removal is disabled and the page explains why. */
  protected readonly onlyOne = computed(() => this.passkeys().length <= 1);

  private readonly renameModel = signal<RenameModel>({ name: '' });
  protected readonly renameForm = form(this.renameModel, (path) => {
    required(path.name);
    maxLength(path.name, NAME_MAX_LENGTH);
  });

  constructor() {
    void this.load();
  }

  protected async addPasskey(): Promise<void> {
    if (this.busy()) return;
    this.busy.set(true);
    this.notice.set(null);
    this.status.set(null);
    try {
      const options = await this.api.beginAddPasskey();
      const outcome = await webauthn.create(options);
      switch (outcome.status) {
        case 'success':
          break;
        case 'unsupported':
          this.notice.set({ id: 'auth.unsupported' });
          return;
        case 'cancelled':
          this.notice.set({ id: 'auth.confirm.cancelled' });
          return;
        case 'failed':
          this.notice.set({ id: 'errors.passkeyFailed' });
          return;
      }
      await this.api.addPasskey(outcome.credential, this.i18n.t('auth.confirm.defaultPasskeyName'));
      this.status.set({ id: 'account.passkeys.added' });
      await this.load();
    } catch (error) {
      this.notice.set(noticeFromError(error));
    } finally {
      this.busy.set(false);
    }
  }

  protected startRename(passkey: PasskeyInfo): void {
    this.notice.set(null);
    this.status.set(null);
    this.confirmingId.set(null);
    this.renameModel.set({ name: passkey.name });
    this.editingId.set(passkey.id);
  }

  protected cancelRename(): void {
    this.editingId.set(null);
  }

  protected async saveRename(event: Event, id: string): Promise<void> {
    event.preventDefault();
    this.notice.set(null);
    await submit(this.renameForm, async () => {
      try {
        await this.api.renamePasskey(id, this.renameModel().name);
        this.editingId.set(null);
        this.status.set({ id: 'account.passkeys.renamed' });
        await this.load();
      } catch (error) {
        this.notice.set(noticeFromError(error));
      }
      return undefined;
    });
  }

  protected askRemove(passkey: PasskeyInfo): void {
    this.notice.set(null);
    this.status.set(null);
    this.editingId.set(null);
    this.confirmingId.set(passkey.id);
  }

  protected cancelRemove(): void {
    this.confirmingId.set(null);
  }

  protected async confirmRemove(id: string): Promise<void> {
    if (this.busy()) return;
    this.busy.set(true);
    this.notice.set(null);
    try {
      await this.api.removePasskey(id);
      this.confirmingId.set(null);
      this.status.set({ id: 'account.passkeys.removed' });
      await this.load();
    } catch (error) {
      this.notice.set(noticeFromError(error));
    } finally {
      this.busy.set(false);
    }
  }

  /** The first validation message for a touched field, or null. Looked up on every render so it follows the language. */
  protected errorFor(state: {
    touched(): boolean;
    errors(): readonly { readonly kind: string }[];
  }): string | null {
    const error = state.errors()[0];
    if (!state.touched() || !error) return null;
    const params = error.kind === 'maxLength' ? { max: NAME_MAX_LENGTH } : undefined;
    return this.i18n.t(ERROR_IDS[error.kind] ?? 'errors.unexpected', params);
  }

  /** Only the creation date is shown: the API keeps no last-used time for passkeys. */
  protected createdOn(passkey: PasskeyInfo): string {
    return new Intl.DateTimeFormat(this.i18n.culture(), { dateStyle: 'medium' }).format(
      new Date(passkey.createdAt),
    );
  }

  protected text(notice: Notice): string {
    return noticeText(this.i18n, notice);
  }

  private async load(): Promise<void> {
    try {
      this.passkeys.set(await this.api.listPasskeys());
    } catch (error) {
      this.notice.set(noticeFromError(error));
    } finally {
      this.loading.set(false);
    }
  }
}
