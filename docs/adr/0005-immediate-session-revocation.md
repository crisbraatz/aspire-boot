# ADR 0005: Immediate session revocation with a cached security stamp

- Status: accepted
- Date: 2026-11-25
- Feature: 003 (user administration, immediate revocation and audit trail)
- Related: ADR 0003 (session rules; the interval rule there is superseded by this ADR), ADR 0006 (audit trail)

## Context
Recovery, passkey removal and role changes must end the other sessions of an account at once. ASP.NET Core Identity's built-in `SecurityStampValidator` checks the stamp on a timer, so a session can keep working for a while after the change. That delay matters most for admin access: a demoted Admin must lose access on the next request, not at the next check. A database read on every request is correct but adds a query to each call, so the check has to be cheap.

## Decision
- **Check on every request.** Every authenticated cookie request compares the cookie's security stamp (`AspNet.Identity.SecurityStamp` claim) with the account's current stamp. The check runs in the cookie `OnValidatePrincipal` handler (`SecurityStampPrincipalValidator`). Identity's interval validator is not used, and there is no interval. On a match nothing is written, so the cookie is not rewritten on each request.
- **Cache in HybridCache.** The current stamp is read through `HybridCache` with key `identity:stamp:{id}` (the user id in the `D` format), no tags, a maximum lifetime of 60 s, and `HybridCacheEntryFlags.DisableLocalCache`. The in-process (L1) cache is disabled because HybridCache does not propagate L1 invalidation across instances. Redis (L2) is therefore the single source, and a change is seen by every API instance on its next request. The read and the write use the same options.
- **Invalidate after commit.** Every slice that changes a stamp (registration confirmation and recovery, passkey removal, role change) calls `StampChangedAsync` after its transaction has committed. The method re-reads the committed stamp from the database, so the cache never receives a value that was not persisted, and writes it with `SetAsync`. If the write fails, it retries once, then removes the key with `RemoveAsync`. Only key operations are used: no tags and no group invalidation. The post-commit work ignores the request's cancellation token, because it must finish after the commit. It never throws. If both the write and the removal fail, an error is logged (user id and seconds only), and the 60 s maximum lifetime bounds the stale window.
- **Fail safe.** If the cache throws, the validator reads the stamp straight from the database (`GetStampFromDatabaseAsync`). If the database read also fails, the principal is rejected and the cookie session is signed out. The request is never accepted on a failed check.
- **The acting session is kept.** When the account owner removes a passkey, the slice calls `SignInManager.RefreshSignInAsync`, which re-issues the cookie with the new stamp and keeps its persistence and authentication method. Registration confirmation re-issues the cookie with `SignInAsync`. Other sessions fail the next check.
- **Role changes end the target's sessions.** A change of role updates the stamp, so the target's sessions are rejected. The new role appears when the user signs in again, because roles are read from the cookie principal. Roles are seeded by migration with fixed identifiers (`Roles.AdminId`, `Roles.UserId`), so the check never has to create a role.
- **Bootstrap grants do not take the lock.** The Admin bootstrap runs during confirmation, without the Admin row lock. This is safe because a grant never lowers the number of Admins. The lock is taken only when the count can go down (role change).

## Sources
- Microsoft Learn, cookie authentication, "React to back-end changes" (`OnValidatePrincipal`, `RejectPrincipal`): https://learn.microsoft.com/en-us/aspnet/core/security/authentication/cookie
- Microsoft Learn, `SecurityStampValidator` and `SignInManager.RefreshSignInAsync`: https://learn.microsoft.com/en-us/dotnet/api/microsoft.aspnetcore.identity.securitystampvalidator and https://learn.microsoft.com/en-us/dotnet/api/microsoft.aspnetcore.identity.signinmanager-1.refreshsigninasync
- Microsoft Learn, HybridCache (L1 and L2, `DisableLocalCache`, tags, expiration): https://learn.microsoft.com/en-us/aspnet/core/performance/caching/hybrid

## Consequences
- Revocation is effective on the next request of every instance. The test `CrossInstanceRevocationTests` runs two API instances that share Redis and checks that a stamp change ends the other instance's session while the acting session stays signed in.
- Cost: one Redis read per authenticated request. On the single VPS this is sub-millisecond on the private network. The per-request cost is accepted and documented here.
- When Redis is down, every authenticated request reads the database. This is correct but adds load, and it is accepted for one VPS.
- The cookie size does not change, and the cookie is not rewritten per request.
- Any new slice that changes a security stamp must call `StampChangedAsync` after the commit (rule in `AGENTS.md`). Without it, the other sessions stay valid until the cache entry expires, up to 60 s.

## Residual risks (accepted)
1. **Stale window after a failed invalidation.** If the overwrite and the removal both fail after a commit, a session with the old stamp can pass for at most 60 s. This is logged at error level.
2. **Short race after commit.** A request that reads the cache between the commit and the overwrite can still see the old stamp. The window is the duration of the post-commit call.
3. **Redis outage raises database load.** Every request reads the database until Redis returns. The database check still fails safe.
4. **Single-instance assumption of other limits.** Rate limits stay in memory (ADR 0002). This ADR covers only the session stamp.
