import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { problemInterceptor } from '../../../core/http/problem.interceptor';
import { AdminApi, UserPage } from './admin-api';

const PAGE: UserPage = {
  items: [
    {
      id: 'u1',
      displayName: 'Ana Silva',
      email: 'ana@example.com',
      role: 'Admin',
      emailConfirmed: true,
      createdAt: '2026-01-15T10:00:00Z',
      avatarUrl: null,
    },
  ],
  page: 2,
  pageSize: 20,
  total: 21,
};

describe('AdminApi', () => {
  let api: AdminApi;
  let httpTesting: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(withInterceptors([problemInterceptor])),
        provideHttpClientTesting(),
      ],
    });
    api = TestBed.inject(AdminApi);
    httpTesting = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpTesting.verify());

  it('lists users with paging parameters only when filters are set', async () => {
    const result = api.list({ page: 2, pageSize: 20 });
    const req = httpTesting.expectOne(
      (request) => request.url === '/api/identity/admin/users' && request.method === 'GET',
    );
    expect(req.request.params.get('page')).toBe('2');
    expect(req.request.params.get('pageSize')).toBe('20');
    expect(req.request.params.has('search')).toBe(false);
    expect(req.request.params.has('role')).toBe(false);
    req.flush(PAGE);

    expect(await result).toEqual(PAGE);
  });

  it('sends the trimmed search and the role filter when given', async () => {
    const result = api.list({ search: 'ana', role: 'Admin', page: 1, pageSize: 20 });
    const req = httpTesting.expectOne((request) => request.url === '/api/identity/admin/users');
    expect(req.request.params.get('search')).toBe('ana');
    expect(req.request.params.get('role')).toBe('Admin');
    req.flush(PAGE);
    await result;
  });

  it('omits an empty search', async () => {
    const result = api.list({ search: '', page: 1, pageSize: 20 });
    const req = httpTesting.expectOne((request) => request.url === '/api/identity/admin/users');
    expect(req.request.params.has('search')).toBe(false);
    req.flush(PAGE);
    await result;
  });

  it('loads one user by id, encoding the id', async () => {
    const result = api.get('a/b');
    const req = httpTesting.expectOne('/api/identity/admin/users/a%2Fb');
    expect(req.request.method).toBe('GET');
    req.flush({ ...PAGE.items[0], passkeyCount: 2, culture: 'pt-BR' });

    expect((await result).passkeyCount).toBe(2);
  });

  it('changes a role with a PUT body', async () => {
    const result = api.changeRole('u1', 'User');
    const req = httpTesting.expectOne('/api/identity/admin/users/u1/role');
    expect(req.request.method).toBe('PUT');
    expect(req.request.body).toEqual({ role: 'User' });
    req.flush(null, { status: 204, statusText: 'No Content' });

    await result;
  });
});
