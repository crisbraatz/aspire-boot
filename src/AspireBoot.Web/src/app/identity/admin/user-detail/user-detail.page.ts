import {
  Component,
  ElementRef,
  computed,
  effect,
  inject,
  input,
  signal,
  untracked,
  viewChild,
} from '@angular/core';
import { RouterLink } from '@angular/router';
import { ApiProblem } from '../../../core/http/problem.interceptor';
import { I18n } from '../../../core/i18n/i18n';
import { TranslatePipe } from '../../../core/i18n/translate.pipe';
import { Avatar } from '../../../shared/ui/avatar/avatar';
import { Session } from '../../shared/session';
import { Notice, noticeFromError, noticeText } from '../../shared/notice';
import { AdminApi, RoleName, UserDetail } from '../shared/admin-api';

/** Role-change problem codes from the API mapped to message ids. */
const ROLE_ERROR_IDS: Readonly<Record<string, string>> = {
  'identity.selfDemotion': 'errors.selfDemotion',
  'identity.lastAdmin': 'errors.lastAdmin',
  'identity.invalidRole': 'errors.invalidRole',
};

/**
 * Shows one user and promotes or demotes them. Role changes go through a native modal dialog: the browser
 * traps focus inside it, Escape closes it, and focus returns to the button that opened it.
 */
@Component({
  selector: 'ab-user-detail-page',
  imports: [Avatar, RouterLink, TranslatePipe],
  templateUrl: './user-detail.page.html',
})
export class UserDetailPage {
  private readonly api = inject(AdminApi);
  private readonly i18n = inject(I18n);
  private readonly session = inject(Session);
  private readonly dialogRef = viewChild<ElementRef<HTMLDialogElement>>('confirmDialog');

  /** The user id from the route (withComponentInputBinding). */
  readonly id = input.required<string>();

  protected readonly user = signal<UserDetail | null>(null);
  protected readonly loading = signal(true);
  protected readonly busy = signal(false);
  protected readonly notice = signal<Notice | null>(null);
  protected readonly status = signal<Notice | null>(null);

  /** The role the dialog would switch to: Admin becomes User and anyone else becomes Admin. */
  protected readonly targetRole = computed<RoleName>(() =>
    this.user()?.role === 'Admin' ? 'User' : 'Admin',
  );
  protected readonly isSelf = computed(
    () => this.user() !== null && this.user()?.id === this.session.currentUser()?.id,
  );
  /** An Admin cannot remove their own Admin role, so the button is hidden for them. */
  protected readonly canChangeRole = computed(
    () => this.user() !== null && !(this.isSelf() && this.user()?.role === 'Admin'),
  );
  protected readonly selfAdmin = computed(() => this.isSelf() && this.user()?.role === 'Admin');

  constructor() {
    effect(() => {
      const id = this.id();
      untracked(() => void this.load(id));
    });
  }

  protected askChange(): void {
    this.notice.set(null);
    this.status.set(null);
    this.dialogRef()?.nativeElement.showModal();
  }

  protected cancelChange(): void {
    this.dialogRef()?.nativeElement.close();
  }

  protected async confirmChange(): Promise<void> {
    if (this.busy()) return;
    const current = this.user();
    if (!current) return;
    this.busy.set(true);
    this.notice.set(null);
    try {
      await this.api.changeRole(current.id, this.targetRole());
      this.status.set({ id: 'admin.user.roleChanged' });
      this.user.set(await this.api.get(current.id));
    } catch (error) {
      this.notice.set(this.errorNotice(error));
    } finally {
      this.busy.set(false);
      this.dialogRef()?.nativeElement.close();
    }
  }

  protected roleLabelId(role: RoleName): string {
    return `admin.users.role.${role.toLowerCase()}`;
  }

  protected dialogTitleId(): string {
    return this.targetRole() === 'Admin'
      ? 'admin.user.dialog.promoteTitle'
      : 'admin.user.dialog.demoteTitle';
  }

  protected dialogTextId(): string {
    return this.targetRole() === 'Admin'
      ? 'admin.user.dialog.promoteText'
      : 'admin.user.dialog.demoteText';
  }

  protected changeLabelId(): string {
    return this.targetRole() === 'Admin'
      ? 'admin.user.changeRole.promote'
      : 'admin.user.changeRole.demote';
  }

  protected confirmLabelId(): string {
    return this.targetRole() === 'Admin'
      ? 'admin.user.dialog.confirmPromote'
      : 'admin.user.dialog.confirmDemote';
  }

  protected emailStatusId(user: UserDetail): string {
    return user.emailConfirmed ? 'admin.users.emailConfirmed' : 'admin.users.emailPending';
  }

  protected joinedOn(user: UserDetail): string {
    return new Intl.DateTimeFormat(this.i18n.culture(), { dateStyle: 'medium' }).format(
      new Date(user.createdAt),
    );
  }

  protected text(notice: Notice): string {
    return noticeText(this.i18n, notice);
  }

  private async load(id: string): Promise<void> {
    this.loading.set(true);
    this.notice.set(null);
    try {
      this.user.set(await this.api.get(id));
    } catch (error) {
      this.user.set(null);
      this.notice.set(this.errorNotice(error));
    } finally {
      this.loading.set(false);
    }
  }

  /** Maps role-change and not-found problems to localized messages; everything else uses the shared mapping. */
  private errorNotice(error: unknown): Notice {
    if (error instanceof ApiProblem) {
      if (error.status === 404) return { id: 'admin.user.notFound' };
      const id = error.code ? ROLE_ERROR_IDS[error.code] : undefined;
      if (id) return { id };
    }
    return noticeFromError(error);
  }
}
