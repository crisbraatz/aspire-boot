import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { problemInterceptor } from '../../../core/http/problem.interceptor';
import { I18n } from '../../../core/i18n/i18n';
import { RecoverCompletePage } from './recover-complete.page';

const ENGLISH = {
  'auth.recover.complete.heading': 'Create a new passkey',
  'auth.recover.complete.lead': 'Your other sessions will be signed out.',
  'auth.recover.complete.success': 'Your new passkey is ready.',
  'auth.recover.invalidLink': 'This recovery link is not valid any more.',
  'auth.recover.requestNew': 'Request a new recovery link',
  'auth.confirm.create': 'Create my passkey',
  'auth.confirm.cancelled': 'Passkey setup was cancelled. Choose Create my passkey to try again.',
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

describe('RecoverCompletePage', () => {
  let httpTesting: HttpTestingController;
  let credentials: { create: ReturnType<typeof vi.fn>; get: ReturnType<typeof vi.fn> };

  beforeEach(async () => {
    class FakePublicKeyCredential {}
    vi.stubGlobal('PublicKeyCredential', FakePublicKeyCredential);
    credentials = { create: vi.fn(), get: vi.fn() };
    Object.defineProperty(navigator, 'credentials', { configurable: true, value: credentials });

    TestBed.configureTestingModule({
      imports: [RecoverCompletePage],
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
    history.replaceState(null, '', '/recover/complete?u=user-1&t=token-1');
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
    const fixture = TestBed.createComponent(RecoverCompletePage);
    fixture.detectChanges();

    expect(location.search).toBe('');
  });

  it('shows the invalid-link message with a way to request a new link when parameters are missing', () => {
    history.replaceState(null, '', '/recover/complete');
    const fixture = TestBed.createComponent(RecoverCompletePage);
    fixture.detectChanges();
    const element = fixture.nativeElement as HTMLElement;

    expect(element.querySelector('[role="alert"]')?.textContent).toContain(
      'This recovery link is not valid any more.',
    );
    expect(element.querySelector('a[href="/recover"]')?.textContent).toContain(
      'Request a new recovery link',
    );
    expect(button(element)).toBeNull();
    httpTesting.expectNone('/api/identity/auth/registration/options');
  });

  it('creates a new passkey for the recovery purpose and opens the account', async () => {
    credentials.create.mockResolvedValue({ toJSON: () => ({ id: 'cred-2', type: 'public-key' }) });
    const navigate = vi.spyOn(TestBed.inject(Router), 'navigateByUrl').mockResolvedValue(true);
    const fixture = TestBed.createComponent(RecoverCompletePage);
    fixture.detectChanges();
    const element = fixture.nativeElement as HTMLElement;

    button(element)!.click();
    await settle(fixture);
    const options = httpTesting.expectOne('/api/identity/auth/registration/options');
    expect(options.request.body).toEqual({
      userId: 'user-1',
      token: 'token-1',
      purpose: 'recover',
    });
    options.flush(OPTIONS);
    await settle(fixture);

    const completion = httpTesting.expectOne('/api/identity/auth/registration');
    expect(completion.request.body).toEqual({
      userId: 'user-1',
      token: 'token-1',
      purpose: 'recover',
      credential: JSON.stringify({ id: 'cred-2', type: 'public-key' }),
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
      'Your new passkey is ready.',
    );
  });

  it('explains a cancelled ceremony and lets the user try again', async () => {
    credentials.create.mockRejectedValue(new DOMException('cancelled', 'NotAllowedError'));
    const fixture = TestBed.createComponent(RecoverCompletePage);
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
});
