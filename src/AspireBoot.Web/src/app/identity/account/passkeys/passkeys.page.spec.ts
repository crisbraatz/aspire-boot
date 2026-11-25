import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { problemInterceptor } from '../../../core/http/problem.interceptor';
import { I18n } from '../../../core/i18n/i18n';
import { PasskeysPage } from './passkeys.page';

const ENGLISH = {
  'account.passkeys.heading': 'Your passkeys',
  'account.passkeys.lead': 'Each passkey signs you in on one device.',
  'account.passkeys.empty': 'You have no passkeys yet.',
  'account.passkeys.createdAt': 'Created {date}',
  'account.passkeys.add': 'Add a passkey',
  'account.passkeys.added': 'Passkey added.',
  'account.passkeys.rename': 'Rename',
  'account.passkeys.renameLabel': 'Passkey name',
  'account.passkeys.save': 'Save name',
  'account.passkeys.renamed': 'Passkey renamed.',
  'account.passkeys.remove': 'Remove',
  'account.passkeys.removeConfirm': 'Remove {name}? You will not be able to sign in with it.',
  'account.passkeys.confirmRemove': 'Yes, remove passkey',
  'account.passkeys.removed': 'Passkey removed.',
  'account.passkeys.onlyOne':
    'This is your only passkey, so it cannot be removed. Add another passkey first.',
  'account.passkeys.cancel': 'Cancel',
  'auth.validation.required': 'This field is required.',
  'auth.validation.maxLength': 'Use at most {max} characters.',
  'auth.confirm.defaultPasskeyName': 'My passkey',
  'auth.confirm.cancelled': 'Passkey setup was cancelled.',
  'auth.unsupported': 'This browser does not support passkeys.',
  'errors.lastPasskey': 'You cannot remove your last passkey. Add another one first.',
  'errors.passkeyFailed': 'We could not save this passkey. Please try again.',
  'errors.unexpected': 'Something went wrong. Please try again.',
};

const OPTIONS = JSON.stringify({
  challenge: 'AQID',
  user: { id: 'BAUG', name: 'ada@example.com', displayName: 'Ada' },
});

const TWO_PASSKEYS = [
  { id: 'cred-1', name: 'Laptop', createdAt: '2026-01-15T10:00:00Z', lastUsedAt: null },
  { id: 'cred-2', name: 'Phone', createdAt: '2026-02-20T10:00:00Z', lastUsedAt: null },
];

const ONE_PASSKEY = [TWO_PASSKEYS[0]];

