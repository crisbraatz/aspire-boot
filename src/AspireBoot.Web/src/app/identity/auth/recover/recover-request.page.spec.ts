import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { problemInterceptor } from '../../../core/http/problem.interceptor';
import { I18n } from '../../../core/i18n/i18n';
import { RecoverRequestPage } from './recover-request.page';

const ENGLISH = {
  'auth.recover.heading': 'Recover your account',
  'auth.recover.lead': 'Lost your passkey? We will email you a recovery link.',
  'auth.recover.email.label': 'Email',
  'auth.recover.submit': 'Send recovery link',
  'auth.recover.sent':
    'Check your email. If this address belongs to an account, we have sent a recovery link.',
  'auth.validation.required': 'This field is required.',
  'auth.validation.email': 'Enter a valid email address.',
  'errors.unexpected': 'Something went wrong. Please try again.',
  'errors.rateLimited': 'Too many attempts. Try again in {seconds} seconds.',
};

describe('RecoverRequestPage', () => {
  let httpTesting: HttpTestingController;

  beforeEach(async () => {
    TestBed.configureTestingModule({
      imports: [RecoverRequestPage],
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
  });

  async function settle(fixture: { whenStable(): Promise<unknown>; detectChanges(): void }) {
    await new Promise((resolve) => setTimeout(resolve, 0));
    await fixture.whenStable();
    fixture.detectChanges();
  }

  it('renders one level-one heading and a labelled email field', () => {
    const fixture = TestBed.createComponent(RecoverRequestPage);
    fixture.detectChanges();
    const element = fixture.nativeElement as HTMLElement;

    expect(element.querySelectorAll('h1')).toHaveLength(1);
    expect(element.querySelector('label[for="recover-email"]')?.textContent).toContain('Email');
  });

  it('rejects an empty or malformed email without calling the API', async () => {
    const fixture = TestBed.createComponent(RecoverRequestPage);
    fixture.detectChanges();
    const element = fixture.nativeElement as HTMLElement;

    element.querySelector('form')!.dispatchEvent(new Event('submit'));
    await settle(fixture);
    expect(element.querySelector('#recover-email-error')?.textContent).toContain(
      'This field is required.',
    );

    const input = element.querySelector<HTMLInputElement>('#recover-email')!;
    input.value = 'not-an-email';
    input.dispatchEvent(new Event('input'));
    element.querySelector('form')!.dispatchEvent(new Event('submit'));
    await settle(fixture);
    expect(element.querySelector('#recover-email-error')?.textContent).toContain(
      'Enter a valid email address.',
    );
    httpTesting.expectNone('/api/identity/auth/recovery');
  });

  it('sends the email and shows the same message whether or not the address is registered', async () => {
    const fixture = TestBed.createComponent(RecoverRequestPage);
    fixture.detectChanges();
    const element = fixture.nativeElement as HTMLElement;

    const input = element.querySelector<HTMLInputElement>('#recover-email')!;
    input.value = 'ada@example.com';
    input.dispatchEvent(new Event('input'));
    element.querySelector('form')!.dispatchEvent(new Event('submit'));
    await settle(fixture);

    const request = httpTesting.expectOne('/api/identity/auth/recovery');
    expect(request.request.body).toEqual({ email: 'ada@example.com' });
    request.flush(null, { status: 202, statusText: 'Accepted' });
    await settle(fixture);

    expect(element.querySelector('[role="status"]')?.textContent).toContain(
      'Check your email. If this address belongs to an account, we have sent a recovery link.',
    );
  });
});
