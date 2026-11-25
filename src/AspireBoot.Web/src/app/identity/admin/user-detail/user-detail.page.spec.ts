import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { signal } from '@angular/core';
import { problemInterceptor } from '../../../core/http/problem.interceptor';
import { I18n } from '../../../core/i18n/i18n';
import en from '../../../../../public/i18n/en-US.json';
import { CurrentUser } from '../../shared/identity-api';
import { Session } from '../../shared/session';
import { UserDetail } from '../shared/admin-api';
import { UserDetailPage } from './user-detail.page';

const USER_URL = '/api/identity/admin/users/u1';
const ROLE_URL = `${USER_URL}/role`;

function detail(overrides: Partial<UserDetail> = {}): UserDetail {
  return {
    id: 'u1',
    displayName: 'Ana Silva',
    email: 'ana@example.com',
    role: 'User',
    emailConfirmed: true,
    createdAt: '2026-01-15T10:00:00Z',
    avatarUrl: null,
    passkeyCount: 2,
    culture: 'pt-BR',
    ...overrides,
  };
}

function signedIn(id: string): CurrentUser {
  return {
    id,
    displayName: 'Admin Root',
    email: 'root@example.com',
    culture: 'en-US',
    avatarUrl: null,
    roles: ['Admin'],
  };
}

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

describe('UserDetailPage', () => {
  let fixture: ComponentFixture<UserDetailPage>;
  let element: HTMLElement;
  let httpTesting: HttpTestingController;
  let me: ReturnType<typeof signal<CurrentUser | null>>;

  beforeEach(async () => {
    shimDialogApi();
    me = signal<CurrentUser | null>(signedIn('admin-1'));
    TestBed.configureTestingModule({
      imports: [UserDetailPage],
      providers: [
        provideRouter([]),
        provideHttpClient(withInterceptors([problemInterceptor])),
        provideHttpClientTesting(),
        { provide: Session, useValue: { currentUser: me.asReadonly() } },
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

  /** Creates the page for user u1 and answers its first detail request. */
  async function open(user: UserDetail): Promise<void> {
    fixture = TestBed.createComponent(UserDetailPage);
    fixture.componentRef.setInput('id', 'u1');
    element = fixture.nativeElement as HTMLElement;
    fixture.detectChanges();
    httpTesting.expectOne(USER_URL).flush(user);
    await settle();
  }

  async function settle(): Promise<void> {
    await new Promise((resolve) => setTimeout(resolve, 0));
    await fixture.whenStable();
    fixture.detectChanges();
  }

  function queryOne<T extends Element>(selector: string): T | null {
    return element.querySelector<T>(selector);
  }

  function button(action: string): HTMLButtonElement | null {
    return queryOne<HTMLButtonElement>(`button[data-action="${action}"]`);
  }

  function dialog(): HTMLDialogElement {
    return queryOne<HTMLDialogElement>('dialog')!;
  }

  it('shows the name, email, role, passkey count and language', async () => {
    await open(detail());

    expect(queryOne('h1')?.textContent).toContain('Ana Silva');
    expect(element.textContent).toContain('ana@example.com');
    expect(element.textContent).toContain('User');
    expect(element.textContent).toContain('Passkeys');
    expect(element.textContent).toContain('2');
    expect(element.textContent).toContain('pt-BR');
  });

  it('shows initials when the user has no avatar and the picture when one is given', async () => {
    await open(detail());
    expect(element.querySelector('ab-avatar img')).toBeNull();
    expect(element.textContent).toContain('AS');
  });

  it('offers to make another user an Admin', async () => {
    await open(detail());

    expect(button('change-role')?.textContent).toContain('Make Admin');
  });

  it('hides the role change for the signed-in Admin and explains why', async () => {
    await open(detail({ id: 'admin-1', role: 'Admin' }));

    expect(button('change-role')).toBeNull();
    expect(element.textContent).toContain('Your own Admin role cannot be removed here.');
  });

  it('opens a native confirmation dialog with focus on Cancel and closes it without a request', async () => {
    await open(detail());

    button('change-role')!.click();
    fixture.detectChanges();

    expect(dialog().open).toBe(true);
    expect(dialog().getAttribute('aria-labelledby')).toBeTruthy();
    expect(document.activeElement).toBe(
      dialog().querySelector<HTMLButtonElement>('button[data-action="cancel-change"]'),
    );

    dialog().querySelector<HTMLButtonElement>('button[data-action="cancel-change"]')!.click();
    fixture.detectChanges();

    expect(dialog().open).toBe(false);
    httpTesting.expectNone(ROLE_URL);
  });

  it('changes the role after confirmation and shows the new role', async () => {
    await open(detail());

    button('change-role')!.click();
    fixture.detectChanges();
    dialog().querySelector<HTMLButtonElement>('button[data-action="confirm-change"]')!.click();

    const put = httpTesting.expectOne(ROLE_URL);
    expect(put.request.method).toBe('PUT');
    expect(put.request.body).toEqual({ role: 'Admin' });
    put.flush(null, { status: 204, statusText: 'No Content' });
    await settle();

    httpTesting.expectOne(USER_URL).flush(detail({ role: 'Admin' }));
    await settle();

    expect(dialog().open).toBe(false);
    expect(element.textContent).toContain('Role updated.');
    expect(button('change-role')?.textContent).toContain('Remove Admin role');
    expect(element.textContent).toContain('Admin');
  });

  it('shows the localized message when the last Admin cannot be demoted', async () => {
    await open(detail({ role: 'Admin' }));

    button('change-role')!.click();
    fixture.detectChanges();
    dialog().querySelector<HTMLButtonElement>('button[data-action="confirm-change"]')!.click();

    httpTesting
      .expectOne(ROLE_URL)
      .flush({ code: 'identity.lastAdmin' }, { status: 409, statusText: 'Conflict' });
    await settle();

    expect(dialog().open).toBe(false);
    expect(queryOne('[role="alert"]')?.textContent).toContain('At least one Admin must remain.');
  });

  it('shows the localized message when the user no longer exists', async () => {
    fixture = TestBed.createComponent(UserDetailPage);
    fixture.componentRef.setInput('id', 'u1');
    element = fixture.nativeElement as HTMLElement;
    fixture.detectChanges();
    httpTesting
      .expectOne(USER_URL)
      .flush({ code: 'unknown' }, { status: 404, statusText: 'Not Found' });
    await settle();

    expect(queryOne('[role="alert"]')?.textContent).toContain('This user does not exist.');
  });
});
