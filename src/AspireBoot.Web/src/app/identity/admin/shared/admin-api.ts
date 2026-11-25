import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';

/** Mirrors the admin Identity contracts under /api/identity/admin (see the feature plan). */
export type RoleName = 'User' | 'Admin';

export interface UserSummary {
  id: string;
  displayName: string;
  email: string;
  role: RoleName;
  emailConfirmed: boolean;
  createdAt: string;
  /** Versioned avatar URL, or null when the user shows initials. */
  avatarUrl: string | null;
}

export interface UserPage {
  items: UserSummary[];
  page: number;
  pageSize: number;
  total: number;
}

export interface UserDetail extends UserSummary {
  passkeyCount: number;
  culture: string;
}

export interface UserListQuery {
  /** Free text matched against display name and email. Empty means no filter. */
  search?: string;
  /** Role filter. Undefined means all roles. */
  role?: RoleName;
  page: number;
  pageSize: number;
}

const BASE = '/api/identity/admin/users';

/** Typed client for the user administration endpoints. The API enforces the Admin policy. */
@Injectable({ providedIn: 'root' })
export class AdminApi {
  private readonly http = inject(HttpClient);

  list(query: UserListQuery): Promise<UserPage> {
    let params = new HttpParams().set('page', query.page).set('pageSize', query.pageSize);
    const search = query.search?.trim();
    if (search) params = params.set('search', search);
    if (query.role) params = params.set('role', query.role);
    return firstValueFrom(this.http.get<UserPage>(BASE, { params }));
  }

  get(id: string): Promise<UserDetail> {
    return firstValueFrom(this.http.get<UserDetail>(this.userUrl(id)));
  }

  changeRole(id: string, role: RoleName): Promise<void> {
    return firstValueFrom(this.http.put<void>(`${this.userUrl(id)}/role`, { role }));
  }

  private userUrl(id: string): string {
    return `${BASE}/${encodeURIComponent(id)}`;
  }
}
