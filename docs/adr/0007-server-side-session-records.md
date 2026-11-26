# ADR 0007: Server-side session records

- Status: accepted
- Date: 2026-11-26
- Feature: 004 (my sessions)
- Extends: ADR 0005 (the per-request check now also reads a session record), ADR 0006 (audit exceptions for the heartbeat and cleanup)
- Related: ADR 0003 (passwordless sign-in), ADR 0002 (rate limits)

## Context
Until now a signed-in browser was only a cookie. The server could invalidate every cookie of an account at once (security stamp, ADR 0005), but it could not list the devices of an account, end one device, or say when a device was last used. Users asked for that view, and the API must also support it for the future mobile app.

Sessions therefore need a server-side record. The record must be cheap to check on every request, must be revoked at once from any API instance, and must not store personal data that the product does not need. ADR 0005 already sets the cost rule: one Redis read per authenticated request, with a database fallback that fails safe.

## Decision

### Data model
- A `UserSession` aggregate in `Core/Identity/Domain` maps to table `user_sessions`. Its key `Id` is a client-generated Guid v7 (`Guid.CreateVersion7()`), as ADR 0006 requires for audited entities.
- Columns: `user_id` (foreign key to `AspNetUsers`, cascade delete), `created_at`, `last_seen_at`, `expires_at`, `revoked_at` (nullable), `is_persistent`, `user_agent` (a summary, at most 64 characters) and `ip_masked` (at most 64 characters).
- Indexes: `(user_id, revoked_at, expires_at)` for the list, `expires_at` and `revoked_at` for the cleanup.
- **Exception to the `xmin` convention.** Other aggregates that change concurrently map PostgreSQL `xmin` as a row version (`users`, feature 002). `user_sessions` deliberately has no concurrency token. Revoke is idempotent: the only transition is `revoked_at` from null to a timestamp, and a second revoke changes nothing. The heartbeat's `UPDATE` matches only rows with `revoked_at IS NULL AND expires_at > now`. So a heartbeat that runs between another request's load and save cannot cause a `DbUpdateConcurrencyException` on a revocation. If it could, the revoke would fail with a 409 and the user would believe a device was ended when it was not. Under PostgreSQL READ COMMITTED both orders are safe: if the heartbeat writes first, the revoke waits and then applies; if the revoke writes first, the heartbeat matches no row. Neither order can un-revoke or extend a revoked session. `AGENTS.md` rule 5 and the `efcore-postgresql` skill record this exception.