describe('PasskeysPage', () => {
  let httpTesting: HttpTestingController;
  let credentials: { create: ReturnType<typeof vi.fn>; get: ReturnType<typeof vi.fn> };

  beforeEach(async () => {
    class FakePublicKeyCredential {}
    vi.stubGlobal('PublicKeyCredential', FakePublicKeyCredential);
    credentials = { create: vi.fn(), get: vi.fn() };
    Object.defineProperty(navigator, 'credentials', { configurable: true, value: credentials });

    TestBed.configureTestingModule({
      imports: [PasskeysPage],
      providers: [
        provideRouter([]),
        provideHttpClient(withInterceptors([problemInterceptor])),
        provideHttpClientTesting(),
      ],
    });
    httpTesting = TestBed.inject(HttpTestingController);
    const loading = TestBed.inject(I18n).use('en-US');
    httpTesting.expectOne('/i18n/en-US.json').flush(ENGLISH);
    await loading;
  });

  afterEach(() => {
    httpTesting.verify();
    vi.unstubAllGlobals();
    delete (navigator as { credentials?: unknown }).credentials;
  });

  async function settle(fixture: { whenStable(): Promise<unknown>; detectChanges(): void }) {
    await new Promise((resolve) => setTimeout(resolve, 0));
    await fixture.whenStable();
    fixture.detectChanges();
  }

  /** Creates the page and answers its initial list request. */
  async function open(passkeys: unknown[]) {
    const fixture = TestBed.createComponent(PasskeysPage);
    fixture.detectChanges();
    httpTesting.expectOne('/api/identity/account/passkeys').flush(passkeys);
    await settle(fixture);
    return { fixture, element: fixture.nativeElement as HTMLElement };
  }

  function button(element: HTMLElement, action: string): HTMLButtonElement | null {
    return element.querySelector<HTMLButtonElement>(`button[data-action="${action}"]`);
  }

  it('lists each passkey with its name and created date and no last-used value', async () => {
    const { element } = await open(TWO_PASSKEYS);
    const items = element.querySelectorAll('li');

    expect(element.querySelectorAll('h1')).toHaveLength(1);
    expect(items).toHaveLength(2);
    expect(items[0].textContent).toContain('Laptop');
    expect(items[0].textContent).toContain('Created');
    expect(items[0].textContent).not.toMatch(/used/i);
  });

  it('disables removal and explains it when only one passkey is left', async () => {
    const { element } = await open(ONE_PASSKEY);

    expect(button(element, 'remove')?.disabled).toBe(true);
    expect(element.textContent).toContain('This is your only passkey');
  });

  it('adds a passkey through the ceremony and reloads the list', async () => {
    credentials.create.mockResolvedValue({ toJSON: () => ({ id: 'cred-3', type: 'public-key' }) });
    const { fixture, element } = await open(TWO_PASSKEYS);

    button(element, 'add')!.click();
    await settle(fixture);
    httpTesting.expectOne('/api/identity/account/passkeys/options').flush(OPTIONS);
    await settle(fixture);
    const added = httpTesting.expectOne('/api/identity/account/passkeys');
    expect(added.request.body).toEqual({
      credential: JSON.stringify({ id: 'cred-3', type: 'public-key' }),
      name: 'My passkey',
    });
    added.flush(null);
    await settle(fixture);
    httpTesting.expectOne('/api/identity/account/passkeys').flush(TWO_PASSKEYS);
    await settle(fixture);

    expect(element.querySelector('[role="status"]')?.textContent).toContain('Passkey added.');
  });

  it('renames a passkey inline with a labelled field and saves it', async () => {
    const { fixture, element } = await open(TWO_PASSKEYS);

    button(element, 'rename')!.click();
    await settle(fixture);
    const input = element.querySelector<HTMLInputElement>('#passkey-rename')!;
    expect(input.value).toBe('Laptop');
    input.value = 'Work laptop';
    input.dispatchEvent(new Event('input'));
    element.querySelector('form')!.dispatchEvent(new Event('submit'));
    await settle(fixture);

    const rename = httpTesting.expectOne('/api/identity/account/passkeys/cred-1');
    expect(rename.request.method).toBe('PATCH');
    expect(rename.request.body).toEqual({ name: 'Work laptop' });
    rename.flush(null);
    await settle(fixture);
    httpTesting.expectOne('/api/identity/account/passkeys').flush(TWO_PASSKEYS);
    await settle(fixture);

    expect(element.querySelector('[role="status"]')?.textContent).toContain('Passkey renamed.');
  });

  it('removes a passkey only after confirmation', async () => {
    const { fixture, element } = await open(TWO_PASSKEYS);

    button(element, 'remove')!.click();
    await settle(fixture);
    httpTesting.expectNone('/api/identity/account/passkeys/cred-1');
    button(element, 'confirm-remove')!.click();
    await settle(fixture);

    const removal = httpTesting.expectOne('/api/identity/account/passkeys/cred-1');
    expect(removal.request.method).toBe('DELETE');
    removal.flush(null);
    await settle(fixture);
    httpTesting.expectOne('/api/identity/account/passkeys').flush(ONE_PASSKEY);
    await settle(fixture);

    expect(element.querySelector('[role="status"]')?.textContent).toContain('Passkey removed.');
  });

  it('shows the last-passkey error when the API refuses the removal', async () => {
    const { fixture, element } = await open(TWO_PASSKEYS);

    button(element, 'remove')!.click();
    await settle(fixture);
    button(element, 'confirm-remove')!.click();
    await settle(fixture);
    httpTesting
      .expectOne('/api/identity/account/passkeys/cred-1')
      .flush({ code: 'identity.lastPasskey' }, { status: 409, statusText: 'Conflict' });
    await settle(fixture);

    expect(element.querySelector('[role="alert"]')?.textContent).toContain(
      'You cannot remove your last passkey.',
    );
  });
});
