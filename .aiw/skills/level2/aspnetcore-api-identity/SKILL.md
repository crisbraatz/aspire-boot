---
name: aspnetcore-api-identity
description: Load when writing API endpoints, authentication/authorization, Identity, antiforgery, rate limiting, Problem Details, OpenAPI/Scalar or proxy settings in AspireBoot.Api.
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
  - https://learn.microsoft.com/en-us/aspnet/core/security/authentication/accconfirm — Microsoft
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

## Identity and cookies
- `AddIdentityCore<User>().AddRoles<Role>().AddEntityFrameworkStores<AppDbContext>().AddSignInManager().AddDefaultTokenProviders()` + `AddAuthentication(IdentityConstants.ApplicationScheme).AddIdentityCookies()`.
- Options: `SignIn.RequireConfirmedEmail = true`, `User.RequireUniqueEmail = true`, `Lockout` enabled for new users (for example 5 attempts / 15 min), and a password policy following OWASP: min length 12+, max 128+, all characters allowed, no composition theater.
- Cookie: `__Host-` prefix, `HttpOnly`, `Secure` (Always), `SameSite=Strict` (Lax only if an external OAuth flow needs it), sliding expiration plus an absolute lifetime, and "remember me" only by explicit choice. API endpoints return 401/403, never redirects.
- Sessions: regenerate on sign-in and privilege change. Server-side session tickets (Redis `ITicketStore`) allow listing and revoking sessions. Validate the security stamp so password or role changes revoke other sessions.
- Generic responses for login, recovery and registration (no account enumeration). Same status code and similar timing.
- Re-authenticate (current password) before changing email or password. Email change confirms the new address. Notify the old one.
- Tokens (confirmation and reset) have a short lifespan (`DataProtectionTokenProviderOptions.TokenLifespan`, for example 2h for reset). Links point to the SPA, which posts the token to the API.

## Authorization (RBAC)
- **Fallback policy = authenticated user** (`SetFallbackPolicy`), so new endpoints are secure by default. Opt out explicitly with `.AllowAnonymous()`.
- Named policies only (`Policies.ManageUsers`, `Policies.ViewUsers`) built from roles/permissions. No role strings scattered in endpoints.
- Resource-based checks (for example "edit own profile OR admin") use an `IAuthorizationHandler`. Never trust IDs from the client to imply ownership.

## Antiforgery (SPA, cookie auth)
- `AddAntiforgery(o => { o.HeaderName = "X-XSRF-TOKEN"; o.Cookie.SecurePolicy = Always; })` + `app.UseAntiforgery()` after auth.
- Issue a readable `XSRF-TOKEN` cookie (HttpOnly=false) from a GET endpoint. Angular `HttpClient` echoes it automatically on mutating requests.
- Minimal APIs auto-validate only form posts. **Validate JSON POST/PUT/PATCH/DELETE explicitly** via an endpoint filter calling `IAntiforgery.ValidateRequestAsync` on the API group.

## Rate limiting
- `AddRateLimiter` + `UseRateLimiter()` (after routing). Strict policies on auth endpoints (sign-in, register, recovery, resend confirmation), partitioned by a normalized key (for example a hash of IP + email), never raw unbounded input. `OnRejected` returns 429 with `Retry-After`.
- Email-sending endpoints also have a per-account cooldown in Redis.

## Data Protection
- `PersistKeysToDbContext<AppDbContext>()` + `SetApplicationName("AspireBoot")` (identical in Api and Worker) + `ProtectKeysWithCertificate(...)` in production. A shared key ring is required for cookies and antiforgery.

## Proxy and HTTPS
- `UseForwardedHeaders` first, with `XForwardedFor | XForwardedProto`, and `KnownNetworks`/`KnownProxies` set to the Caddy network only. Restrict `AllowedHosts`.
- Caddy terminates TLS. The API sets HSTS only when behind it correctly.

## OpenAPI and Scalar
- `AddOpenApi()` + `MapOpenApi()` + `MapScalarApiReference()` **only in Development**. Never expose docs UIs in production.

## Middleware order
ForwardedHeaders → ExceptionHandler → StatusCodePages → HSTS (prod) → Routing → RateLimiter → Authentication → Authorization → Antiforgery → endpoints.

## Pitfalls
- Missing fallback policy, which makes endpoints public.
- Trusting `X-Forwarded-*` from any peer.
- Per-IP-only lockout. Lockout must be per account.
- Different error messages for "user not found" and "wrong password".
- Scalar/OpenAPI exposed in production.
