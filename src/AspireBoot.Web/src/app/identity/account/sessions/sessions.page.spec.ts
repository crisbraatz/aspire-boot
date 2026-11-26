import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { problemInterceptor } from '../../../core/http/problem.interceptor';
import { I18n } from '../../../core/i18n/i18n';
import en from '../../../../../public/i18n/en-US.json';
import pt from '../../../../../public/i18n/pt-BR.json';
import es from '../../../../../public/i18n/es-ES.json';
import { SessionsPage } from './sessions.page';

const LIST_URL = '/api/identity/account/sessions';
const OTHERS_URL = '/api/identity/account/sessions/end-others';

const CURRENT = {
  id: 's-current',
  browser: 'chrome',
  os: 'macos',
  createdAt: '2026-01-15T10:00:00Z',
  lastSeenAt: '2026-11-20T08:30:00Z',
  ipMasked: '192.168.*.*',
  current: true,
};
const FIREFOX = {
  id: 's-firefox',
  browser: 'firefox',
  os: 'windows',
  createdAt: '2026-02-20T10:00:00Z',
  lastSeenAt: '2026-11-19T18:00:00Z',
  ipMasked: '10.0.*.*',
  current: false,
};
const UNKNOWN = {
  id: 's-unknown',
  browser: 'unknown',
  os: 'unknown',
  createdAt: '2026-03-01T10:00:00Z',
  lastSeenAt: '2026-03-02T10:00:00Z',
  ipMasked: 'unknown',
  current: false,
};

/**
 * Every id the sessions page and its routes use, dynamic token keys included. Each one must exist in all catalogs.
 */
const SESSION_IDS = [
  'account.sessions.title',
  'account.sessions.heading',
  'account.sessions.lead',
  'account.sessions.loading',
  'account.sessions.empty',
  'account.sessions.thisDevice',
  'account.sessions.signedInOn',
  'account.sessions.lastSeenOn',
  'account.sessions.ip',
  'account.sessions.end',
  'account.sessions.endConfirmTitle',
  'account.sessions.endConfirmText',
  'account.sessions.endConfirm',
  'account.sessions.endOthers',
  'account.sessions.noOthers',
  'account.sessions.endOthersConfirmTitle',
  'account.sessions.endOthersConfirmText',
  'account.sessions.endOthersConfirm',
  'account.sessions.cancel',
  'account.sessions.ended',
  'account.sessions.endedOthers',
  'account.sessions.back',
  'account.home.manageSessions',
  'sessions.device',
  'sessions.browser.chrome',
  'sessions.browser.edge',
  'sessions.browser.firefox',
  'sessions.browser.safari',
  'sessions.browser.opera',
  'sessions.browser.samsung',
  'sessions.browser.unknown',
  'sessions.os.windows',
  'sessions.os.macos',
  'sessions.os.ios',
  'sessions.os.android',
  'sessions.os.linux',
  'sessions.os.chromeos',
  'sessions.os.unknown',
  'errors.sessionNotFound',
  'errors.currentSession',
  'errors.rateLimited',
  'errors.unexpected',
];

/**
 * jsdom does not implement the modal dialog API. This shim mirrors the browser behaviour the page relies on:
 * showModal opens and moves focus to the element with autofocus, close closes and fires `close`.
 */
function shimDialogApi(): void {
  const proto = HTMLDialogElement.prototype as HTMLDialogElement & {
    showModal?: () => void;
    close?: () => void;
  };
  if (typeof proto.showModal === 'function') return;
  proto.showModal = function (this: HTMLDialogElement) {
    this.setAttribute('open', '');
    this.querySelector<HTMLElement>('[autofocus]')?.focus();
  };
  proto.close = function (this: HTMLDialogElement) {
    this.removeAttribute('open');
    this.dispatchEvent(new Event('close'));
  };
}

