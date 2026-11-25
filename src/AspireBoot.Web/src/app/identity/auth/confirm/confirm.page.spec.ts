import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { problemInterceptor } from '../../../core/http/problem.interceptor';
import { I18n } from '../../../core/i18n/i18n';
import { ConfirmPage } from './confirm.page';

const ENGLISH = {
  'auth.confirm.heading': 'Create your passkey',
  'auth.confirm.lead': 'A passkey signs you in with your device.',
  'auth.confirm.create': 'Create my passkey',
  'auth.confirm.success': 'Your passkey is ready.',
  'auth.confirm.cancelled': 'Passkey setup was cancelled. Choose Create my passkey to try again.',
  'auth.confirm.invalidLink': 'This link is not valid any more.',
  'auth.confirm.requestNew': 'Request a new link',
  'auth.confirm.defaultPasskeyName': 'My passkey',
  'auth.unsupported': 'This browser does not support passkeys.',
  'auth.waitingDevice': 'Waiting for your device…',
  'errors.passkeyFailed': 'We could not save this passkey. Please try again.',
  'errors.unexpected': 'Something went wrong. Please try again.',
};

const OPTIONS = JSON.stringify({
  challenge: 'AQID',
  user: { id: 'BAUG', name: 'ada@example.com', displayName: 'Ada' },
});

describe('ConfirmPage', () => {
  let httpTesting: HttpTestingController;
  let credentials: { create: ReturnType<typeof vi.fn>; get: ReturnType<typeof vi.fn> };

  beforeEach(async () => {
    class FakePublicKeyCredential {}
    vi.stubGlobal('PublicKeyCredential', FakePublicKeyCredential);
    credentials = { create: vi.fn(), get: vi.fn() };
    Object.defineProperty(navigator, 'credentials', { configurable: true, value: credentials });

    TestBed.configureTestingModule({
      imports: [ConfirmPage],
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
    history.replaceState(null, '', '/confirm?u=user-1&t=token-1');
  });

  afterEach(() => {
    httpTesting.verify();
    vi.unstubAllGlobals();
    delete (navigator as { credentials?: unknown }).credentials;
    history.replaceState(null, '', '/');
  });

  async function settle(fixture: { whenStable(): Promise<unknown>; detectChanges(): void }) {
    await new Promise((resolve) => setTimeout(resolve, 0));
    await fixture.whenStable();
    fixture.detectChanges();
  }

  function button(element: HTMLElement): HTMLButtonElement | null {
    return element.querySelector<HTMLButtonElement>('button[data-action="create"]');
  }

  it('removes the one-time token from the URL', () => {
    const fixture = TestBed.createComponent(ConfirmPage);
    fixture.detectChanges();

    expect(location.search).toBe('');
  });

  it('shows the invalid-link message with a way to request a new link when the parameters are missing', () => {
    history.replaceState(null, '', '/confirm');
    const fixture = TestBed.createComponent(ConfirmPage);
    fixture.detectChanges();
    const element = fixture.nativeElement as HTMLElement;

    expect(element.querySelector('[role="alert"]')?.textContent).toContain(
      'This link is not valid any more.',
    );
    expect(element.querySelector('a[href="/signup"]')?.textContent).toContain('Request a new link');
    expect(button(element)).toBeNull();
    httpTesting.expectNone('/api/identity/auth/registration/options');
  });

  it('explains that passkeys are unsupported and offers no create button', () => {
    vi.stubGlobal('PublicKeyCredential', undefined);
    const fixture = TestBed.createComponent(ConfirmPage);
    fixture.detectChanges();
    const element = fixture.nativeElement as HTMLElement;

    expect(element.textContent).toContain('This browser does not support passkeys.');
    expect(button(element)).toBeNull();
  });

  it('creates the passkey, completes confirmation, refreshes the session and opens the account', async () => {
    credentials.create.mockResolvedValue({ toJSON: () => ({ id: 'cred-1', type: 'public-key' }) });
    const navigate = vi.spyOn(TestBed.inject(Router), 'navigateByUrl').mockResolvedValue(true);
    const fixture = TestBed.createComponent(ConfirmPage);
    fixture.detectChanges();
    const element = fixture.nativeElement as HTMLElement;

    button(element)!.click();
    await settle(fixture);
    const options = httpTesting.expectOne('/api/identity/auth/registration/options');
    expect(options.request.body).toEqual({
      userId: 'user-1',
      token: 'token-1',
      purpose: 'confirm',
    });
    options.flush(OPTIONS);
    await settle(fixture);

    const completion = httpTesting.expectOne('/api/identity/auth/registration');
    expect(completion.request.body).toEqual({
      userId: 'user-1',
      token: 'token-1',
      purpose: 'confirm',
      credential: JSON.stringify({ id: 'cred-1', type: 'public-key' }),
      name: 'My passkey',
    });
    completion.flush(null);
    await settle(fixture);
    httpTesting.expectOne('/api/identity/account/me').flush({
      id: 'user-1',
      displayName: 'Ada',
      email: 'ada@example.com',
      culture: 'en-US',
    });
    await settle(fixture);

    expect(navigate).toHaveBeenCalledWith('/account');
    expect(element.querySelector('[role="status"]')?.textContent).toContain(
      'Your passkey is ready.',
    );
  });

  it('explains a cancelled ceremony and lets the user try again', async () => {
    credentials.create.mockRejectedValue(new DOMException('cancelled', 'NotAllowedError'));
    const fixture = TestBed.createComponent(ConfirmPage);
    fixture.detectChanges();
    const element = fixture.nativeElement as HTMLElement;

    button(element)!.click();
    await settle(fixture);
    httpTesting.expectOne('/api/identity/auth/registration/options').flush(OPTIONS);
    await settle(fixture);

    expect(element.querySelector('[role="alert"]')?.textContent).toContain(
      'Passkey setup was cancelled.',
    );
    expect(button(element)?.disabled).toBe(false);
    httpTesting.expectNone('/api/identity/auth/registration');
  });

  it('shows the invalid-link state when the API rejects the token', async () => {
    const fixture = TestBed.createComponent(ConfirmPage);
    fixture.detectChanges();
    const element = fixture.nativeElement as HTMLElement;

    button(element)!.click();
    await settle(fixture);
    httpTesting
      .expectOne('/api/identity/auth/registration/options')
      .flush({ code: 'identity.invalidLink' }, { status: 400, statusText: 'Bad Request' });
    await settle(fixture);

    expect(element.querySelector('[role="alert"]')?.textContent).toContain(
      'This link is not valid any more.',
    );
    expect(element.querySelector('a[href="/signup"]')).not.toBeNull();
  });
});
