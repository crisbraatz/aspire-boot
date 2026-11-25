import { Component, DestroyRef, computed, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { I18n } from '../../../core/i18n/i18n';
import { TranslatePipe } from '../../../core/i18n/translate.pipe';
import { Avatar } from '../../../shared/ui/avatar/avatar';
import { Notice, noticeFromError, noticeText } from '../../shared/notice';
import { AdminApi, RoleName, UserPage, UserSummary } from '../shared/admin-api';

const PAGE_SIZE = 20;
const SEARCH_DEBOUNCE_MS = 300;
const ROLES: readonly RoleName[] = ['User', 'Admin'];

/**
 * Lists every user with search, a role filter and server-side pagination. Each row is a table row from md
 * up and a card below md; both carry the same data, and only the one shown is in the accessibility tree.
 */
@Component({
  selector: 'ab-users-page',
  imports: [Avatar, RouterLink, TranslatePipe],
  templateUrl: './users.page.html',
})
export class UsersPage {
  private readonly api = inject(AdminApi);
  private readonly i18n = inject(I18n);
  private readonly destroyRef = inject(DestroyRef);

  protected readonly roles = ROLES;
  protected readonly searchText = signal('');
  protected readonly role = signal<RoleName | ''>('');
  protected readonly page = signal(1);
  protected readonly result = signal<UserPage | null>(null);
  protected readonly loading = signal(true);
  protected readonly notice = signal<Notice | null>(null);

  protected readonly users = computed<readonly UserSummary[]>(() => this.result()?.items ?? []);
  protected readonly total = computed(() => this.result()?.total ?? 0);
  protected readonly pageCount = computed(() => Math.max(1, Math.ceil(this.total() / PAGE_SIZE)));
  protected readonly hasPrevious = computed(() => this.page() > 1);
  protected readonly hasNext = computed(() => this.page() < this.pageCount());

  private debounceTimer: ReturnType<typeof setTimeout> | undefined;
  /** Bumped on every request so an answer to an old query never overwrites a newer one. */
  private requestId = 0;

  constructor() {
    this.destroyRef.onDestroy(() => clearTimeout(this.debounceTimer));
    void this.load();
  }

  /** Search waits for a short pause in typing, then restarts from the first page. */
  protected onSearch(event: Event): void {
    this.searchText.set((event.target as HTMLInputElement).value);
    clearTimeout(this.debounceTimer);
    this.debounceTimer = setTimeout(() => {
      this.page.set(1);
      void this.load();
    }, SEARCH_DEBOUNCE_MS);
  }

  protected onRoleChange(event: Event): void {
    const value = (event.target as HTMLSelectElement).value;
    this.role.set(ROLES.find((role) => role === value) ?? '');
    this.page.set(1);
    void this.load();
  }

  protected previousPage(): void {
    if (!this.hasPrevious()) return;
    this.page.update((page) => page - 1);
    void this.load();
  }

  protected nextPage(): void {
    if (!this.hasNext()) return;
    this.page.update((page) => page + 1);
    void this.load();
  }

  protected retry(): void {
    void this.load();
  }

  protected roleLabelId(role: RoleName): string {
    return `admin.users.role.${role.toLowerCase()}`;
  }

  protected emailStatusId(user: UserSummary): string {
    return user.emailConfirmed ? 'admin.users.emailConfirmed' : 'admin.users.emailPending';
  }

  protected createdOn(user: UserSummary): string {
    return new Intl.DateTimeFormat(this.i18n.culture(), { dateStyle: 'medium' }).format(
      new Date(user.createdAt),
    );
  }

  protected text(notice: Notice): string {
    return noticeText(this.i18n, notice);
  }

  private async load(): Promise<void> {
    const id = ++this.requestId;
    this.loading.set(true);
    this.notice.set(null);
    try {
      const role = this.role();
      const result = await this.api.list({
        search: this.searchText(),
        role: role || undefined,
        page: this.page(),
        pageSize: PAGE_SIZE,
      });
      if (id !== this.requestId) return;
      this.result.set(result);
    } catch (error) {
      if (id !== this.requestId) return;
      this.notice.set(noticeFromError(error));
    } finally {
      if (id === this.requestId) this.loading.set(false);
    }
  }
}