### Sign-in and the session claim
- Every sign-in creates a row and puts its id in the cookie as the claim `aspireboot:sid` (constant `SessionClaims.SessionId`).
- The claim is added with `SignInManager.SignInWithClaimsAsync`. A custom `IUserClaimsPrincipalFactory` was rejected: the factory has no access to the per-sign-in session id, and it also runs on refresh, where a new id would be wrong.
- All sign-ins go through the shared component `SessionSignIn` (`Api/Identity/Shared`). The routes are passkey sign-in, registration completion and any future sign-in path.
- `SignInManager.RefreshSignInAsync` must not be used. In ASP.NET Core 10 it rebuilds the principal and carries over only the `amr` claim, so the sid claim would be lost. `SessionSignIn.RefreshAsync` replaces it. It authenticates the application scheme, reads the sid and the `AuthenticationProperties`, checks that the authenticated user id equals `user.Id` (otherwise it signs out, as `RefreshSignInAsync` does since .NET 9), and calls `SignInWithClaimsAsync` with the kept `amr` and `AuthenticationMethod` claims and the sid. Persistence (`IsPersistent`) and the session id are kept, and a test checks that the cookie `Expires` value survives a refresh of a persistent session.
- Lifetime: `ExpiresAt = now + CookieAuthenticationOptions.ExpireTimeSpan` (8 hours). It slides with the cookie on each heartbeat.
- Sources: [SignInManager.SignInWithClaimsAsync](https://learn.microsoft.com/en-us/dotnet/api/microsoft.aspnetcore.identity.signinmanager-1.signinwithclaimsasync), [IUserClaimsPrincipalFactory](https://learn.microsoft.com/en-us/dotnet/api/microsoft.aspnetcore.identity.iuserclaimsprincipalfactory-1), [SignInManager.RefreshSignInAsync](https://learn.microsoft.com/en-us/dotnet/api/microsoft.aspnetcore.identity.signinmanager-1.refreshsigninasync) and the ASP.NET Core source, [SignInManager.cs](https://github.com/dotnet/aspnetcore/blob/main/src/Identity/Core/src/SignInManager.cs).

### Per-request check
- `SecurityStampPrincipalValidator.IsValidAsync` (the cookie `OnValidatePrincipal` handler) runs two checks **one after the other**: first the security stamp (ADR 0005), then the session. They are sequential because the scoped `AppDbContext` cannot run two queries in parallel.
- A missing or unparseable sid, a foreign user id, a revoked row, an expired row or a missing row rejects the principal. Pre-release cookies therefore stop working once (see "Release" below).
- The session state comes from a **separate HybridCache entry per session**, not from one combined entry. The stamp is per user and sessions are per session. Combining them would force a rewrite of every session of the user on each change.
  - Key: `identity:session:{sessionId:D}`. Value: `SessionState(Guid UserId, bool Active, DateTimeOffset ExpiresAt, DateTimeOffset LastSeenAt)`.
  - Options: `Expiration = 60 s` (maximum lifetime), `Flags = DisableLocalCache`, no tags, the same as ADR 0005. Redis is the single source, so revocation is visible to every API instance on its next request.
  - A missing row is cached as `Active = false`.
- If the cache throws, the database is read. If the database also fails, the request is rejected. The request is never accepted on a failed check.

### Last-seen heartbeat
- After a successful check, if `now - LastSeenAt >= UserSessions:LastSeenThrottle` (default 5 minutes, validated range 1 to 60 minutes), the validator runs one conditional `ExecuteUpdate`:
  `WHERE id = @id AND user_id = @user AND revoked_at IS NULL AND expires_at > now AND last_seen_at < now - throttle`, setting `last_seen_at = now` and `expires_at = now + cookie lifetime`.
- The time condition means that among concurrent requests or instances only one writes. The others match zero rows, and no lock is taken in the application.
- **Cache invalidation, not overwrite.** When the heartbeat matches one row, the validator calls `InvalidateAsync`, which runs `RemoveAsync` and never `SetAsync`. An in-memory state is stale by definition: it may say `Active = true` for a session that a revocation committed a moment ago. Writing it back would undo the revocation for up to 60 s. Removing the key makes the next read load the committed state. A unit test checks the ordering.
- **Excluded from audit.** `ExecuteUpdate` bypasses the change tracker, so the heartbeat writes no `audit_entries` row. This is deliberate: `last_seen_at` is telemetry, not a change made by a person, and auditing it would write a row every 5 minutes per device. Creation and revocation are tracked and audited. This is the exception that ADR 0006 residual risk 4 requires to be recorded here.
- Failures of the heartbeat are logged (session id and error type) and ignored, so a failed write never rejects a session.
- Sources: [EF Core ExecuteUpdate and ExecuteDelete](https://learn.microsoft.com/en-us/ef/core/saving/execute-insert-update-delete), [HybridCache](https://learn.microsoft.com/en-us/aspnet/core/performance/caching/hybrid).

### Revocation
- `UserSessionRevocation.RevokeAsync(db, userId, exceptSessionId?, now, ct)` loads the active rows as **tracked** entities, calls `Revoke(now)` on each, and returns their ids. The caller saves them in its own transaction. Revocation is therefore audited and atomic with the change that causes it.
- Stamp changes revoke the affected rows in the **same transaction** as the stamp change:
  - Recovery and registration completion revoke all earlier sessions of the account.
  - A role change revokes all sessions of the target.
  - Passkey removal revokes all sessions except the acting one (`exceptSessionId` is its sid).
- **Row expiry.** The row expiry equals the cookie ticket expiry (`ExpireTimeSpan`) for persistent and non-persistent sessions alike. A browser-session cookie is dropped by the browser on close, but its row stays listed as active until it expires or is ended. This is safe because nobody holds the cookie, and the user can end it from the list. A shorter expiry for non-persistent rows was not adopted, to keep the row aligned with the ticket the server validates.
- Sign-out revokes the current row, saves, and then signs out. Without a row it still signs out.
- **Post-commit order.** After commit the caller runs `StampChangedAsync` (ADR 0005) and then `SessionsChangedAsync(ids)`. Each one never throws, so a failure in one never skips the other. Either alone is enough to reject the other sessions: the stamp rejects the cookies, and the session cache rejects the rows.
- `SessionsChangedAsync` re-reads the committed rows and writes each `SessionState` with `SetAsync`. On failure it retries once, then calls `RemoveAsync`. It ignores the request's cancellation token, because it must finish after the commit.
- **Registration completion.** `CompleteRegistration` creates the new session row **inside** the registration transaction (`SessionSignIn.CreateSession`, no save of its own), then commits, then issues the cookie with `IssueCookieAsync`. If the cookie cannot be issued after the commit, the row is an orphan: nobody holds its sid. It expires on its own and the cleanup removes it. This residual risk is accepted, and a test covers the failure path.

### Accepted cache-refill race
A `GetOrCreate` that misses the cache can read the row from the database **before** a revocation commits, then write `Active = true` into the cache **after** `SessionsChangedAsync` has removed or overwritten the key. The window is the length of that one database read. The entry then reports the session as active for at most the 60 s maximum lifetime, after which it expires and the next read loads the committed state. This is the same residual risk ADR 0005 accepts for the security stamp (its residual risk 2). We accept it rather than add locking, because a lock on every cache miss would add a database round trip per request. The stale window is bounded by the 60 s lifetime and does not grow.

### Endpoints
All three endpoints are under `/api/identity/account/sessions`, require authentication, use the `Authenticated` rate-limit policy (partitioned by user) and return Problem Details for errors.

- `GET /api/identity/account/sessions` (`ListSessions`): at most the 50 most recently seen active sessions, newest last-seen first. Items are `{ id, browser, os, createdAt, lastSeenAt, ipMasked, current }`. Read with `AsNoTracking` and a projection. The response has `Cache-Control: no-store`.
- `DELETE /api/identity/account/sessions/{id:guid}` (`EndSession`):
  - Another user's id, or an unknown id, returns `404 identity.sessionNotFound`. The response is the same for both, so the endpoint is not an existence oracle.
  - The current session returns `409 identity.currentSession` ("use sign-out"). A DELETE that silently signs the caller out would surprise API clients, including the future mobile app, and sign-out already exists.
  - An already revoked or expired session returns `204`. The call is idempotent.
  - Otherwise it revokes, saves, calls `SessionsChangedAsync` and returns `204`.
- `POST /api/identity/account/sessions/end-others` (`EndOtherSessions`): revokes every active session except the current one, calls `SessionsChangedAsync`, and returns `200 { ended }`.

State-changing methods use antiforgery like every unsafe `/api` request.

### Cleanup
- Message `CleanupUserSessions` in `Core/Messaging/Identity`. It is scheduled as `ScheduleRecurring<CleanupUserSessions>("17 3 * * *", TimeZoneInfo.Utc).SkipWhileRunning()`, so the cron **runs in UTC** at 03:17 every day, on one node.
- The handler deletes rows where `revoked_at < now - Retention` or `expires_at < now - Retention`, with `ExecuteDeleteAsync` in batches of 1000 selected by id. `Retention` comes from `UserSessions:Retention` (default 30 days, validated 1 to 3650 days).
- The deletes are **unaudited housekeeping**, with the same justification as the heartbeat. Retention keeps the most recent history for the user and for incident review.
- With `SkipWhileRunning` a failed run is logged and the next daily run catches up. Missed runs are skipped, which is harmless for cleanup.
- Counter: `aspireboot.identity.sessions.cleaned` (rows deleted).
- The schedule is tested through `IRecurringScheduleControl` (Wolverine's timer agent keeps no tracking row for a test to wait on).

### Privacy
- **IP.** The stored value is masked, never the raw address. IPv4 keeps the first two octets (`a.b.*.*`). IPv6 keeps the first 48 bits (`xxxx:xxxx:xxxx::/48`). An unknown address is stored as `unknown`.
  - The address comes from `HttpContext.Connection.RemoteIpAddress` after `UseForwardedHeaders`. Forwarded headers are trusted only from `ReverseProxy:KnownNetworks` (Caddy), with `ForwardLimit = 1` (ADR 0002). A header from any other peer is ignored, so a client cannot spoof the stored address.
  - Tests: a peer inside `KnownNetworks` with a forwarded address stores the masked forwarded address. A peer outside `KnownNetworks` with the same header stores its own masked address. Both are checked through an `IStartupFilter` that sets `RemoteIpAddress` before `UseForwardedHeaders` runs.
- **User agent.** The stored value is a summary of two tokens, browser family and operating system (for example `chrome|macos`). It is produced by an **in-house parser** with plain ordered substring matching, so no regex runs over attacker-controlled input and no new dependency is added. The full user agent is never stored. Unknown or empty input becomes `unknown`, and the parser never throws.
- **Logs.** Logs contain the user id, the session id and counts only. Neither the IP nor the user agent is logged. An integration test captures the logs of the sign-in path and of the session endpoints and checks that neither value appears.

### Observability
- Counter `aspireboot.identity.sessions.ended` (tags `reason`), described as user, others, signout or stamp. `user` (EndSession), `others` (EndOtherSessions), `signout` (sign-out, only when the row was active) and `stamp` (role change, passkey removal and registration completion, by the number of rows revoked) are all emitted. Integration tests check the reasons with a `MeterListener`.
- Counter `aspireboot.identity.sessions.cleaned`.
- Log messages are source-generated `LoggerMessage` with event ids. They carry no PII, secrets, IP or user agent.

### Release
- **One-time forced sign-in.** Every cookie issued before this release has no sid claim, so the validator rejects it. Every user is signed out once and signs in again with a passkey. The frontend redirects to sign-in on the resulting 401. This is intended and is documented in `README.md` and in `docs/features/004-user-sessions.md`. No migration can create session rows for old cookies, because the cookie is the only proof of the session.

### Configuration
- `UserSessions:LastSeenThrottle` (default `00:05:00`, validated `00:01:00` to `01:00:00`), API.
- `UserSessions:Retention` (default `30.00:00:00`, validated `1` to `3650` days), read by the Worker only (`UserSessionsCleanupOptions`).
- Defaults are in `UserSessionOptions` (API) and `UserSessionsCleanupOptions` (Worker), which validate ranges on start. The values are documented in `deploy/.env.example` and `README.md`.

## Sources
- ASP.NET Core Identity, `SignInManager.SignInWithClaimsAsync`, `IUserClaimsPrincipalFactory` and `SignInManager.RefreshSignInAsync`: links above.
- ASP.NET Core cookie authentication, `OnValidatePrincipal`: https://learn.microsoft.com/en-us/aspnet/core/security/authentication/cookie
- ASP.NET Core forwarded headers: https://learn.microsoft.com/en-us/aspnet/core/host-and-deploy/proxy-load-balancer
- HybridCache (L1 and L2, `DisableLocalCache`, expiration): https://learn.microsoft.com/en-us/aspnet/core/performance/caching/hybrid
- EF Core `ExecuteUpdate` and `ExecuteDelete`: https://learn.microsoft.com/en-us/ef/core/saving/execute-insert-update-delete
- Wolverine recurring messages: https://wolverinefx.net/guide/messaging/recurring
- OWASP Session Management Cheat Sheet: https://cheatsheetseries.owasp.org/cheatsheets/Session_Management_Cheat_Sheet.html
- OWASP Authentication Cheat Sheet: https://cheatsheetseries.owasp.org/cheatsheets/Authentication_Cheat_Sheet.html
- ADR 0005 and ADR 0006 in this repository.

## Consequences
- Users can see their devices, end one, or end all others. Revocation from any API instance takes effect on the next request of every instance. The only exception is the cache-refill race described above, which is bounded by 60 s.
- Every authenticated request now reads one more cache entry (Redis). The cost is accepted, as in ADR 0005. With Redis down, each request reads the session row from PostgreSQL (fail safe).
- The `user_sessions` table grows with sign-ins. The daily cleanup bounds it to the retention window plus the active rows.
- Every new sign-in path must go through `SessionSignIn`, and every new stamp change must revoke session rows in the same transaction and call `SessionsChangedAsync` after the commit. Both rules are in `AGENTS.md` rule 11.
- The one-time forced sign-in at release is a known, accepted user-facing cost.

## Residual risks (accepted)
1. **Cache-refill race.** As described above: a miss read just before a revocation commits can repopulate `Active = true` for at most 60 s.
2. **Orphan session row.** If the cookie cannot be issued after a registration commit, the row stays until it expires and the cleanup removes it. Nobody can use it, because no cookie carries its sid.
3. **Heartbeat and cleanup are not audited.** Accepted for the reasons above. The data they change is telemetry or expired housekeeping.
4. **Failed invalidation after a commit.** If both the overwrite and the removal of a session entry fail, that entry can report a stale state for at most 60 s, and the failure is logged at error level.
5. **Redis outage raises database load.** Every authenticated request reads the session row until Redis returns. The database check still fails safe.
6. **Cookie failure after registration.** The endpoint answers a Problem Details with `identity.signInRequired`, and the frontend tells the user the account is ready and to sign in. Nothing is lost: the account is confirmed and the passkey is saved.
7. **Single-node scheduling assumption.** The cleanup runs on one node through a singleton agent. If the agent does not run for a day, the next run catches up.
