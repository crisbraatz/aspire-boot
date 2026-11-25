import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { problemInterceptor } from '../../../core/http/problem.interceptor';
import { I18n } from '../../../core/i18n/i18n';
import en from '../../../../../public/i18n/en-US.json';
import { UserPage, UserSummary } from '../shared/admin-api';
import { UsersPage } from './users.page';

const USERS_URL = '/api/identity/admin/users';

function user(overrides: Partial<UserSummary> = {}): UserSummary {
  return {
    id: 'u1',
    displayName: 'Ana Silva',
    email: 'ana@example.com',
    role: 'User',
    emailConfirmed: true,
    createdAt: '2026-01-15T10:00:00Z',
    avatarUrl: null,
    ...overrides,
  };
}

function pageOf(items: UserSummary[], page = 1, total = items.length): UserPage {
  return { items, page, pageSize: 20, total };
}

describe('UsersPage', () => {
  let fixture: ComponentFixture<UsersPage>;
  let element: HTMLElement;
  let httpTesting: HttpTestingController;

  beforeEach(async () => {
    TestBed.configureTestingModule({
      imports: [UsersPage],
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
  });

  afterEach(() => {
    httpTesting.verify();
    localStorage.clear();
  });

  /** Creates the page and answers its first list request. */
  async function open(page: UserPage): Promise<void> {
    fixture = TestBed.createComponent(UsersPage);
    element = fixture.nativeElement as HTMLElement;
    fixture.detectChanges();
    httpTesting.expectOne((req) => req.url === USERS_URL).flush(page);
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

  it('shows the heading and one table row and one card per user', async () => {
    await open(pageOf([user(), user({ id: 'u2', displayName: 'Bruno Costa', role: 'Admin' })]));

    expect(queryOne('h1')?.textContent).toContain('Users');
    expect(element.querySelectorAll('tbody tr')).toHaveLength(2);
    expect(element.querySelectorAll('[data-testid="user-card"]')).toHaveLength(2);
  });

  it('gives the table column headers and a caption', async () => {
    await open(pageOf([user()]));

    const table = queryOne('table');
    expect(table?.querySelector('caption')).not.toBeNull();
    const headers = table?.querySelectorAll('thead th[scope="col"]') ?? [];
    expect(headers.length).toBeGreaterThanOrEqual(4);
  });

  it('shows initials when a user has no avatar and the picture when one is given', async () => {
    await open(
      pageOf([
        user(),
        user({
          id: 'u2',
          displayName: 'Bruno Costa',
          avatarUrl: '/api/identity/admin/users/u2/avatar/1',
        }),
      ]),
    );

    const images = element.querySelectorAll<HTMLImageElement>('ab-avatar img');
    expect(images).toHaveLength(2);
    expect(images[1].getAttribute('src')).toBe('/api/identity/admin/users/u2/avatar/1');
    expect(element.textContent).toContain('AS');
  });

  it('shows the empty state when no user matches', async () => {
    await open(pageOf([], 1, 0));

    expect(element.textContent).toContain('No users match these filters.');
    expect(queryOne('table')).toBeNull();
  });

  it('shows an alert when the list cannot be loaded', async () => {
    fixture = TestBed.createComponent(UsersPage);
    element = fixture.nativeElement as HTMLElement;
    fixture.detectChanges();
    httpTesting
      .expectOne((req) => req.url === USERS_URL)
      .flush({ code: 'unknown' }, { status: 500, statusText: 'Server Error' });
    await settle();

    const alert = queryOne('[role="alert"]');
    expect(alert?.textContent).toContain('Something went wrong');
  });

  it('labels the search field and debounces the request', async () => {
    await open(pageOf([user()]));
    const search = queryOne<HTMLInputElement>('input[type="search"]');
    expect(element.querySelector(`label[for="${search?.id}"]`)?.textContent).toContain(
      'Search by name or email',
    );

    search!.value = 'ana';
    search!.dispatchEvent(new Event('input'));
    httpTesting.expectNone((req) => req.url === USERS_URL);

    await new Promise((resolve) => setTimeout(resolve, 350));
    const req = httpTesting.expectOne((r) => r.url === USERS_URL);
    expect(req.request.params.get('search')).toBe('ana');
    expect(req.request.params.get('page')).toBe('1');
    req.flush(pageOf([user()]));
    await settle();
  });

  it('filters by role and goes back to the first page', async () => {
    await open(pageOf([user()], 1, 45));
    button('next')!.click();
    httpTesting
      .expectOne((r) => r.url === USERS_URL && r.params.get('page') === '2')
      .flush(pageOf([user()], 2, 45));
    await settle();

    const select = queryOne<HTMLSelectElement>('select#role-filter');
    expect(element.querySelector(`label[for="role-filter"]`)?.textContent).toContain('Role');
    select!.value = 'Admin';
    select!.dispatchEvent(new Event('change'));
    await settle();

    const req = httpTesting.expectOne((r) => r.url === USERS_URL);
    expect(req.request.params.get('role')).toBe('Admin');
    expect(req.request.params.get('page')).toBe('1');
    req.flush(pageOf([user({ role: 'Admin' })]));
    await settle();
  });

  it('moves between pages and disables the buttons at the ends', async () => {
    await open(pageOf([user()], 1, 45));

    expect(button('previous')?.disabled).toBe(true);
    expect(button('next')?.disabled).toBe(false);
    expect(element.textContent).toContain('Page 1 of 3');

    button('next')!.click();
    httpTesting
      .expectOne((r) => r.url === USERS_URL && r.params.get('page') === '2')
      .flush(pageOf([user()], 2, 45));
    await settle();
    expect(button('previous')?.disabled).toBe(false);
  });

  it('disables next on the last page', async () => {
    await open(pageOf([user()], 1, 1));

    expect(button('next')?.disabled).toBe(true);
    expect(button('previous')?.disabled).toBe(true);
  });

  it('links each display name in the table and the cards to the user detail page', async () => {
    await open(pageOf([user(), user({ id: 'u2', displayName: 'Bruno Costa', role: 'Admin' })]));

    const rowLinks = Array.from(element.querySelectorAll<HTMLAnchorElement>('tbody a'));
    expect(rowLinks.map((link) => link.getAttribute('href'))).toEqual([
      '/admin/users/u1',
      '/admin/users/u2',
    ]);
    expect(rowLinks.map((link) => link.textContent?.trim())).toEqual(['Ana Silva', 'Bruno Costa']);

    const cardLinks = Array.from(
      element.querySelectorAll<HTMLAnchorElement>('[data-testid="user-card"] a'),
    );
    expect(cardLinks.map((link) => link.getAttribute('href'))).toEqual([
      '/admin/users/u1',
      '/admin/users/u2',
    ]);
    expect(cardLinks.map((link) => link.textContent?.trim())).toEqual(['Ana Silva', 'Bruno Costa']);
  });

  it('keeps the search and pagination controls in a labelled navigation region', async () => {
    await open(pageOf([user()], 1, 1));

    expect(queryOne('nav[aria-label="Pagination"]')).not.toBeNull();
    expect(queryOne('[role="status"]')?.textContent).toContain('Page 1 of 1');
  });
});
