import {
  ChangeDetectorRef,
  Component,
  ElementRef,
  computed,
  inject,
  signal,
  viewChild,
} from '@angular/core';
import { RouterLink } from '@angular/router';
import { ApiProblem } from '../../../core/http/problem.interceptor';
import { I18n } from '../../../core/i18n/i18n';
import { TranslatePipe } from '../../../core/i18n/translate.pipe';
import { IdentityApi, SessionSummary } from '../../shared/identity-api';
import { Notice, noticeFromError, noticeText } from '../../shared/notice';

const KNOWN_BROWSERS: readonly string[] = [
  'chrome',
  'edge',
  'firefox',
  'safari',
  'opera',
  'samsung',
];
const KNOWN_OPERATING_SYSTEMS: readonly string[] = [
  'windows',
  'macos',
  'ios',
  'android',
  'linux',
  'chromeos',
];
const UNKNOWN_TOKEN = 'unknown';

/** Session problem codes from the API mapped to message ids. Other codes use the shared mapping. */
const SESSION_ERROR_IDS: Readonly<Record<string, string>> = {
  'identity.sessionNotFound': 'errors.sessionNotFound',
  'identity.currentSession': 'errors.currentSession',
};

type DialogMode = 'end' | 'others';

/**
 * Lists the devices signed in to the account and ends them. Ending one device, or all the others, goes
 * through one native modal dialog: the browser traps focus, Escape cancels, and focus returns to the
 * button that opened it. The current device is marked and never offers "End".
 */
@Component({
  selector: 'ab-sessions-page',
  imports: [RouterLink, TranslatePipe],
  templateUrl: './sessions.page.html',
})
export class SessionsPage {
  private readonly api = inject(IdentityApi);
  private readonly i18n = inject(I18n);
  private readonly changeDetector = inject(ChangeDetectorRef);
  private readonly dialogRef = viewChild<ElementRef<HTMLDialogElement>>('sessionDialog');

  protected readonly sessions = signal<SessionSummary[]>([]);
  protected readonly loading = signal(true);
  protected readonly busy = signal(false);
  protected readonly notice = signal<Notice | null>(null);
  protected readonly status = signal<Notice | null>(null);

  /** Which confirmation the dialog shows, and for which session when ending one. */
  protected readonly mode = signal<DialogMode>('end');
  protected readonly pending = signal<SessionSummary | null>(null);

  /** Sessions other than the one making this request. */
  protected readonly others = computed(() => this.sessions().filter((s) => !s.current));
  protected readonly pendingDevice = computed(() => {
    const session = this.pending();
    return session ? this.deviceName(session) : '';
  });

  /** The button that opened the dialog, focused again when it closes. */
  private trigger: HTMLElement | null = null;

  constructor() {
    void this.load();
  }

  protected askEnd(session: SessionSummary, event: Event): void {
    this.prepareDialog(event);
    this.mode.set('end');
    this.pending.set(session);
    this.openDialog();
  }

  protected askEndOthers(event: Event): void {
    this.prepareDialog(event);
    this.mode.set('others');
    this.pending.set(null);
    this.openDialog();
  }

  /** Runs on every close, including Escape, so the page state and focus always return to the trigger. */
  protected onDialogClosed(): void {
    this.pending.set(null);
    this.trigger?.focus();
    this.trigger = null;
  }

  protected cancelDialog(): void {
    this.dialogRef()?.nativeElement.close();
  }

  protected async confirmDialog(): Promise<void> {
    if (this.busy()) return;
    if (this.mode() === 'end') {
      const session = this.pending();
      if (session) await this.endSession(session.id);
    } else {
      await this.endOthers();
    }
    this.dialogRef()?.nativeElement.close();
  }

  protected deviceName(session: SessionSummary): string {
    return this.i18n.t('sessions.device', {
      browser: this.browserName(session.browser),
      os: this.osName(session.os),
    });
  }

  protected browserName(token: string): string {
    return this.i18n.t(this.tokenKey('sessions.browser', token, KNOWN_BROWSERS));
  }

  protected osName(token: string): string {
    return this.i18n.t(this.tokenKey('sessions.os', token, KNOWN_OPERATING_SYSTEMS));
  }

  protected hasIp(session: SessionSummary): boolean {
    return session.ipMasked !== UNKNOWN_TOKEN;
  }

  protected dateOn(value: string): string {
    return new Intl.DateTimeFormat(this.i18n.culture(), {
      dateStyle: 'medium',
      timeStyle: 'short',
    }).format(new Date(value));
  }

  protected text(notice: Notice): string {
    return noticeText(this.i18n, notice);
  }

  private async endSession(id: string): Promise<void> {
    this.busy.set(true);
    this.notice.set(null);
    this.status.set(null);
    try {
      await this.api.endSession(id);
      this.status.set({ id: 'account.sessions.ended' });
      await this.load();
    } catch (error) {
      this.notice.set(this.errorNotice(error));
      if (this.isSessionNotFound(error)) await this.load();
    } finally {
      this.busy.set(false);
    }
  }

  private async endOthers(): Promise<void> {
    this.busy.set(true);
    this.notice.set(null);
    this.status.set(null);
    try {
      const result = await this.api.endOtherSessions();
      this.status.set({ id: 'account.sessions.endedOthers', params: { count: result.ended } });
      await this.load();
    } catch (error) {
      this.notice.set(this.errorNotice(error));
    } finally {
      this.busy.set(false);
    }
  }

  private async load(): Promise<void> {
    try {
      this.sessions.set(await this.api.listSessions());
    } catch (error) {
      this.notice.set(this.errorNotice(error));
    } finally {
      this.loading.set(false);
    }
  }

  private prepareDialog(event: Event): void {
    this.notice.set(null);
    this.status.set(null);
    this.trigger = event.currentTarget as HTMLElement;
  }

  /** Renders the dialog content for the chosen mode first, so its autofocus target exists before it opens. */
  private openDialog(): void {
    this.changeDetector.detectChanges();
    this.dialogRef()?.nativeElement.showModal();
  }

  private errorNotice(error: unknown): Notice {
    if (error instanceof ApiProblem && error.code && SESSION_ERROR_IDS[error.code]) {
      return { id: SESSION_ERROR_IDS[error.code] };
    }
    return noticeFromError(error);
  }

  private isSessionNotFound(error: unknown): boolean {
    return error instanceof ApiProblem && error.code === 'identity.sessionNotFound';
  }

  private tokenKey(prefix: string, token: string, known: readonly string[]): string {
    return `${prefix}.${known.includes(token) ? token : UNKNOWN_TOKEN}`;
  }
}
