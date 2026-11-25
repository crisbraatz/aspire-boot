import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { problemInterceptor } from '../../../core/http/problem.interceptor';
import { I18n } from '../../../core/i18n/i18n';
import { SignUpPage } from './sign-up.page';

const ENGLISH = {
  'auth.signUp.heading': 'Create your account',
  'auth.signUp.lead': 'We will email you a link to create your passkey. No password needed.',
  'auth.signUp.displayName.label': 'Display name',
  'auth.signUp.email.label': 'Email',
  'auth.signUp.submit': 'Create account',
  'auth.signUp.checkEmail': 'Check your email.',
  'auth.signUp.resend': 'Send the email again',
  'auth.signUp.resendCooldown': 'You can request another email in {seconds} seconds.',
  'auth.signUp.resent': 'We sent the email again.',
  'auth.signUp.hasAccount': 'Already have an account?',
  'auth.signIn.submit': 'Sign in with passkey',
  'auth.validation.required': 'This field is required.',
  'auth.validation.email': 'Enter a valid email address.',
  'auth.validation.maxLength': 'Use at most {max} characters.',
  'errors.unexpected': 'Something went wrong. Please try again.',
  'errors.rateLimited': 'Too many attempts. Try again in {seconds} seconds.',
};

describe('SignUpPage', () => {
  let httpTesting: HttpTestingController;

  beforeEach(async () => {
    TestBed.configureTestingModule({
      imports: [SignUpPage],
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
    vi.useRealTimers();
  });

  async function settle(fixture: { whenStable(): Promise<unknown>; detectChanges(): void }) {
    await new Promise((resolve) => setTimeout(resolve, 0));
    await fixture.whenStable();
    fixture.detectChanges();
  }

  function type(element: HTMLElement, id: string, value: string): void {
    const input = element.querySelector<HTMLInputElement>(`#${id}`)!;
    input.value = value;
    input.dispatchEvent(new Event('input'));
  }

  it('renders one level-one heading and labelled fields', () => {
    const fixture = TestBed.createComponent(SignUpPage);
    fixture.detectChanges();
    const element = fixture.nativeElement as HTMLElement;

    expect(element.querySelectorAll('h1')).toHaveLength(1);
    expect(element.querySelector('label[for="signup-name"]')?.textContent).toContain(
      'Display name',
    );
    expect(element.querySelector('label[for="signup-email"]')?.textContent).toContain('Email');
  });

  it('shows required errors and sends nothing when submitted empty', async () => {
    const fixture = TestBed.createComponent(SignUpPage);
    fixture.detectChanges();
    const element = fixture.nativeElement as HTMLElement;

    element.querySelector('form')!.dispatchEvent(new Event('submit'));
    await settle(fixture);

    expect(element.querySelector('#signup-email-error')?.textContent).toContain(
      'This field is required.',
    );
    expect(element.querySelector('#signup-email')?.getAttribute('aria-invalid')).toBe('true');
    httpTesting.expectNone('/api/identity/auth/signup');
  });

  it('rejects a malformed email before calling the API', async () => {
    const fixture = TestBed.createComponent(SignUpPage);
    fixture.detectChanges();
    const element = fixture.nativeElement as HTMLElement;

    type(element, 'signup-name', 'Ada');
    type(element, 'signup-email', 'not-an-email');
    element.querySelector('form')!.dispatchEvent(new Event('submit'));
    await settle(fixture);

    expect(element.querySelector('#signup-email-error')?.textContent).toContain(
      'Enter a valid email address.',
    );
    httpTesting.expectNone('/api/identity/auth/signup');
  });

  it('posts the form with the current culture and shows the generic check-email message', async () => {
    const fixture = TestBed.createComponent(SignUpPage);
    fixture.detectChanges();
    const element = fixture.nativeElement as HTMLElement;

    type(element, 'signup-name', 'Ada');
    type(element, 'signup-email', 'ada@example.com');
    element.querySelector('form')!.dispatchEvent(new Event('submit'));
    await settle(fixture);

    const request = httpTesting.expectOne('/api/identity/auth/signup');
    expect(request.request.body).toEqual({
      displayName: 'Ada',
      email: 'ada@example.com',
      culture: 'en-US',
    });
    request.flush(null, { status: 202, statusText: 'Accepted' });
    await settle(fixture);

    expect(element.querySelector('[role="status"]')?.textContent).toContain('Check your email.');
  });

  it('disables resend during the cooldown and resends once it has passed', async () => {
    vi.useFakeTimers({ toFake: ['setInterval', 'clearInterval'] });
    const fixture = TestBed.createComponent(SignUpPage);
    fixture.detectChanges();
    const element = fixture.nativeElement as HTMLElement;

    type(element, 'signup-name', 'Ada');
    type(element, 'signup-email', 'ada@example.com');
    element.querySelector('form')!.dispatchEvent(new Event('submit'));
    await settle(fixture);
    httpTesting
      .expectOne('/api/identity/auth/signup')
      .flush(null, { status: 202, statusText: 'Accepted' });
    await settle(fixture);

    const resend = element.querySelector<HTMLButtonElement>('button[data-action="resend"]')!;
    expect(resend.disabled).toBe(true);
    expect(element.textContent).toContain('You can request another email in 60 seconds.');

    vi.advanceTimersByTime(60_000);
    fixture.detectChanges();
    expect(resend.disabled).toBe(false);

    resend.click();
    await settle(fixture);
    httpTesting
      .expectOne('/api/identity/auth/confirmation/resend')
      .flush(null, { status: 202, statusText: 'Accepted' });
    await settle(fixture);

    expect(resend.disabled).toBe(true);
    expect(element.textContent).toContain('We sent the email again.');
  });

  it('announces a rate limit with the retry time in an alert', async () => {
    const fixture = TestBed.createComponent(SignUpPage);
    fixture.detectChanges();
    const element = fixture.nativeElement as HTMLElement;

    type(element, 'signup-name', 'Ada');
    type(element, 'signup-email', 'ada@example.com');
    element.querySelector('form')!.dispatchEvent(new Event('submit'));
    await settle(fixture);
    httpTesting
      .expectOne('/api/identity/auth/signup')
      .flush(
        { code: 'rateLimited' },
        { status: 429, statusText: 'Too Many Requests', headers: { 'Retry-After': '30' } },
      );
    await settle(fixture);

    expect(element.querySelector('[role="alert"]')?.textContent).toContain(
      'Too many attempts. Try again in 30 seconds.',
    );
  });
});