describe('SessionsPage', () => {
  let httpTesting: HttpTestingController;
  let fixture: ComponentFixture<SessionsPage>;
  let element: HTMLElement;

  beforeEach(async () => {
    shimDialogApi();
    TestBed.configureTestingModule({
      imports: [SessionsPage],
      providers: [
        provideRouter([]),
        provideHttpClient(withInterceptors([problemInterceptor])),
        provideHttpClientTesting(),
      ],
    });
    httpTesting = TestBed.inject(HttpTestingController);
    const loading = TestBed.inject(I18n).use('en-US');
    httpTesting.expectOne('/i18n/en-US.json').flush(en);
    await loading;
  });

  afterEach(() => {
    httpTesting.verify();
    localStorage.clear();
  });

  async function settle(): Promise<void> {
    await new Promise((resolve) => setTimeout(resolve, 0));
    await fixture.whenStable();
    fixture.detectChanges();
  }

  /** Creates the page and answers its initial list request. */
  async function open(sessions: unknown[]): Promise<void> {
    fixture = TestBed.createComponent(SessionsPage);
    element = fixture.nativeElement as HTMLElement;
    fixture.detectChanges();
    httpTesting.expectOne(LIST_URL).flush(sessions);
    await settle();
  }

  function button(action: string, sessionId?: string): HTMLButtonElement | null {
    const scope = sessionId ? `[data-session="${sessionId}"] ` : '';
    return element.querySelector<HTMLButtonElement>(`${scope}button[data-action="${action}"]`);
  }

  function dialog(): HTMLDialogElement {
    return element.querySelector<HTMLDialogElement>('dialog')!;
  }

  function dialogButton(action: string): HTMLButtonElement {
    return dialog().querySelector<HTMLButtonElement>(`button[data-action="${action}"]`)!;
  }

  function status(): HTMLElement | null {
    return element.querySelector<HTMLElement>('[role="status"]');
  }

  function alert(): HTMLElement | null {
    return element.querySelector<HTMLElement>('[role="alert"]');
  }

  it('lists each session with translated browser and OS, masked IP and dates', async () => {
    await open([CURRENT, FIREFOX, UNKNOWN]);
    const items = element.querySelectorAll('li[data-session]');

    expect(element.querySelectorAll('h1')).toHaveLength(1);
    expect(items).toHaveLength(3);
    expect(items[1].textContent).toContain('Firefox on Windows');
    expect(items[1].textContent).toContain('10.0.*.*');
    expect(items[1].textContent).toMatch(/Signed in/);
    expect(items[1].textContent).toMatch(/Last active/);
  });

  it('shows "Unknown browser" for an unrecognized agent', async () => {
    await open([UNKNOWN]);

    expect(element.textContent).toContain('Unknown browser');
    expect(element.textContent).toContain('Unknown system');
    expect(element.textContent).not.toContain('unknown');
  });

  it('marks the current device with a badge and offers no End button for it', async () => {
    await open([CURRENT, FIREFOX]);
    const current = element.querySelector('[data-session="s-current"]')!;

    expect(current.textContent).toContain('This device');
    expect(button('end', 's-current')).toBeNull();
    expect(button('end', 's-firefox')).not.toBeNull();
  });

  it('ends a session only after confirmation, then reloads the list and announces it', async () => {
    await open([CURRENT, FIREFOX]);

    button('end', 's-firefox')!.click();
    fixture.detectChanges();
    expect(dialog().open).toBe(true);
    expect(dialog().getAttribute('aria-labelledby')).toBeTruthy();
    httpTesting.expectNone({ method: 'DELETE' });

    dialogButton('confirm-end').click();
    httpTesting
      .expectOne({ method: 'DELETE', url: `${LIST_URL}/s-firefox` })
      .flush(null, { status: 204, statusText: 'No Content' });
    await settle();
    httpTesting.expectOne(LIST_URL).flush([CURRENT]);
    await settle();

    expect(dialog().open).toBe(false);
    expect(status()?.textContent).toContain('Session ended.');
    expect(element.querySelector('[data-session="s-firefox"]')).toBeNull();
  });

  it('cancels the end without any request when the dialog is dismissed', async () => {
    await open([CURRENT, FIREFOX]);

    button('end', 's-firefox')!.click();
    fixture.detectChanges();
    dialogButton('cancel-dialog').click();
    fixture.detectChanges();

    expect(dialog().open).toBe(false);
    expect(button('end', 's-firefox')).not.toBeNull();
    httpTesting.expectNone({ method: 'DELETE' });
  });

  it('disables "End all other sessions" when there are no other sessions', async () => {
    await open([CURRENT]);

    expect(button('end-others')?.disabled).toBe(true);
    expect(element.textContent).toContain('There are no other sessions to end.');
  });

  it('ends all other sessions after its own confirmation and reports the count', async () => {
    await open([CURRENT, FIREFOX, UNKNOWN]);

    button('end-others')!.click();
    fixture.detectChanges();
    expect(dialog().getAttribute('aria-labelledby')).toBeTruthy();
    expect(dialog().textContent).toContain('End all other sessions?');

    dialogButton('confirm-end-others').click();
    httpTesting.expectOne(OTHERS_URL).flush({ ended: 2 });
    await settle();
    httpTesting.expectOne(LIST_URL).flush([CURRENT]);
    await settle();

    expect(status()?.textContent).toContain('Other sessions ended: 2.');
    expect(button('end-others')?.disabled).toBe(true);
  });

  it('maps identity.sessionNotFound to a message and refreshes the list', async () => {
    await open([CURRENT, FIREFOX]);

    button('end', 's-firefox')!.click();
    fixture.detectChanges();
    dialogButton('confirm-end').click();
    httpTesting
      .expectOne({ method: 'DELETE', url: `${LIST_URL}/s-firefox` })
      .flush(
        { title: 'Not found', code: 'identity.sessionNotFound' },
        { status: 404, statusText: 'Not Found' },
      );
    await settle();
    httpTesting.expectOne(LIST_URL).flush([CURRENT]);
    await settle();

    expect(alert()?.textContent).toContain('This session no longer exists.');
    expect(element.querySelector('[data-session="s-firefox"]')).toBeNull();
  });

  it('maps identity.currentSession to a message that points to sign-out', async () => {
    await open([CURRENT, FIREFOX]);

    button('end', 's-firefox')!.click();
    fixture.detectChanges();
    dialogButton('confirm-end').click();
    httpTesting
      .expectOne({ method: 'DELETE', url: `${LIST_URL}/s-firefox` })
      .flush(
        { title: 'Conflict', code: 'identity.currentSession' },
        { status: 409, statusText: 'Conflict' },
      );
    await settle();

    expect(alert()?.textContent).toContain('Use Sign out instead.');
  });

  it('maps a rate limit to the countdown message', async () => {
    await open([CURRENT, FIREFOX]);

    button('end', 's-firefox')!.click();
    fixture.detectChanges();
    dialogButton('confirm-end').click();
    httpTesting
      .expectOne({ method: 'DELETE', url: `${LIST_URL}/s-firefox` })
      .flush(
        { title: 'Too many', code: 'rateLimited' },
        { status: 429, statusText: 'Too Many Requests', headers: { 'Retry-After': '30' } },
      );
    await settle();

    expect(alert()?.textContent).toContain('Try again in 30 seconds.');
  });

  it('falls back to the generic message for an unknown failure', async () => {
    await open([CURRENT, FIREFOX]);

    button('end', 's-firefox')!.click();
    fixture.detectChanges();
    dialogButton('confirm-end').click();
    httpTesting
      .expectOne({ method: 'DELETE', url: `${LIST_URL}/s-firefox` })
      .flush({ title: 'Boom' }, { status: 500, statusText: 'Server Error' });
    await settle();

    expect(alert()?.textContent).toContain('Something went wrong. Please try again.');
  });

  it('links back to the account home', async () => {
    await open([CURRENT]);

    expect(element.querySelector('a[href="/account"]')).not.toBeNull();
  });

  it('has every session id in all three catalogs', () => {
    const catalogs = { 'en-US': en, 'pt-BR': pt, 'es-ES': es } as Record<
      string,
      Record<string, string>
    >;
    for (const [culture, catalog] of Object.entries(catalogs)) {
      for (const id of SESSION_IDS) {
        expect(catalog[id], `${culture} ${id}`).toBeTruthy();
      }
    }
  });
});
