import { Component, ElementRef, computed, inject, signal, viewChild } from '@angular/core';
import { FormField, form, maxLength, minLength, required, submit } from '@angular/forms/signals';
import { Router, RouterLink } from '@angular/router';
import { ApiProblem } from '../../../core/http/problem.interceptor';
import { I18n } from '../../../core/i18n/i18n';
import { TranslatePipe } from '../../../core/i18n/translate.pipe';
import {
  Culture,
  SUPPORTED_CULTURES,
  isSupportedCulture,
} from '../../../core/i18n/supported-cultures';
import { Avatar } from '../../../shared/ui/avatar/avatar';
import { IdentityApi } from '../../shared/identity-api';
import { Notice, noticeFromError, noticeText } from '../../shared/notice';
import { Session } from '../../shared/session';

const NAME_MIN_LENGTH = 2;
const NAME_MAX_LENGTH = 100;
const MAX_AVATAR_BYTES = 5 * 1024 * 1024;
const AVATAR_TYPES: readonly string[] = ['image/jpeg', 'image/png', 'image/webp'];

const NAME_ERROR_IDS: Readonly<Record<string, string>> = {
  required: 'auth.validation.required',
  minLength: 'account.profile.name.length',
  maxLength: 'account.profile.name.length',
};

/** Problem Details codes from the avatar endpoints mapped to message ids. */
const AVATAR_ERROR_IDS: Readonly<Record<string, string>> = {
  'avatar.unsupportedType': 'account.profile.avatar.unsupported',
  'avatar.corrupt': 'account.profile.avatar.corrupt',
  'avatar.dimensionsTooLarge': 'account.profile.avatar.dimensions',
  'avatar.missing': 'account.profile.avatar.missing',
  'avatar.tooLarge': 'account.profile.avatar.tooLarge',
  'avatar.busy': 'account.profile.avatar.busy',
  'avatar.storageUnavailable': 'account.profile.avatar.storageUnavailable',
};

interface ProfileModel {
  displayName: string;
}

/**
 * "My account": edits the display name, shows the email read-only, manages the profile picture,
 * switches the language and links to passkey management. The API stays the source of truth; the
 * session is patched after each successful save so the header updates at once.
 */
@Component({
  selector: 'ab-account-home-page',
  imports: [Avatar, FormField, RouterLink, TranslatePipe],
  templateUrl: './account-home.page.html',
})
export class AccountHomePage {
  protected readonly session = inject(Session);
  protected readonly i18n = inject(I18n);
  private readonly api = inject(IdentityApi);
  private readonly router = inject(Router);
  private readonly avatarInput = viewChild<ElementRef<HTMLInputElement>>('avatarInput');

  protected readonly cultures = SUPPORTED_CULTURES;
  protected readonly maxAvatarMegabytes = MAX_AVATAR_BYTES / (1024 * 1024);
  protected readonly avatarTypes = AVATAR_TYPES.join(',');

  private readonly profileModel = signal<ProfileModel>({
    displayName: this.session.currentUser()?.displayName ?? '',
  });
  protected readonly profileForm = form(this.profileModel, (path) => {
    required(path.displayName);
    minLength(path.displayName, NAME_MIN_LENGTH);
    maxLength(path.displayName, NAME_MAX_LENGTH);
  });
  protected readonly profileBusy = signal(false);
  protected readonly profileStatus = signal<Notice | null>(null);
  protected readonly profileNotice = signal<Notice | null>(null);

  protected readonly avatarBusy = signal(false);
  protected readonly avatarDragging = signal(false);
  protected readonly avatarStatus = signal<Notice | null>(null);
  protected readonly avatarNotice = signal<Notice | null>(null);
  protected readonly confirmingRemove = signal(false);
  protected readonly hasAvatar = computed(() => this.session.currentUser()?.avatarUrl != null);

  protected async saveProfile(event: Event): Promise<void> {
    event.preventDefault();
    this.profileNotice.set(null);
    this.profileStatus.set(null);
    await submit(this.profileForm, async () => {
      this.profileBusy.set(true);
      try {
        const displayName = this.profileModel().displayName;
        await this.api.updateProfile(displayName);
        this.session.patchUser({ displayName });
        this.profileStatus.set({ id: 'account.profile.saved' });
      } catch (error) {
        this.profileNotice.set(noticeFromError(error));
      } finally {
        this.profileBusy.set(false);
      }
      return undefined;
    });
  }

