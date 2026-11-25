import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { problemInterceptor } from '../../../core/http/problem.interceptor';
import { I18n } from '../../../core/i18n/i18n';
import en from '../../../../../public/i18n/en-US.json';
import { Session } from '../../shared/session';
import { AccountHomePage } from './account-home.page';

const MAX_AVATAR_BYTES = 5 * 1024 * 1024;
const PROFILE_URL = '/api/identity/account/profile';
const AVATAR_URL = '/api/identity/account/avatar';

describe('AccountHomePage', () => {
  let httpTesting: HttpTestingController;
  let fixture: ComponentFixture<AccountHomePage>;
  let element: HTMLElement;

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
    httpTesting.expectOne('/i18n/en-US.json').flush(en);
    await loading;
    const session = TestBed.inject(Session);
    const me = session.refresh();
    httpTesting.expectOne('/api/identity/account/me').flush({
      id: 'user-1',
      displayName: 'Ada Lovelace',
      email: 'ada@example.com',
      culture: 'en-US',
      avatarUrl: null,
    });
    await me;
    fixture = TestBed.createComponent(AccountHomePage);
    fixture.detectChanges();
    element = fixture.nativeElement as HTMLElement;
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

  function control<T extends HTMLElement>(selector: string): T {
    const found = element.querySelector<T>(selector);
    if (!found) throw new Error(`missing ${selector}`);
    return found;
  }

  function text(): string {
    return element.textContent ?? '';
  }

  function chooseFile(file: File): void {
    const input = control<HTMLInputElement>('#avatar-file');
    Object.defineProperty(input, 'files', { value: [file], configurable: true });
    input.dispatchEvent(new Event('change'));
  }

  function picture(type: string, size = 1024): File {
    return new File([new Uint8Array(size)], 'me.png', { type });
  }

  function typeName(value: string): void {
    const input = control<HTMLInputElement>('#account-name');
    input.value = value;
    input.dispatchEvent(new Event('input'));
    fixture.detectChanges();
  }

  function submitProfile(): void {
    control<HTMLFormElement>('form[data-form="profile"]').dispatchEvent(
      new Event('submit', { cancelable: true }),
    );
  }

  describe('greeting and navigation', () => {
    it('greets the signed-in user by display name with one heading', () => {
      expect(element.querySelectorAll('h1')).toHaveLength(1);
      expect(element.textContent).toContain('You are signed in as Ada Lovelace.');
    });

    it('links to passkey management at /account/passkeys', () => {
      const link = element.querySelector<HTMLAnchorElement>('a[href="/account/passkeys"]');
      expect(link?.textContent).toContain('Manage passkeys');
    });

    it('signs out through the API and returns to the home page', async () => {
      const navigate = vi.spyOn(TestBed.inject(Router), 'navigateByUrl').mockResolvedValue(true);
      control<HTMLButtonElement>('button[data-action="sign-out"]').click();
      await settle();
      httpTesting.expectOne('/api/identity/auth/signout').flush(null);
      await settle();

      expect(TestBed.inject(Session).isSignedIn()).toBe(false);
      expect(navigate).toHaveBeenCalledWith('/');
    });
  });

  describe('accessibility', () => {
    it('renders the email as read-only text, not an editable field', () => {
      const email = control<HTMLInputElement>('#account-email');
      expect(email.readOnly).toBe(true);
      expect(email.value).toBe('ada@example.com');
    });

    it('gives every form control an accessible label', () => {
      const controls = element.querySelectorAll<HTMLElement>('input, select, textarea');
      expect(controls.length).toBeGreaterThan(0);
      for (const item of Array.from(controls)) {
        const labelled =
          (item as HTMLInputElement).labels?.length || item.getAttribute('aria-label');
        expect(labelled, item.outerHTML).toBeTruthy();
      }
    });

    it('announces status and avatar changes through aria-live regions', () => {
      const regions = element.querySelectorAll('[role="status"][aria-live]');
      expect(regions.length).toBeGreaterThanOrEqual(2);
    });

    it('makes the drop zone keyboard focusable', () => {
      expect(control('[data-action="choose-avatar"]').tabIndex).toBe(0);
    });

    it('opens the file picker when Enter is pressed on the drop zone', () => {
      const input = control<HTMLInputElement>('#avatar-file');
      const click = vi.spyOn(input, 'click').mockImplementation(() => undefined);
      control('[data-action="choose-avatar"]').dispatchEvent(
        new KeyboardEvent('keydown', { key: 'Enter', bubbles: true }),
      );
      expect(click).toHaveBeenCalled();
    });

    it('opens the file picker when the drop zone is clicked', () => {
      const input = control<HTMLInputElement>('#avatar-file');
      const click = vi.spyOn(input, 'click').mockImplementation(() => undefined);
      control('[data-action="choose-avatar"]').click();
      expect(click).toHaveBeenCalled();
    });

    it('marks the drop zone aria-disabled only while an upload is running', async () => {
      const zone = control('[data-action="choose-avatar"]');
      expect(zone.getAttribute('aria-disabled')).toBe('false');

      chooseFile(picture('image/png'));
      await settle();
      expect(zone.getAttribute('aria-disabled')).toBe('true');

      httpTesting.expectOne(AVATAR_URL).flush({ avatarUrl: '/api/identity/account/avatar/4' });
      await settle();
      expect(zone.getAttribute('aria-disabled')).toBe('false');
    });

    it('accepts only JPEG, PNG and WebP in the file input', () => {
      expect(control<HTMLInputElement>('#avatar-file').accept).toBe(
        'image/jpeg,image/png,image/webp',
      );
    });

    it('links the profile picture with its localized alt text', () => {
      expect(element.querySelector('img')).toBeNull();
      expect(text()).toContain('Ada Lovelace');
    });
  });

  describe('display name', () => {
    it('shows a localized error for a name outside 2 to 100 characters, without a request', async () => {
      typeName('A');
      submitProfile();
      await settle();

      expect(text()).toContain('Use between 2 and 100 characters.');
      expect(control('#account-name').getAttribute('aria-invalid')).toBe('true');
      httpTesting.expectNone(PROFILE_URL);
    });

    it('saves the name and patches the signed-in user', async () => {
      typeName('Ada Byron');
      submitProfile();
      await settle();
      httpTesting.expectOne(PROFILE_URL).flush(null);
      await settle();

      expect(TestBed.inject(Session).currentUser()?.displayName).toBe('Ada Byron');
      expect(text()).toContain(en['account.profile.saved']);
    });
  });

  describe('avatar', () => {
    it('uploads a picture and patches the session with the new avatar url', async () => {
      chooseFile(picture('image/png'));
      await settle();
      const request = httpTesting.expectOne(AVATAR_URL);
      expect(request.request.method).toBe('PUT');
      request.flush({ avatarUrl: '/api/identity/account/avatar/2' });
      await settle();

      expect(TestBed.inject(Session).currentUser()?.avatarUrl).toBe(
        '/api/identity/account/avatar/2',
      );
      expect(text()).toContain(en['account.profile.avatar.uploaded']);
    });

    it('rejects a picture above 5 MB with a localized message and without a request', async () => {
      chooseFile(picture('image/png', MAX_AVATAR_BYTES + 1));
      await settle();

      expect(text()).toContain(en['account.profile.avatar.tooLarge']);
      httpTesting.expectNone(AVATAR_URL);
    });

    it('rejects an unsupported type without a request', async () => {
      chooseFile(picture('image/gif'));
      await settle();

      expect(text()).toContain(en['account.profile.avatar.unsupported']);
      httpTesting.expectNone(AVATAR_URL);
    });

    it('uploads a picture dropped on the drop zone', async () => {
      const drop = new Event('drop', { bubbles: true, cancelable: true });
      Object.defineProperty(drop, 'dataTransfer', { value: { files: [picture('image/webp')] } });
      control('[data-action="choose-avatar"]').dispatchEvent(drop);
      await settle();

      const request = httpTesting.expectOne(AVATAR_URL);
      request.flush({ avatarUrl: '/api/identity/account/avatar/3' });
      await settle();
      expect(TestBed.inject(Session).currentUser()?.avatarUrl).toBe(
        '/api/identity/account/avatar/3',
      );
    });

    it('ignores a picture dropped while another upload is running', async () => {
      chooseFile(picture('image/png'));
      await settle();
      const running = httpTesting.expectOne(AVATAR_URL);

      const drop = new Event('drop', { bubbles: true, cancelable: true });
      Object.defineProperty(drop, 'dataTransfer', { value: { files: [picture('image/webp')] } });
      control('[data-action="choose-avatar"]').dispatchEvent(drop);
      await settle();

      httpTesting.expectNone(AVATAR_URL);
      running.flush({ avatarUrl: '/api/identity/account/avatar/5' });
      await settle();
    });

    it('shows the busy message from a 503 avatar.busy problem', async () => {
      chooseFile(picture('image/png'));
      await settle();
      httpTesting
        .expectOne(AVATAR_URL)
        .flush({ code: 'avatar.busy' }, { status: 503, statusText: 'Service Unavailable' });
      await settle();

      expect(text()).toContain(en['account.profile.avatar.busy']);
    });

    it('shows the storage message from a 503 avatar.storageUnavailable problem', async () => {
      chooseFile(picture('image/png'));
      await settle();
      httpTesting
        .expectOne(AVATAR_URL)
        .flush(
          { code: 'avatar.storageUnavailable' },
          { status: 503, statusText: 'Service Unavailable' },
        );
      await settle();

      expect(text()).toContain(en['account.profile.avatar.storageUnavailable']);
    });

    it('asks for confirmation before removing and removes after confirming', async () => {
      TestBed.inject(Session).patchUser({ avatarUrl: '/api/identity/account/avatar/1' });
      fixture.detectChanges();
      control<HTMLButtonElement>('[data-action="remove-avatar"]').click();
      fixture.detectChanges();
      httpTesting.expectNone(AVATAR_URL);

      control<HTMLButtonElement>('[data-action="confirm-remove-avatar"]').click();
      await settle();
      httpTesting.expectOne(AVATAR_URL).flush(null);
      await settle();

      expect(TestBed.inject(Session).currentUser()?.avatarUrl).toBeNull();
      expect(text()).toContain(en['account.profile.avatar.removed']);
    });
  });

  describe('language', () => {
    it('switches the language and saves it to the account', async () => {
      const select = control<HTMLSelectElement>('#account-culture');
      select.value = 'pt-BR';
      select.dispatchEvent(new Event('change'));
      await settle();
      httpTesting.expectOne('/i18n/pt-BR.json').flush({});
      await settle();
      httpTesting.expectOne('/api/identity/account/preferences').flush(null);
      await settle();

      expect(TestBed.inject(I18n).culture()).toBe('pt-BR');
    });
  });
});
