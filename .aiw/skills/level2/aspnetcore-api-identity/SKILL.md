---
name: aspnetcore-api-identity
description: Load when writing API endpoints, authentication/authorization, passkey (WebAuthn) Identity flows, antiforgery, rate limiting, Problem Details, OpenAPI/Scalar or proxy settings in AspireBoot.Api.
level: 2
kind: technology
version: ASP.NET Core 10
sources:
  - https://learn.microsoft.com/en-us/aspnet/core/fundamentals/minimal-apis/route-handlers — Microsoft
  - https://learn.microsoft.com/en-us/aspnet/core/fundamentals/minimal-apis/min-api-filters — Microsoft
  - https://learn.microsoft.com/en-us/aspnet/core/fundamentals/error-handling — Microsoft
  - https://learn.microsoft.com/en-us/aspnet/core/fundamentals/openapi/overview — Microsoft
  - https://learn.microsoft.com/en-us/aspnet/core/fundamentals/openapi/using-openapi-documents — Microsoft
  - https://learn.microsoft.com/en-us/aspnet/core/security/authentication/cookie — Microsoft
  - https://learn.microsoft.com/en-us/aspnet/core/security/authentication/passkeys/?view=aspnetcore-10.0 — Microsoft
  - https://learn.microsoft.com/en-us/dotnet/api/microsoft.aspnetcore.identity.identitypasskeyoptions?view=aspnetcore-10.0 — Microsoft
  - https://learn.microsoft.com/en-us/aspnet/core/security/authorization/policies — Microsoft
  - https://learn.microsoft.com/en-us/aspnet/core/security/anti-request-forgery — Microsoft
  - https://learn.microsoft.com/en-us/aspnet/core/performance/rate-limit — Microsoft
  - https://learn.microsoft.com/en-us/aspnet/core/security/data-protection/configuration/overview — Microsoft
  - https://learn.microsoft.com/en-us/aspnet/core/host-and-deploy/proxy-load-balancer — Microsoft
  - https://cheatsheetseries.owasp.org/cheatsheets/Authentication_Cheat_Sheet.html — OWASP
  - https://cheatsheetseries.owasp.org/cheatsheets/Session_Management_Cheat_Sheet.html — OWASP
  - model-knowledge
fetched: 2026-11-25
---

# ASP.NET Core 10 API + Identity

## Endpoints (Minimal APIs, one per slice)
- Each slice exposes `static void Map(IEndpointRouteBuilder group)`. Context groups are created once (`app.MapGroup("/api/users").WithTags("Users")`) and apply auth, filters and rate limits at group level.
- Return `TypedResults` with an explicit union return type (`Results<Ok<T>, NotFound, ValidationProblem>`) so OpenAPI is accurate. Endpoint names are globally unique (`.WithName("ListUsers")`).
- Validation: use .NET 10 built-in Minimal API validation (`builder.Services.AddValidation()` with DataAnnotations on request records), plus domain value-object factories. Verify the API against current docs before first use. Return `TypedResults.ValidationProblem`.
- Handlers take `CancellationToken` and pass it everywhere. Async all the way.
- Pagination: `page`/`pageSize` with a max (for example 100) or keyset. Never return unbounded lists.

## Errors
- `AddProblemDetails()` + `UseExceptionHandler()` + `UseStatusCodePages()`. Every error is RFC 9457 Problem Details with a `traceId` extension.
- Never leak exception messages or stack traces outside Development. Map domain errors to stable `type`/`title` codes that the frontend translates.
- `IExceptionHandler` returning true must write the full response.