  /** The first validation message for a touched field, or null. Looked up on each render so it follows the language. */
  protected nameError(): string | null {
    const field = this.profileForm.displayName;
    const error = field().errors()[0];
    if (!field().touched() || !error) return null;
    const params =
      error.kind === 'minLength' || error.kind === 'maxLength'
        ? { min: NAME_MIN_LENGTH, max: NAME_MAX_LENGTH }
        : undefined;
    return this.i18n.t(NAME_ERROR_IDS[error.kind] ?? 'errors.unexpected', params);
  }

  protected openPicker(event: Event): void {
    event.preventDefault();
    this.avatarInput()?.nativeElement.click();
  }

  protected onFileChosen(event: Event): void {
    const input = event.target as HTMLInputElement;
    const file = input.files?.[0];
    input.value = '';
    if (file) void this.uploadAvatar(file);
  }

  protected onDragOver(event: DragEvent): void {
    event.preventDefault();
    this.avatarDragging.set(true);
  }

  protected onDragLeave(): void {
    this.avatarDragging.set(false);
  }

  protected onDrop(event: DragEvent): void {
    event.preventDefault();
    this.avatarDragging.set(false);
    if (this.avatarBusy()) return;
    const file = event.dataTransfer?.files?.[0];
    if (file) void this.uploadAvatar(file);
  }

  protected askRemoveAvatar(): void {
    this.avatarNotice.set(null);
    this.avatarStatus.set(null);
    this.confirmingRemove.set(true);
  }

  protected cancelRemoveAvatar(): void {
    this.confirmingRemove.set(false);
  }

  protected async removeAvatar(): Promise<void> {
    this.avatarBusy.set(true);
    this.avatarNotice.set(null);
    try {
      await this.api.removeAvatar();
      this.session.patchUser({ avatarUrl: null });
      this.confirmingRemove.set(false);
      this.avatarStatus.set({ id: 'account.profile.avatar.removed' });
    } catch (error) {
      this.avatarNotice.set(this.avatarNoticeFor(error));
    } finally {
      this.avatarBusy.set(false);
    }
  }

  protected changeCulture(event: Event): void {
    const value = (event.target as HTMLSelectElement).value;
    if (isSupportedCulture(value)) void this.session.changeCulture(value as Culture);
  }

  protected cultureLabel(culture: Culture): string {
    return this.i18n.t(`account.profile.culture.${culture}`);
  }

  protected text(notice: Notice): string {
    return noticeText(this.i18n, notice);
  }

  private async uploadAvatar(file: File): Promise<void> {
    this.avatarStatus.set(null);
    this.avatarNotice.set(null);
    const clientError = this.clientAvatarError(file);
    if (clientError) {
      this.avatarNotice.set({ id: clientError });
      return;
    }
    this.avatarBusy.set(true);
    try {
      const result = await this.api.uploadAvatar(file);
      this.session.patchUser({ avatarUrl: result.avatarUrl });
      this.avatarStatus.set({ id: 'account.profile.avatar.uploaded' });
    } catch (error) {
      this.avatarNotice.set(this.avatarNoticeFor(error));
    } finally {
      this.avatarBusy.set(false);
    }
  }

  /** Checked before any request. The server enforces the same rules again. */
  private clientAvatarError(file: File): string | null {
    if (!AVATAR_TYPES.includes(file.type)) return 'account.profile.avatar.unsupported';
    if (file.size > MAX_AVATAR_BYTES) return 'account.profile.avatar.tooLarge';
    return null;
  }

  private avatarNoticeFor(error: unknown): Notice {
    if (error instanceof ApiProblem && error.code && AVATAR_ERROR_IDS[error.code]) {
      return { id: AVATAR_ERROR_IDS[error.code] };
    }
    return noticeFromError(error);
  }

  protected async signOut(): Promise<void> {
    await this.session.signOut();
    await this.router.navigateByUrl('/');
  }
}
