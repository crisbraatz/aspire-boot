import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { Session } from '../../identity/shared/session';
import { AppShell } from './app-shell';

const ANA = {
  id: 'u1',
  displayName: 'Ana Lima',
  email: 'ana@example.com',
  culture: 'en-US',
  avatarUrl: null,
};

describe('AppShell header', () => {
  let fixture: ComponentFixture<AppShell>;
  let httpTesting: HttpTestingController;

  beforeEach(async () => {
    TestBed.configureTestingModule({
      providers: [provideRouter([]), provideHttpClient(), provideHttpClientTesting()],
    });
    httpTesting = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(AppShell);
    fixture.detectChanges();
    httpTesting.expectOne('/api/identity/account/me').flush(ANA);
    await fixture.whenStable();
    fixture.detectChanges();
  });

  afterEach(() => {
    httpTesting.verify();
    localStorage.clear();
  });

  function accountLink(): HTMLAnchorElement {
    return fixture.nativeElement.querySelector('a[href="/account"]') as HTMLAnchorElement;
  }

  it('shows the initials and the display name in the account link', () => {
    expect(accountLink().textContent).toContain('Ana Lima');
    expect(accountLink().querySelector('[aria-hidden="true"]')?.textContent?.trim()).toBe('AL');
  });

  it('shows the picture once the avatar url is patched into the session', () => {
    TestBed.inject(Session).patchUser({ avatarUrl: '/api/identity/account/avatar/4' });
    fixture.detectChanges();

    expect(accountLink().querySelector('img')?.getAttribute('src')).toBe(
      '/api/identity/account/avatar/4',
    );
  });

  it('updates the name immediately after a patch', () => {
    TestBed.inject(Session).patchUser({ displayName: 'Bia Costa' });
    fixture.detectChanges();

    expect(accountLink().textContent).toContain('Bia Costa');
    expect(accountLink().textContent).not.toContain('Ana Lima');
  });
});
