import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { problemInterceptor } from '../../../core/http/problem.interceptor';
import { I18n } from '../../../core/i18n/i18n';
import { Session } from '../../shared/session';
import { AccountHomePage } from './account-home.page';

const ENGLISH = {
  'account.home.heading': 'Your account',
  'account.home.signedInAs': 'You are signed in as {name}.',
  'account.home.managePasskeys': 'Manage passkeys',
  'account.home.signOut': 'Sign out',
  'errors.unexpected': 'Something went wrong. Please try again.',
};

describe('AccountHomePage', () => {
  let httpTesting: HttpTestingController;

  beforeEach(async () => {
    TestBed.configureTestingModule({
      imports: [AccountHomePage],
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
    const session = TestBed.inject(Session);
    const me = session.refresh();
    httpTesting.expectOne('/api/identity/account/me').flush({
      id: 'user-1',
      displayName: 'Ada Lovelace',
      email: 'ada@example.com',
      culture: 'en-US',
    });
    await me;
  });

  afterEach(() => {
    httpTesting.verify();
  });

  async function settle(fixture: { whenStable(): Promise<unknown>; detectChanges(): void }) {
    await new Promise((resolve) => setTimeout(resolve, 0));
    await fixture.whenStable();
    fixture.detectChanges();
  }

  it('greets the signed-in user by display name with one heading', () => {
    const fixture = TestBed.createComponent(AccountHomePage);
    fixture.detectChanges();
    const element = fixture.nativeElement as HTMLElement;

    expect(element.querySelectorAll('h1')).toHaveLength(1);
    expect(element.textContent).toContain('You are signed in as Ada Lovelace.');
  });

  it('links to passkey management', () => {
    const fixture = TestBed.createComponent(AccountHomePage);
    fixture.detectChanges();
    const element = fixture.nativeElement as HTMLElement;

    expect(element.querySelector('a[href="/account/passkeys"]')?.textContent).toContain(
      'Manage passkeys',
    );
  });

  it('signs out through the API and returns to the home page', async () => {
    const navigate = vi.spyOn(TestBed.inject(Router), 'navigateByUrl').mockResolvedValue(true);
    const fixture = TestBed.createComponent(AccountHomePage);
    fixture.detectChanges();
    const element = fixture.nativeElement as HTMLElement;

    element.querySelector<HTMLButtonElement>('button[data-action="sign-out"]')!.click();
    await settle(fixture);
    httpTesting.expectOne('/api/identity/auth/signout').flush(null);
    await settle(fixture);

    expect(TestBed.inject(Session).isSignedIn()).toBe(false);
    expect(navigate).toHaveBeenCalledWith('/');
  });
});