## Identity and cookies (passwordless, ADR 0003)
- `AddIdentityCore<User>(o => { o.Stores.SchemaVersion = PersistenceExtensions.IdentitySchemaVersion; ... })` with `.AddRoles<Role>().AddEntityFrameworkStores<AppDbContext>().AddSignInManager().AddDefaultTokenProviders()` + `AddAuthentication(IdentityConstants.ApplicationScheme).AddIdentityCookies()`.
- **No password APIs.** Never call `PasswordSignInAsync`, `CheckPasswordSignInAsync`, `AddPasswordAsync`, or read/write `PasswordHash`. `NoPasswordApisTests` fails on any of them in Api or Core. No password options, password policy or lockout are configured.
- Options: `SignIn.RequireConfirmedEmail = true`, `User.RequireUniqueEmail = true`. Schema version is `Version3` (passkey table), set through `PersistenceExtensions.IdentitySchemaVersion` in both hosts.
- Passkeys (`IdentityPasskeyOptions`, configured in `SecurityExtensions`): `ServerDomain` from `Passkeys:ServerDomain`, `ResidentKeyRequirement = "required"`, and `ValidateOrigin` that allows only the exact `Passkeys:AllowedOrigins`. Never leave `ServerDomain` empty, because the framework would infer it from the request host.
- Ceremony pattern:
  - Registration: `SignInManager.MakePasskeyCreationOptionsAsync(PasskeyUserEntity)` returns options, then `PerformPasskeyAttestationAsync(credentialJson)` returns `PasskeyAttestationResult`. Save the passkey with `UserManager.AddOrUpdatePasskeyAsync`.
  - Sign-in: `MakePasskeyRequestOptionsAsync(null)` for discoverable credentials, then `PerformPasskeyAssertionAsync` and `UserManager.AddOrUpdatePasskeyAsync` (to update the sign counter), then `PasskeySignInAsync`.
  - Identity 10 has no `isPersistent` overload on `PasskeySignInAsync`. "Keep me signed in" is implemented by calling `SignInAsync(user, isPersistent)` after a verified assertion.
- Passkey management: `UserManager.GetPasskeysAsync`, `GetPasskeyAsync`, `RemovePasskeyAsync`, `FindByPasskeyIdAsync`. `UserPasskeyInfo` has `CreatedAt` but no last-used timestamp.
- Credential JSON is capped at 16 KB, and passkey names are 1 to 50 characters.
- Cookie: `__Host-` prefix, `HttpOnly`, `Secure` (Always), `SameSite=Strict`, sliding expiration plus an 8 h lifetime, and persistence only by explicit choice. API endpoints return 401/403, never redirects.
- Security stamp (ADR 0005): every authenticated request is checked in the cookie `OnValidatePrincipal` handler (`SecurityStampPrincipalValidator`). It compares the cookie stamp with the current stamp from `ISecurityStampCache` (HybridCache, key `identity:stamp:{id}`, 60 s maximum lifetime, `DisableLocalCache`, no tags). Every slice that changes a stamp calls `UpdateSecurityStampAsync` in its transaction, then `StampChangedAsync` after the commit. Never skip `StampChangedAsync`, and never add an interval check.
- Cache failure falls back to the database; database failure rejects the principal. Never fail open.
- Generic responses for sign-up, resend and recovery (`202` with the same body, no account enumeration) and one generic `401` for sign-in failures. Keep the timing close, and note the accepted residual risk in ADR 0003.
- Confirmation and recovery tokens are Identity email tokens and a custom `PasskeyRecoveryTokenProvider`. Both last 2 hours (`DataProtectionTokenProviderOptions.TokenLifespan`). They are single use because completing a flow updates the security stamp. Links point to the SPA, which posts the token to the API in the body. Tokens are never logged.
- Atomic flows: a change that saves a passkey and confirms the email runs inside one `Database.BeginTransactionAsync`. Removing a passkey locks the account row with `SELECT ... FOR UPDATE` before counting, so the last passkey cannot be removed by two parallel requests.

## Authorization (RBAC)
- **Fallback policy = authenticated user** (`SetFallbackPolicy`), so new endpoints are secure by default. Opt out explicitly with `.AllowAnonymous()`.
- Named policies only (`Policies.ManageUsers`, `Policies.ViewUsers`) built from roles/permissions. No role strings scattered in endpoints.
- Resource-based checks (for example "edit own profile OR admin") use an `IAuthorizationHandler`. Never trust IDs from the client to imply ownership.
- Admin (feature 003): `AuthorizationPolicies.Admin` requires the Admin role. `/api/identity/admin` maps the whole group with `RequireAuthorization(Admin)` and `RateLimitPolicies.Admin` (60 per 60 s, per user). The authorization middleware runs before the endpoint antiforgery filter, so a non-admin unsafe request gets 403 first.
- Each user has one role (`User` by default). Role changes go through `RoleAssignment` (`identity.selfDemotion`, `identity.lastAdmin`, `identity.invalidRole`) under the Admin row lock. Never change a role outside that slice.
- Bootstrap: `Bootstrap:AdminEmail` (`Bootstrap__AdminEmail`) grants Admin on confirmation only, through `RoleAssignment.ShouldBootstrapAdmin`. Recovery never grants Admin.
- Admin listing: `search` uses a parameterized `ILIKE` with `%` and `_` escaped, `pageSize` 1 to 100, and no unbounded lists.

