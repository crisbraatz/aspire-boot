import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { problemInterceptor } from '../../../core/http/problem.interceptor';
import { I18n } from '../../../core/i18n/i18n';
import { SignInPage } from './sign-in.page';

const ENGLISH = {
  'auth.signIn.heading': 'Sign in',
  'auth.signIn.lead': 'Use your passkey. No password needed.',
  'auth.signIn.email.label': 'Email (optional)',
  'auth.signIn.email.hint': 'Pick your passkey from the suggestions to sign in faster.',
  'auth.signIn.keepSignedIn': 'Keep me signed in on this device',
  'auth.signIn.submit': 'Sign in with passkey',
  'auth.signIn.cancelled': 'Sign-in was cancelled. Choose Sign in with passkey to try again.',
  'auth.signIn.newHere': 'New here?',
  'auth.signIn.createAccount': 'Create an account',
  'auth.unsupported': 'This browser does not support passkeys.',
  'auth.waitingDevice': 'Waiting for your device…',
  'errors.signInFailed': 'We could not sign you in. Check your passkey and try again.',
  'errors.unexpected': 'Something went wrong. Please try again.',
};

const REQUEST_OPTIONS = JSON.stringify({ challenge: 'AQID', allowCredentials: [] });
const ME = { id: 'user-1', displayName: 'Ada', email: 'ada@example.com', culture: 'en-US' };

describe('SignInPage', () => {
  let httpTesting: HttpTestingController;
  let credentials: { create: ReturnType<typeof vi.fn>; get: ReturnType<typeof vi.fn> };
  let conditionalAvailable: boolean;

  beforeEach(async () => {
    conditionalAvailable = false;
    class FakePublicKeyCredential {
      static isConditionalMediationAvailable() {
        return Promise.resolve(conditionalAvailable);
      }
    }
    vi.stubGlobal('PublicKeyCredential', FakePublicKeyCredential);
    credentials = { create: vi.fn(), get: vi.fn() };
    Object.defineProperty(navigator, 'credentials', { configurable: true, value: credentials });

    TestBed.configureTestingModule({
      imports: [SignInPage],
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

  function signInButton(element: HTMLElement): HTMLButtonElement {
    return element.querySelector<HTMLButtonElement>('button[type="submit"]')!;
  }

  it('renders one heading, an autofill-ready email field and a labelled keep-signed-in checkbox', () => {
    const fixture = TestBed.createComponent(SignInPage);
    fixture.detectChanges();
    const element = fixture.nativeElement as HTMLElement;

    expect(element.querySelectorAll('h1')).toHaveLength(1);
    expect(element.querySelector('#signin-email')?.getAttribute('autocomplete')).toBe(
      'username webauthn',
    );
    expect(element.querySelector('label[for="keep-signed-in"]')?.textContent).toContain(
      'Keep me signed in on this device',
    );
  });

  it('explains that passkeys are unsupported and offers no sign-in button', () => {
    vi.stubGlobal('PublicKeyCredential', undefined);
    const fixture = TestBed.createComponent(SignInPage);
    fixture.detectChanges();
    const element = fixture.nativeElement as HTMLElement;

    expect(element.textContent).toContain('This browser does not support passkeys.');
    expect(element.querySelector('button[type="submit"]')).toBeNull();
  });

  it('signs in with the passkey button, honouring keep-signed-in, then opens the account', async () => {
    credentials.get.mockResolvedValue({ toJSON: () => ({ id: 'cred-1' }) });
    const navigate = vi.spyOn(TestBed.inject(Router), 'navigateByUrl').mockResolvedValue(true);
    const fixture = TestBed.createComponent(SignInPage);
    fixture.detectChanges();
    const element = fixture.nativeElement as HTMLElement;

    element.querySelector<HTMLInputElement>('#keep-signed-in')!.click();
    signInButton(element).click();
    await settle(fixture);
    httpTesting.expectOne('/api/identity/auth/signin/options').flush(REQUEST_OPTIONS);
    await settle(fixture);

    const signIn = httpTesting.expectOne('/api/identity/auth/signin');
    expect(signIn.request.body).toEqual({
      credential: JSON.stringify({ id: 'cred-1' }),
      keepSignedIn: true,
    });
    signIn.flush(null);
    await settle(fixture);
    httpTesting.expectOne('/api/identity/account/me').flush(ME);
    await settle(fixture);

    expect(navigate).toHaveBeenCalledWith('/account');
  });

  it('starts conditional UI on load when the browser supports it and signs in from it', async () => {
    conditionalAvailable = true;
    credentials.get.mockResolvedValue({ toJSON: () => ({ id: 'cred-2' }) });
    vi.spyOn(TestBed.inject(Router), 'navigateByUrl').mockResolvedValue(true);
    const fixture = TestBed.createComponent(SignInPage);
    fixture.detectChanges();
    await settle(fixture);

    httpTesting.expectOne('/api/identity/auth/signin/options').flush(REQUEST_OPTIONS);
    await settle(fixture);
    expect(credentials.get).toHaveBeenCalledWith(
      expect.objectContaining({ mediation: 'conditional' }),
    );
    httpTesting.expectOne('/api/identity/auth/signin').flush(null);
    await settle(fixture);
    httpTesting.expectOne('/api/identity/account/me').flush(ME);
    await settle(fixture);
  });

  it('does not start conditional UI when the browser lacks it', async () => {
    conditionalAvailable = false;
    const fixture = TestBed.createComponent(SignInPage);
    fixture.detectChanges();
    await settle(fixture);

    httpTesting.expectNone('/api/identity/auth/signin/options');
    expect(credentials.get).not.toHaveBeenCalled();
  });

  it('explains a cancelled passkey prompt and allows a retry', async () => {
    credentials.get.mockRejectedValue(new DOMException('cancelled', 'NotAllowedError'));
    const fixture = TestBed.createComponent(SignInPage);
    fixture.detectChanges();
    const element = fixture.nativeElement as HTMLElement;

    signInButton(element).click();
    await settle(fixture);
    httpTesting.expectOne('/api/identity/auth/signin/options').flush(REQUEST_OPTIONS);
    await settle(fixture);

    expect(element.querySelector('[aria-live="polite"]')?.textContent).toContain(
      'Sign-in was cancelled.',
    );
    expect(signInButton(element).disabled).toBe(false);
    httpTesting.expectNone('/api/identity/auth/signin');
  });

  it('announces a rejected sign-in in the live region', async () => {
    credentials.get.mockResolvedValue({ toJSON: () => ({ id: 'cred-3' }) });
    const fixture = TestBed.createComponent(SignInPage);
    fixture.detectChanges();
    const element = fixture.nativeElement as HTMLElement;

    signInButton(element).click();
    await settle(fixture);
    httpTesting.expectOne('/api/identity/auth/signin/options').flush(REQUEST_OPTIONS);
    await settle(fixture);
    httpTesting
      .expectOne('/api/identity/auth/signin')
      .flush({ code: 'identity.signInFailed' }, { status: 401, statusText: 'Unauthorized' });
    await settle(fixture);

    expect(element.querySelector('[aria-live="polite"]')?.textContent).toContain(
      'We could not sign you in.',
    );
  });
});