## Antiforgery (SPA, cookie auth)
- `AddAntiforgery(o => { o.HeaderName = "X-XSRF-TOKEN"; o.Cookie.SecurePolicy = Always; })` + `app.UseAntiforgery()` after auth.
- Issue a readable `XSRF-TOKEN` cookie (HttpOnly=false) from a GET endpoint. Angular `HttpClient` echoes it automatically on mutating requests.
- `GET /api/antiforgery/token` issues the readable `XSRF-TOKEN` cookie. The SPA refreshes it after sign-in and sign-out, because the token is bound to the session.
- Minimal APIs auto-validate only form posts. **Validate JSON POST/PUT/PATCH/DELETE explicitly** via an endpoint filter calling `IAntiforgery.ValidateRequestAsync` on the API group.

## Rate limiting (ADR 0002)
- **Architecture rule:** every endpoint under `/api` declares one named policy from `RateLimitPolicies` (`signup`, `email-link`, `passkey-ceremony`, `authenticated`, `public`) with `.RequireRateLimiting(...)` on its group or endpoint. Never add a bare `/api` endpoint without a policy. `RateLimitCoverageTests` fails when one is missing.
- Partition: `user:{NameIdentifier}` when authenticated, otherwise `ip:{RemoteIp}`. Use `RateLimitPartition.GetFixedWindowLimiter` with a fixed window, `QueueLimit = 0`.
- Limits come from `RateLimiting:<policy>:PermitLimit` and `WindowSeconds`, with defaults in `RateLimitingExtensions`.
- `OnRejected` writes Problem Details (`type` and `code` `rateLimited`, status 429) and sets `Retry-After` from the lease metadata (`MetadataName.RetryAfter`), in whole seconds, at least 1.
- Counters are in memory. This is correct for one API instance on one VPS. Move to a Redis-backed limiter when there is more than one instance (ADR 0002).
- Email-sending endpoints also have a per-email cooldown (`EmailCooldown`, a SHA-256 hash of the normalized email in `IDistributedCache`, `Identity:EmailCooldownSeconds`). Known residual risk: the check and set are not atomic.
- Tests: `Integration/Platform/RateLimiting/RateLimitCoverageTests` enumerates `EndpointDataSource`. The API test factory uses high limits in the `Testing` environment, and only the 429 test uses `PermitLimit = 1`.

## Data Protection
- `PersistKeysToDbContext<AppDbContext>()` + `SetApplicationName("AspireBoot")` (identical in Api and Worker) + `ProtectKeysWithCertificate(...)` in production. A shared key ring is required for cookies and antiforgery.

## Proxy and HTTPS
- `UseForwardedHeaders` first, with `XForwardedFor | XForwardedProto`, `ForwardLimit = 1`, and `KnownIPNetworks` set from `ReverseProxy:KnownNetworks` to the Caddy network only. Wildcard networks are rejected. Restrict `AllowedHosts`. Rate-limit partitions depend on this, so a spoofed `X-Forwarded-For` from an untrusted peer must not change them.
- Caddy terminates TLS. The API sets HSTS only when behind it correctly.

## OpenAPI and Scalar
- `AddOpenApi()` + `MapOpenApi()` + `MapScalarApiReference()` **only in Development**. Never expose docs UIs in production.

## Middleware order
ForwardedHeaders → ExceptionHandler → StatusCodePages → HSTS (prod) → Routing → RateLimiter → Authentication → Authorization → Antiforgery → endpoints.

## Pitfalls
- Missing fallback policy, which makes endpoints public.
- Trusting `X-Forwarded-*` from any peer, or partitioning rate limits by IP for signed-in users.
- Adding a `/api` endpoint without a rate-limit policy.
- Different error messages or status codes for "user exists" and "user unknown" on the public flows.
- Calling a password API, or re-adding a password option, which breaks ADR 0003.
- Changing a passkey without `UserManager.AddOrUpdatePasskeyAsync`, which leaves the sign counter stale.
- Scalar/OpenAPI exposed in production.
